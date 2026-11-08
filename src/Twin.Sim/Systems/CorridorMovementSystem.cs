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
                unit.CorridorProgress = progress;
                Plan(simulation, world, unit, profile, progress);
            }
            else if (progress.State == CorridorState.Waiting)
            {
                CheckForAnswer(simulation, world, unit, profile, progress);
            }

            if (progress.State == CorridorState.Moving)
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
                progress.Permits.Add(new CrossingPermit(boundary, progress.Request.At, leader.PermitRadiusMeters));
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
            if ((!respectingUsable || respecting.CostSeconds > threshold * reference.CostSeconds) &&
                Pathfinder.FirstUnpermittedCrossing(world, FullRoute(from, reference.Waypoints, to), corridor, progress.Permits) is { } crossing)
            {
                progress.State = CorridorState.Waiting;
                progress.Request = crossing;
                progress.RequestedAt = simulation.TimeSeconds;
                CombatQueries.SetActivity(unit, UnitActivity.WaitingForPermission);
                simulation.Log(new PermissionRequested(simulation.TimeSeconds, unit.Id, crossing.BoundaryId, crossing.At));
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
        CombatQueries.SetActivity(unit, UnitActivity.Moving);
        simulation.Log(new PathPlanned(simulation.TimeSeconds, unit.Id, respecting.Waypoints, respecting.CostSeconds));
    }

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
        if (progress.Next >= route.Count)
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

    public List<Vec2> Route { get; set; } = [];

    public int Next { get; set; }
}
