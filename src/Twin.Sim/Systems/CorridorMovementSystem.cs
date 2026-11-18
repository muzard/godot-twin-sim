using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Carries out <see cref="MoveThroughCorridor"/> orders (spec v0.1, "Lupapyyntö"):
/// <list type="number">
/// <item>Plan a route that respects hard boundaries and a reference route that may cross them.</item>
/// <item>If the respecting route is missing or costs more than the threshold times the reference, ask the leader for
/// permission to cross the reference route's first unpermitted boundary, and wait in place.</item>
/// <item>A grant adds a permit and the agent plans again (and may ask again for the next boundary). After a denial or
/// a timeout it takes the respecting route if there is one, and otherwise stops and reports that it is blocked.</item>
/// <item>Follow the route at the local speed, v · dt per step, ending exactly on the destination.</item>
/// </list>
/// A squad leader waiting for its squad keeps its planned route but does not move (backlog 0004). A squad leader that must
/// ask to leave the forest first walks on along its reference route to a point inside the edge, commands its squad to
/// deploy in an open line there, and asks once the line has formed or the deployment time is up (S2c).
/// </summary>
public sealed class CorridorMovementSystem : ISimSystem
{
    /// <summary>Tolerance when comparing simulated times computed in different ways.</summary>
    private const double TimeEpsilon = 1e-9;

    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var unit in simulation.Units)
        {
            if (unit.CurrentOrder is not MoveThroughCorridor order || unit.Profile is not { } profile)
            {
                unit.CorridorProgress = null;
                continue;
            }

            var world = (IWorldQuery)simulation.Terrain;
            if (unit.IsImmobilized)
            {
                // Pinned, suppressed or changing posture: no movement and no new plans or requests until it ends.
                // Doctrine decides whether a permission answer that arrives meanwhile is handled now or when free.
                unit.Velocity = Vec3.Zero;
                if (unit.CorridorProgress is { State: CorridorState.Waiting } waiting && ReferenceEquals(waiting.Order, order) &&
                    simulation.Config.Combat.Doctrine.PermissionAnswers == PermissionAnswerTiming.Immediately)
                {
                    CheckForAnswer(simulation, world, unit, profile, waiting);
                }

                continue;
            }

            // A new order object starts over; re-issuing the same object after it finished does nothing more.
            if (unit.CorridorProgress is not { } progress || !ReferenceEquals(progress.Order, order))
            {
                progress = new CorridorProgress(order);
                if (simulation.SquadOf(unit.Id) is { } squad && squad.LeaderId == unit.Id)
                {
                    progress.Permits.AddRange(squad.Permits); // a squad leader carries the squad's permits (S2b)
                }

                unit.CorridorProgress = progress;
                Plan(simulation, world, unit, profile, progress);
            }
            else if (progress.State == CorridorState.Waiting)
            {
                CheckForAnswer(simulation, world, unit, profile, progress);
            }

            if (progress.State == CorridorState.Deploying)
            {
                unit.Velocity = Vec3.Zero;
                CheckDeployment(simulation, unit, progress);
            }
            else if (progress.State == CorridorState.Moving && unit.HeldBySquad)
            {
                // A squad leader waits for its squad with its route planned (backlog 0004).
                unit.Velocity = Vec3.Zero;
                CombatQueries.SetActivity(unit, UnitActivity.WaitingForSquad);
            }
            else if (progress.State == CorridorState.Moving)
            {
                Advance(simulation, world, unit, profile, progress, dtSeconds);
            }
            else
            {
                unit.Velocity = Vec3.Zero;
            }
        }
    }

    private static void CheckForAnswer(Simulation simulation, IWorldQuery world, Unit unit, MovementProfile profile, CorridorProgress progress)
    {
        var now = simulation.TimeSeconds;
        var waited = now - progress.RequestedAt;
        var leader = simulation.Config.Leader;
        var timeout = simulation.Config.Permission.TimeoutSeconds;
        var boundary = progress.Request!.BoundaryId;

        if (leader is { Answer: not LeaderAnswer.NoAnswer } && leader.DelaySeconds <= timeout &&
            waited >= leader.DelaySeconds - TimeEpsilon)
        {
            simulation.Log(new PermissionAnswered(now, unit.Id, boundary, leader.Answer));
            if (leader.Answer == LeaderAnswer.Grant)
            {
                progress.Deployment = null; // a later forest edge on the way is deployed for afresh
                progress.EdgeCrossing = null;
                progress.Permits.Add(new CrossingPermit(boundary, progress.Request.At, leader.PermitRadiusMeters));
                if (simulation.SquadOf(unit.Id) is { } squad && squad.LeaderId == unit.Id)
                {
                    // The permit covers the squad's formation (backlog 0004, S2b).
                    squad.AddPermit(new CrossingPermit(boundary, progress.Request.At, leader.PermitRadiusMeters + progress.SquadHalfWidth));
                }
            }
            else
            {
                progress.Refused = true;
            }

            Plan(simulation, world, unit, profile, progress);
        }
        else if (waited >= timeout - TimeEpsilon)
        {
            simulation.Log(new PermissionTimedOut(now, unit.Id, boundary));
            progress.Refused = true;
            Plan(simulation, world, unit, profile, progress);
        }
    }

    private static void Plan(Simulation simulation, IWorldQuery world, Unit unit, MovementProfile profile, CorridorProgress progress)
    {
        var corridor = progress.Order.Corridor;
        var from = unit.Position.Xy;
        var to = progress.Order.Destination;
        var respecting = Pathfinder.FindPath(world, profile, from, to, new PathOptions(corridor, progress.Permits));

        // The pathfinder checks arcs between cell centers; the legs from the agent to its cell center and from the
        // goal cell center to B are checked here, so a boundary inside those cells is not crossed unasked.
        var respectingRoute = FullRoute(from, respecting.Waypoints, to);
        var respectingUsable = respecting.Found && !Pathfinder.CrossesCliff(world, profile, respectingRoute) &&
            Pathfinder.FirstUnpermittedCrossing(world, respectingRoute, corridor, progress.Permits) is null;

        if (!progress.Refused)
        {
            var reference = Pathfinder.FindPath(world, profile, from, to,
                new PathOptions(corridor, progress.Permits, IgnoreHardBoundaries: true));
            if (!reference.Found || Pathfinder.CrossesCliff(world, profile, FullRoute(from, reference.Waypoints, to)))
            {
                Block(simulation, unit, progress, "no route to the destination inside the corridor");
                return;
            }

            var threshold = simulation.Config.Permission.Threshold;
            var referenceRoute = FullRoute(from, reference.Waypoints, to);
            if ((!respectingUsable || respecting.CostSeconds > threshold * reference.CostSeconds) &&
                Pathfinder.FirstUnpermittedCrossing(world, referenceRoute, corridor, progress.Permits) is { } crossing)
            {
                if (crossing.BoundaryId == HardBoundary.ForestEdgeId && progress.Deployment is null &&
                    simulation.SquadOf(unit.Id) is { } squad && squad.LeaderId == unit.Id)
                {
                    ApproachEdge(simulation, world, unit, progress, squad, referenceRoute, crossing, reference.CostSeconds);
                    return;
                }

                Request(simulation, unit, progress, crossing);
                return;
            }
        }

        if (!respectingUsable)
        {
            Block(simulation, unit, progress, "no route to the destination without crossing a hard boundary");
            return;
        }

        progress.Route = FullRoute(from, respecting.Waypoints, to);
        progress.Next = 1;
        progress.State = CorridorState.Moving;
        progress.Deployment = null; // on its way: a squad deployed at the edge re-forms
        progress.EdgeCrossing = null;
        CombatQueries.SetActivity(unit, UnitActivity.Moving);
        simulation.Log(new PathPlanned(simulation.TimeSeconds, unit.Id, respecting.Waypoints, respecting.CostSeconds));
    }

    /// <summary>Asks the leader for permission to cross and waits in place.</summary>
    private static void Request(Simulation simulation, Unit unit, CorridorProgress progress, BoundaryCrossing crossing)
    {
        progress.State = CorridorState.Waiting;
        progress.Request = crossing;
        progress.RequestedAt = simulation.TimeSeconds;
        if (simulation.SquadOf(unit.Id) is { } led && led.LeaderId == unit.Id)
        {
            // The permit will cover this width: the deployed line's at the forest edge, otherwise the formation's (S2b).
            progress.SquadHalfWidth = progress.Deployment?.HalfWidth ?? SquadSlots.HalfWidth(simulation, led);
        }

        CombatQueries.SetActivity(unit, UnitActivity.WaitingForPermission);
        simulation.Log(new PermissionRequested(simulation.TimeSeconds, unit.Id, crossing.BoundaryId, crossing.At));
    }

    /// <summary>
    /// A squad leader that must ask to leave the forest walks on along its reference route to its deployment point inside
    /// the edge first (S2c), with its squad following in formation.
    /// </summary>
    private static void ApproachEdge(
        Simulation simulation, IWorldQuery world, Unit unit, CorridorProgress progress, Squads.Squad squad,
        List<Vec2> referenceRoute, BoundaryCrossing crossing, double costSeconds)
    {
        progress.EdgeCrossing = crossing;
        progress.Route = EdgeDeployment.RouteToEdge(world, referenceRoute, crossing.At, simulation.Config.SquadDoctrine.EdgeInsetMeters);
        progress.Next = 1;
        if (progress.Route.Count < 2)
        {
            BeginDeployment(simulation, unit, progress, squad); // already at the edge
            return;
        }

        progress.State = CorridorState.Moving;
        CombatQueries.SetActivity(unit, UnitActivity.Moving);
        simulation.Log(new PathPlanned(simulation.TimeSeconds, unit.Id, progress.Route.Skip(1).ToList(), costSeconds));
    }

    /// <summary>At its deployment point the leader commands the squad into its open line along the edge (S2c).</summary>
    private static void BeginDeployment(Simulation simulation, Unit unit, CorridorProgress progress, Squads.Squad squad)
    {
        var line = EdgeDeployment.Line(simulation, squad, unit.Position.Xy, progress.EdgeCrossing!.At);
        squad.EdgeDeployment = line;
        progress.Deployment = line;
        progress.State = CorridorState.Deploying;
        progress.DeploymentCommandedAt = simulation.TimeSeconds;
        unit.Velocity = Vec3.Zero;
        CombatQueries.SetActivity(unit, UnitActivity.WaitingForSquad);
        SquadSystem.Command(simulation, squad, unit, line.Command);
    }

    /// <summary>
    /// The leader asks to leave the forest once every able member that follows the squad stands in its spot
    /// (<see cref="EdgeDeployed"/>), or when the deployment time is up.
    /// </summary>
    private static void CheckDeployment(Simulation simulation, Unit unit, CorridorProgress progress)
    {
        var line = progress.Deployment!;
        var crossing = progress.EdgeCrossing!;
        if (simulation.SquadOf(unit.Id) is not { } squad || squad.LeaderId != unit.Id)
        {
            Request(simulation, unit, progress, crossing);
            return;
        }

        var deployed = squad.Members.All(m =>
            m.UnitId == unit.Id || !squad.IsFollowing(m.UnitId) ||
            simulation.GetUnit(m.UnitId).Health == Combat.Health.OutOfAction ||
            !line.Spots.TryGetValue(m.UnitId, out var spot) || // rejoined after the line was laid out: no spot to wait for
            (line.Acted.Contains(m.UnitId) && simulation.GetUnit(m.UnitId).Position.Xy.DistanceTo(spot) <= InSpotMeters));
        if (deployed)
        {
            simulation.Log(new EdgeDeployed(simulation.TimeSeconds, unit.Id, squad.Id));
            Request(simulation, unit, progress, crossing);
        }
        else if (simulation.TimeSeconds >= progress.DeploymentCommandedAt + simulation.Config.SquadDoctrine.EdgeDeploymentSeconds - TimeEpsilon)
        {
            Request(simulation, unit, progress, crossing);
        }
        else
        {
            CombatQueries.SetActivity(unit, UnitActivity.WaitingForSquad);
        }
    }

    /// <summary>A member stands in its spot in the line when it is this close to it, in metres.</summary>
    private const double InSpotMeters = 0.5;

    /// <summary>The route the agent walks: its position, the path's cell centers, then the exact destination.</summary>
    private static List<Vec2> FullRoute(Vec2 from, IReadOnlyList<Vec2> waypoints, Vec2 to)
    {
        var route = new List<Vec2> { from };
        foreach (var p in waypoints.Append(to))
        {
            if (p != route[^1])
            {
                route.Add(p);
            }
        }

        return route;
    }

    private static void Block(Simulation simulation, Unit unit, CorridorProgress progress, string reason)
    {
        progress.State = CorridorState.Finished;
        CombatQueries.SetActivity(unit, UnitActivity.Blocked);
        unit.Velocity = Vec3.Zero;
        simulation.Log(new Blocked(simulation.TimeSeconds, unit.Id, reason));
    }

    private static void Advance(
        Simulation simulation, IWorldQuery world, Unit unit, MovementProfile profile, CorridorProgress progress, double dt)
    {
        var previous = unit.Position;
        var position = previous.Xy;
        var remaining = dt;
        var route = progress.Route;
        while (remaining > 0 && progress.Next < route.Count)
        {
            var target = route[progress.Next];
            var speed = SegmentSpeed(world, profile, route[progress.Next - 1], target) * unit.SpeedFactor;
            var distance = position.DistanceTo(target);
            var needed = distance / speed;
            if (needed <= remaining)
            {
                position = target;
                remaining -= needed;
                progress.Next++;
            }
            else
            {
                position += (target - position).Normalized() * (speed * remaining);
                remaining = 0;
            }
        }

        unit.Position = position.WithZ(world.GroundHeight(position.X, position.Y));
        if (progress.Next >= route.Count && progress.EdgeCrossing is { } edge && progress.Deployment is null)
        {
            // At its deployment point inside the forest edge (S2c); without a squad to deploy any more, it just asks.
            unit.Velocity = Vec3.Zero;
            if (simulation.SquadOf(unit.Id) is { } squad && squad.LeaderId == unit.Id)
            {
                BeginDeployment(simulation, unit, progress, squad);
            }
            else
            {
                Request(simulation, unit, progress, edge);
            }
        }
        else if (progress.Next >= route.Count)
        {
            progress.State = CorridorState.Finished;
            unit.Velocity = Vec3.Zero;
            unit.Activity = UnitActivity.Idle;
            unit.CurrentOrder = null;
            unit.CorridorProgress = null;
            simulation.Log(new Arrived(simulation.TimeSeconds, unit.Id, position));
        }
        else
        {
            unit.Velocity = (unit.Position - previous) * (1 / dt);
            CombatQueries.SetActivity(unit, UnitActivity.Moving);
        }
    }

    /// <summary>The speed along a route segment: the same rule as the pathfinder's arc speed.</summary>
    private static double SegmentSpeed(IWorldQuery world, MovementProfile profile, Vec2 a, Vec2 b)
    {
        var sa = profile.BaseSpeed(world.ClassAt(a.X, a.Y));
        var sb = profile.BaseSpeed(world.ClassAt(b.X, b.Y));
        var baseSpeed = sa > 0 && sb > 0 ? (sa + sb) / 2 : Math.Max(sa, sb);
        var length = a.DistanceTo(b);
        var slope = length > 0 ? (world.GroundHeight(b.X, b.Y) - world.GroundHeight(a.X, a.Y)) / length : 0;
        return Math.Max(baseSpeed * profile.SlopeSpeedFactor(slope), 0.01);
    }
}

