using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Squad cohesion (backlog 0004, S2a). Once a squad leader has a movement order (<see cref="MoveTo"/> or
/// <see cref="MoveThroughCorridor"/>) its squad follows it. The leader moves at <see cref="SquadDoctrine.LeaderSpeedFactor"/>
/// of its speed and is held, keeping its order, while the squad is not with it: while any following member is pinned or
/// out of action (nobody is left behind), or once a member is farther than <see cref="SquadDoctrine.StragglerMeters"/> from
/// its slot, until every member is within <see cref="SquadDoctrine.RegroupMeters"/> of it. Runs after the members have
/// moved (<see cref="SquadFollowSystem"/>) and before the leader moves, so it judges where the members are now.
/// </summary>
public sealed class SquadCohesionSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var doctrine = simulation.Config.SquadDoctrine;
        foreach (var squad in simulation.Squads)
        {
            foreach (var member in squad.Members)
            {
                var unit = simulation.GetUnit(member.UnitId);
                unit.HeldBySquad = false;
                unit.SquadSpeedFactor = 1;
            }

            if (squad.LeaderId is not { } leaderId)
            {
                continue;
            }

            var leader = simulation.GetUnit(leaderId);
            switch (leader.CurrentOrder)
            {
                case MoveThroughCorridor order:
                    squad.Corridor = order.Corridor;
                    squad.MovementStarted = true;
                    break;
                case MoveTo:
                    squad.Corridor = null;
                    squad.MovementStarted = true;
                    break;
                default:
                    break;
            }

            if (!squad.MovementStarted)
            {
                continue;
            }

            if (squad.TrailLeaderId != leaderId)
            {
                squad.LeaderTrail.Clear();
                squad.LeaderTrail.Add(leader.Position.Xy);
                squad.TrailLeaderId = leaderId;
            }

            if (leader.Drill is null)
            {
                leader.SquadSpeedFactor = doctrine.LeaderSpeedFactor; // not in 0003's drill, where it rushes like anyone
            }

            squad.LeaderHeld = Held(simulation, squad, leaderId, doctrine);
            leader.HeldBySquad = squad.LeaderHeld;
        }
    }

    private static bool Held(Simulation simulation, Squad squad, string leaderId, SquadDoctrine doctrine)
    {
        if (SquadSlots.Compute(simulation, squad) is not { } slots)
        {
            return false; // nothing to follow: the leader is not waiting for anyone
        }

        var anyFar = false;
        var allNear = true;
        foreach (var member in squad.Members)
        {
            if (member.UnitId == leaderId || !squad.IsFollowing(member.UnitId))
            {
                continue;
            }

            var unit = simulation.GetUnit(member.UnitId);
            if (SquadSlots.IsCasualty(unit))
            {
                return true;
            }

            var distance = unit.Position.Xy.DistanceTo(slots[member.UnitId]);
            anyFar |= distance > doctrine.StragglerMeters;
            allNear &= distance <= doctrine.RegroupMeters;
        }

        return squad.LeaderHeld ? !allNear : anyFar;
    }
}

/// <summary>
/// Squad members follow their leader (backlog 0004, S2a): each following member moves toward its slot at its own terrain
/// speed, in a straight line when it can, otherwise by a path search in a window of
/// <see cref="SquadDoctrine.LocalSearchMeters"/> around itself. It never leaves the corridor's outer limit or crosses a
/// cliff. Members that cannot move (pinned, suppressed, changing posture, out of action) or are in the drill under fire
/// stay. Runs before the leader moves: members head for their slots around where the leader is at the start of the step,
/// and the leader's trail is recorded.
/// </summary>
public sealed class SquadFollowSystem : ISimSystem
{
    /// <summary>Extra length of the leader's trail kept beyond the deepest possible slot (a file), in metres.</summary>
    private const double TrailMarginMeters = 50;

    /// <summary>The leader's trail gets a new point once the leader is this far from the last one.</summary>
    private const double TrailStepMeters = 0.5;

