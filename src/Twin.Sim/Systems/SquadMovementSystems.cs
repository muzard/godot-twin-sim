using Twin.Sim.Corridors;
using Twin.Sim.Events;
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

            if (squad.MovementOrder is not null && squad.OrderRequestedAt is null && leader.Health != Combat.Health.OutOfAction &&
                leader.Drill is null && leader.CurrentOrder is not (MoveTo or MoveThroughCorridor))
            {
                squad.ClearMovementOrder(); // done, or stopped: a successor has nothing to carry on
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

            AnswerMemberRequest(simulation, squad, leaderId);
            squad.LeaderHeld = squad.PendingRequest is not null || Held(simulation, squad, leaderId, doctrine);
            leader.HeldBySquad = squad.LeaderHeld;
        }
    }

    /// <summary>
    /// A member's request to cross a boundary (S2b): after the relay time the leader asks the platoon leader (the scripted
    /// leader), unless a squad permit already covers it; a grant becomes a squad permit, a denial or timeout leaves the
    /// member on its side.
    /// </summary>
    private static void AnswerMemberRequest(Simulation simulation, Squad squad, string leaderId)
    {
        if (squad.PendingRequest is not { } request)
        {
            return;
        }

        var now = simulation.TimeSeconds;
        if (!squad.IsFollowing(request.UnitId) || simulation.GetUnit(request.UnitId).Health == Combat.Health.OutOfAction)
        {
            squad.PendingRequest = null;
            return;
        }

        if (request.LeaderAskedAt is not { } askedAt)
        {
            if (now >= request.AskedAt + simulation.Config.SquadDoctrine.RelaySeconds - 1e-9)
            {
                if (squad.Permits.Any(p => p.Covers(request.BoundaryId, request.At)))
                {
                    squad.PendingRequest = null;
                }
                else
                {
                    simulation.Log(new PermissionRequested(now, leaderId, request.BoundaryId, request.At));
                    squad.PendingRequest = request with { LeaderAskedAt = now, HalfWidth = SquadSlots.HalfWidth(simulation, squad) };
                }
            }

            return;
        }

        var platoonLeader = simulation.Config.Leader;
        var timeout = simulation.Config.Permission.TimeoutSeconds;
        var waited = now - askedAt;
        if (platoonLeader is { Answer: not Command.LeaderAnswer.NoAnswer } && platoonLeader.DelaySeconds <= timeout &&
            waited >= platoonLeader.DelaySeconds - 1e-9)
        {
            simulation.Log(new PermissionAnswered(now, leaderId, request.BoundaryId, platoonLeader.Answer));
            if (platoonLeader.Answer == Command.LeaderAnswer.Grant)
            {
                squad.AddPermit(new CrossingPermit(request.BoundaryId, request.At, platoonLeader.PermitRadiusMeters + request.HalfWidth));
            }
            else
            {
                squad.Refuse(request.BoundaryId);
            }

            squad.PendingRequest = null;
        }
        else if (waited >= timeout - 1e-9)
        {
            simulation.Log(new PermissionTimedOut(now, leaderId, request.BoundaryId));
            squad.Refuse(request.BoundaryId);
            squad.PendingRequest = null;
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
        var deployingAtEdge = SquadSlots.GoingToDeployAtEdge(simulation, squad);
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

            if (deployingAtEdge && SquadSlots.FirstUnpermittedCrossing(simulation, squad, [unit.Position.Xy, slots[member.UnitId]])
                is { BoundaryId: HardBoundary.ForestEdgeId })
            {
                continue; // it waits at the edge for the leader to come up and deploy the squad (S2c)
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
        var goRound = false;
        if (corridor is not null && CrossingAhead(simulation, squad, position, slot) is var (crossing, stopAt))
        {
            if (squad.WasRefused(crossing.BoundaryId))
            {
                goRound = true; // refused under this order: it finds a way round, which the path search keeps to
            }
            else if (position.DistanceTo(stopAt) > 1e-9)
            {
                slot = stopAt; // S2b: it walks up to the boundary first
            }
            else if (crossing.BoundaryId == HardBoundary.ForestEdgeId && SquadSlots.GoingToDeployAtEdge(simulation, squad))
            {
                member.Velocity = Vec3.Zero; // the leader deploys the squad at the edge and asks for it (S2c)
                CombatQueries.SetActivity(member, UnitActivity.Idle);
                return;
            }
            else
            {
                WaitForPermission(simulation, squad, member, crossing); // then waits there and asks the leader
                return;
            }
        }

        List<Vec2>? route = !goRound && StraightUsable(simulation, member, profile, corridor, position, slot)
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

    /// <summary>A member stops this short of a boundary it may not cross yet, in metres.</summary>
    private const double StopShortMeters = 0.25;

    /// <summary>
    /// The first boundary the squad has no permit for on the straight way from the member to its slot, with the point where
    /// the member stops short of it: just before a drawn line, or at the last point still inside the forest. Null if none.
    /// </summary>
    private static (BoundaryCrossing Crossing, Vec2 StopAt)? CrossingAhead(Simulation simulation, Squad squad, Vec2 from, Vec2 to)
    {
        var samples = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / SampleMeters));
        var previous = from;
        for (var i = 1; i <= samples; i++)
        {
            var point = from + ((to - from) * ((double)i / samples));
            if (SquadSlots.FirstUnpermittedCrossing(simulation, squad, [previous, point]) is { } crossing)
            {
                if (crossing.BoundaryId == HardBoundary.ForestEdgeId)
                {
                    return (crossing, previous);
                }

                var before = Math.Max(0, from.DistanceTo(crossing.At) - StopShortMeters);
                return (crossing, from + ((to - from).Normalized() * before));
            }

            previous = point;
        }

        return null;
    }

    /// <summary>
    /// The member waits at a boundary for permission (S2b). It asks the leader unless a request is already on its way.
    /// </summary>
    private static void WaitForPermission(Simulation simulation, Squad squad, Unit member, BoundaryCrossing crossing)
    {
        member.Velocity = Vec3.Zero;
        CombatQueries.SetActivity(member, UnitActivity.WaitingForPermission);
        if (squad.PendingRequest is null)
        {
            squad.PendingRequest = new Squad.MemberRequest(member.Id, crossing.BoundaryId, crossing.At, simulation.TimeSeconds);
            simulation.Log(new PermissionRequested(simulation.TimeSeconds, member.Id, crossing.BoundaryId, crossing.At));
        }
    }

    /// <summary>True if the straight move stays on passable ground inside the corridor and crosses no cliff or unpermitted boundary.</summary>
    private static bool StraightUsable(Simulation simulation, Unit member, MovementProfile profile, Corridor? corridor, Vec2 from, Vec2 to)
    {
        var samples = (int)Math.Ceiling(from.DistanceTo(to) / SampleMeters);
        var points = new List<Vec2>(samples + 1) { from };
        for (var i = 1; i <= samples; i++)
        {
            var point = from + ((to - from) * ((double)i / samples));
            if (!SquadSlots.CanStand(simulation, member, corridor, point))
            {
                return false;
            }

            points.Add(point);
        }

        if (corridor is not null && simulation.SquadOf(member.Id) is { } squad &&
            SquadSlots.FirstUnpermittedCrossing(simulation, squad, points) is not null)
        {
            return false; // it would cross a boundary the squad has no permit for
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

        var options = new PathOptions(corridor, simulation.SquadOf(member.Id)?.Permits ?? [], Window: window);
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