/// <summary>Where a unit is in carrying out a corridor order.</summary>
internal enum CorridorState
{
    Moving,
    Waiting,
    Finished,

    /// <summary>A squad leader at the forest edge waiting for its squad to deploy before it asks (S2c).</summary>
    Deploying,
}

/// <summary>A unit's progress through one <see cref="MoveThroughCorridor"/> order.</summary>
internal sealed class CorridorProgress(MoveThroughCorridor order)
{
    public MoveThroughCorridor Order { get; } = order;

    public CorridorState State { get; set; }

    public List<CrossingPermit> Permits { get; } = [];

    /// <summary>True after a denial or timeout: the agent no longer asks and takes the respecting route if it can.</summary>
    public bool Refused { get; set; }

    public BoundaryCrossing? Request { get; set; }

    public double RequestedAt { get; set; }

    /// <summary>A squad leader's formation half-width when it asked: a grant covers the squad within radius plus this.</summary>
    public double SquadHalfWidth { get; set; }

    public List<Vec2> Route { get; set; } = [];

    /// <summary>The forest-edge crossing a squad leader is walking up to, to deploy before it asks (S2c).</summary>
    public BoundaryCrossing? EdgeCrossing { get; set; }

    /// <summary>The squad's open line at the edge, from the deployment command until the squad moves on (S2c).</summary>
    public Squads.EdgeLine? Deployment { get; set; }

    /// <summary>When the leader commanded the deployment.</summary>
    public double DeploymentCommandedAt { get; set; }

    public int Next { get; set; }
}