    /// <summary>Spacing of the samples that check a straight move.</summary>
    private const double SampleMeters = 0.5;

    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var squad in simulation.Squads)
        {
            if (!squad.MovementStarted || squad.LeaderId is not { } leaderId)
            {
                continue;
            }

            var leader = simulation.GetUnit(leaderId);
            if (leader.Drill is null && squad.TrailLeaderId == leaderId)
            {
                var keep = (squad.Members.Count * simulation.Config.SquadDoctrine.IntervalMeters) + TrailMarginMeters;
                RecordTrail(squad, leader.Position.Xy, keep);
            }

            if (SquadSlots.Compute(simulation, squad) is not { } slots)
            {
                Hold(simulation, squad, leaderId);
                continue;
            }

            foreach (var (unitId, slot) in slots)
            {
                if (unitId != leaderId)
                {
                    Follow(simulation, squad, simulation.GetUnit(unitId), slot, dtSeconds);
                }
            }
        }
    }

    /// <summary>Members with nothing to follow stay where they are.</summary>
    private static void Hold(Simulation simulation, Squad squad, string leaderId)
    {
        foreach (var member in squad.Members)
        {
            var unit = simulation.GetUnit(member.UnitId);
            if (member.UnitId != leaderId && squad.IsFollowing(member.UnitId) && unit.CurrentOrder is null &&
                unit.Drill is null && !unit.IsImmobilized)
            {
                unit.Velocity = Vec3.Zero;
                CombatQueries.SetActivity(unit, UnitActivity.Idle);
            }
        }
    }

    private static void RecordTrail(Squad squad, Vec2 leaderAt, double keepMeters)
    {
        var trail = squad.LeaderTrail;
        if (trail.Count > 0 && trail[^1].DistanceTo(leaderAt) < TrailStepMeters)
        {
            return;
        }

        trail.Add(leaderAt);
        var length = 0.0;
        for (var i = trail.Count - 1; i > 0; i--)
        {
            length += trail[i].DistanceTo(trail[i - 1]);
            if (length > keepMeters)
            {
                trail.RemoveRange(0, i - 1);
                break;
            }
        }
    }

    private static void Follow(Simulation simulation, Squad squad, Unit member, Vec2 slot, double dt)
    {
        if (member.IsImmobilized || member.Drill is not null || member.CurrentOrder is not null || member.Profile is not { } profile)
        {
            return;
        }

        var previous = member.Position;
        var position = previous.Xy;
        if (position.DistanceTo(slot) <= 1e-9)
        {
            member.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(member, UnitActivity.Idle);
            return;
        }

        // A member outside the corridor (back from a detached order, for example) heads into it unconstrained.
        var corridor = squad.Corridor is { } inside && Segments.DistanceToPolyline(position, inside.Centerline) <= inside.OuterLimit
            ? inside
            : null;
        List<Vec2>? route = StraightUsable(simulation, member, profile, corridor, position, slot)
            ? [position, slot]
            : LocalRoute(simulation, member, profile, corridor, position, slot);
        if (route is null)
        {
            member.Velocity = Vec3.Zero;
            CombatQueries.SetActivity(member, UnitActivity.Idle); // no way to its slot from here for now
            return;
        }

        var remaining = dt;
        var next = 1;
        while (remaining > 0 && next < route.Count)
        {
            var target = route[next];
            var speed = SegmentSpeed(simulation, profile, route[next - 1], target) * member.SpeedFactor;
            var needed = position.DistanceTo(target) / speed;
            if (needed <= remaining)
            {
                position = target;
                remaining -= needed;
                next++;
            }
            else
            {
                position += (target - position).Normalized() * (speed * remaining);
                remaining = 0;
            }
        }

        member.Position = position.WithZ(simulation.Terrain.HeightAt(position));
        member.Velocity = (member.Position - previous) * (1 / dt);
        CombatQueries.SetActivity(member, UnitActivity.Moving);
    }

    /// <summary>True if the straight move stays on passable ground inside the corridor and crosses no cliff.</summary>
    private static bool StraightUsable(Simulation simulation, Unit member, MovementProfile profile, Corridor? corridor, Vec2 from, Vec2 to)
    {
        var samples = (int)Math.Ceiling(from.DistanceTo(to) / SampleMeters);
        for (var i = 1; i <= samples; i++)
        {
            if (!SquadSlots.CanStand(simulation, member, corridor, from + ((to - from) * ((double)i / samples))))
            {
                return false;
            }
        }

        return simulation.Terrain is not IWorldQuery world || !Pathfinder.CrossesCliff(world, profile, [from, to]);
    }

    /// <summary>
    /// A way to the slot found by a path search in the member's local window, or null. The search starts from the
    /// nearest cell center the member can walk to straight (its own cell's center may lie across a cliff or behind it),
    /// and the route then goes straight to the farthest waypoint it can, so replanning every step never steps back.
    /// </summary>
    private static List<Vec2>? LocalRoute(Simulation simulation, Unit member, MovementProfile profile, Corridor? corridor, Vec2 from, Vec2 to)
    {
        if (simulation.Terrain is not IWorldQuery world)
        {
            return null;
        }

        var half = simulation.Config.SquadDoctrine.LocalSearchMeters / 2;
        var window = (new Vec2(from.X - half, from.Y - half), new Vec2(from.X + half, from.Y + half));
        var goal = Math.Abs(to.X - from.X) <= half - world.CellSize && Math.Abs(to.Y - from.Y) <= half - world.CellSize
            ? to
            : NearestStandableInWindow(simulation, member, corridor, world, window, to);
        if (goal is not { } reachable)
        {
            return null;
        }

        var options = new PathOptions(corridor, SquadPermits(simulation, member), Window: window);
        foreach (var start in NearbyCellCenters(world, from))
        {
            if (!StraightUsable(simulation, member, profile, corridor, from, start))
            {
                continue;
            }

            var path = Pathfinder.FindPath(world, profile, start, reachable, options);
            if (!path.Found)
            {
                continue;
            }

            var waypoints = new List<Vec2> { start };
            foreach (var point in path.Waypoints.Append(reachable))
            {
                if (point.DistanceTo(waypoints[^1]) > 1e-9)
                {
                    waypoints.Add(point);
                }
            }

            if (waypoints.Count > 1 && !StraightUsable(simulation, member, profile, corridor, waypoints[^2], reachable))
            {
                waypoints.RemoveAt(waypoints.Count - 1); // the last leg is not walkable: stop at the last cell center
            }

            var first = 0;
            while (first + 1 < waypoints.Count && StraightUsable(simulation, member, profile, corridor, from, waypoints[first + 1]))
            {
                first++;
            }

            return [from, .. waypoints.Skip(first)];
        }

        return null;

        static IReadOnlyList<CrossingPermit> SquadPermits(Simulation simulation, Unit member) =>
            simulation.SquadOf(member.Id) is { LeaderId: { } leaderId } &&
            simulation.GetUnit(leaderId).CorridorProgress is { } progress ? progress.Permits : [];
    }

    /// <summary>
    /// When the slot is beyond the search window: the cell center inside the window nearest to it where the member can
    /// stand, or null.
    /// </summary>
    private static Vec2? NearestStandableInWindow(
        Simulation simulation, Unit member, Corridor? corridor, IWorldQuery world, (Vec2 Min, Vec2 Max) window, Vec2 slot)
    {
        var cell = world.CellSize;
        var c0 = Math.Ceiling(((window.Min.X - world.Bounds.MinX) / cell) - 0.5);
        var r0 = Math.Ceiling(((window.Min.Y - world.Bounds.MinY) / cell) - 0.5);
        Vec2? best = null;
        for (var row = r0; world.Bounds.MinY + ((row + 0.5) * cell) <= window.Max.Y; row++)
        {
            for (var column = c0; world.Bounds.MinX + ((column + 0.5) * cell) <= window.Max.X; column++)
            {
                var center = new Vec2(world.Bounds.MinX + ((column + 0.5) * cell), world.Bounds.MinY + ((row + 0.5) * cell));
                if ((best is null || center.DistanceTo(slot) < best.Value.DistanceTo(slot)) &&
                    SquadSlots.CanStand(simulation, member, corridor, center))
                {
                    best = center;
                }
            }
        }

        return best;
    }

    /// <summary>The centers of the cell containing <paramref name="point"/> and its eight neighbours, nearest first.</summary>
    private static IEnumerable<Vec2> NearbyCellCenters(IWorldQuery world, Vec2 point)
    {
        var cell = world.CellSize;
        var column = Math.Floor((point.X - world.Bounds.MinX) / cell);
        var row = Math.Floor((point.Y - world.Bounds.MinY) / cell);
        var centers = new List<Vec2>(9);
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                centers.Add(new Vec2(
                    world.Bounds.MinX + ((column + dc + 0.5) * cell), world.Bounds.MinY + ((row + dr + 0.5) * cell)));
            }
        }

        return centers.OrderBy(c => c.DistanceTo(point)).ThenBy(c => c.Y).ThenBy(c => c.X);
    }

    /// <summary>Speed along a move: the mean of the two cells' base speeds times the slope factor (as the corridor system).</summary>
    private static double SegmentSpeed(Simulation simulation, MovementProfile profile, Vec2 a, Vec2 b)
    {
        if (simulation.Terrain is not IWorldQuery world)
        {
            return profile.OpenMps;
        }

        var sa = profile.BaseSpeed(world.ClassAt(a.X, a.Y));
        var sb = profile.BaseSpeed(world.ClassAt(b.X, b.Y));
        var baseSpeed = sa > 0 && sb > 0 ? (sa + sb) / 2 : Math.Max(sa, sb);
        var length = a.DistanceTo(b);
        var slope = length > 0 ? (world.GroundHeight(b.X, b.Y) - world.GroundHeight(a.X, a.Y)) / length : 0;
        return Math.Max(baseSpeed * profile.SlopeSpeedFactor(slope), 0.01);
    }
}
