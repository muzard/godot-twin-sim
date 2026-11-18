using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Where each member of a moving squad should be now (backlog 0004, S2a). A slot is <c>(along, across)</c> relative to
/// the leader (<see cref="FormationSlots"/>), laid out along the leader's path: the path it has walked behind it, its
/// planned route ahead, both extended straight beyond their ends. A formation wider than the corridor's inner zone is
/// compressed evenly to fit, or becomes a file when the lateral gap between neighbours would fall below the minimum. A slot
/// in impassable terrain or outside the corridor's outer limit moves toward the leader's path to the nearest usable point.
/// </summary>
internal static class SquadSlots
{
    /// <summary>Step when searching along the line from an unusable slot toward the path, in metres.</summary>
    private const double SearchStepMeters = 0.25;

    /// <summary>
    /// Every following member's slot and the leader's own position, or null while the members have nothing to follow:
    /// before the squad's first movement order, while it has no leader, while the leader is in the drill under fire, and
    /// while the leader's path gives no direction (a new leader that has not moved yet, for example).
    /// </summary>
    public static SortedDictionary<string, Vec2>? Compute(Simulation simulation, Squad squad)
    {
        if (!squad.MovementStarted || squad.LeaderId is not { } leaderId || simulation.GetUnit(leaderId).Drill is not null)
        {
            return null;
        }

        var leader = simulation.GetUnit(leaderId);
        var doctrine = simulation.Config.SquadDoctrine;
        var corridor = squad.Corridor;
        var tables = new SortedDictionary<SquadFormation, SortedDictionary<string, (double Along, double Across)>>();
        foreach (var member in squad.Members)
        {
            var formation = squad.FormationOf(member.UnitId);
            if (!tables.ContainsKey(formation))
            {
                tables.Add(formation, Table(squad.Definition, formation, doctrine, corridor));
            }
        }

        var ahead = tables.Values.SelectMany(t => t.Values).Max(s => s.Along) + doctrine.IntervalMeters;
        var path = LeaderPath(squad, leader, ahead, out var leaderAt);
        if (path.Count < 2)
        {
            return null;
        }

        var slots = new SortedDictionary<string, Vec2>(StringComparer.Ordinal) { [leaderId] = leader.Position.Xy };
        foreach (var member in squad.Members)
        {
            if (member.UnitId == leaderId || !squad.IsFollowing(member.UnitId))
            {
                continue;
            }

            var (along, across) = tables[squad.FormationOf(member.UnitId)][member.UnitId];
            var unit = simulation.GetUnit(member.UnitId);
            var onPath = OnPathUsable(simulation, unit, corridor, path, leaderAt, along, out var direction);
            var slot = onPath + (Left(direction) * across);
            slots.Add(member.UnitId, Usable(simulation, unit, corridor, slot, onPath));
        }

        return slots;
    }

    /// <summary>
    /// The formation's slots after compression to the corridor's inner zone; a file if the lateral gap between neighbours
    /// would fall below the minimum.
    /// </summary>
    private static SortedDictionary<string, (double Along, double Across)> Table(
        SquadDefinition definition, SquadFormation formation, SquadDoctrine doctrine, Corridor? corridor)
    {
        var slots = FormationSlots.For(formation, definition, doctrine.IntervalMeters);
        var maxAcross = slots.Max(s => Math.Abs(s.Across));
        var scale = 1.0;
        if (corridor is not null && maxAcross > corridor.Width / 2)
        {
            scale = corridor.Width / 2 / maxAcross;
            if (SmallestLateralGap(slots) * scale < doctrine.MinimumGapMeters)
            {
                slots = FormationSlots.For(SquadFormation.File, definition, doctrine.IntervalMeters);
                scale = 1;
            }
        }

        var table = new SortedDictionary<string, (double, double)>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            table.Add(slot.UnitId, (slot.Along, slot.Across * scale));
        }

        return table;
    }

    /// <summary>The smallest positive difference between the distances of slots from the axis.</summary>
    private static double SmallestLateralGap(IReadOnlyList<FormationSlot> slots)
    {
        var distances = slots.Select(s => Math.Abs(s.Across)).Distinct().Order().ToList();
        var gap = double.PositiveInfinity;
        for (var i = 1; i < distances.Count; i++)
        {
            gap = Math.Min(gap, distances[i] - distances[i - 1]);
        }

        return gap;
    }

    /// <summary>
    /// The leader's path: the trail it walked, its position, then its planned route ahead as far as the front slot needs
    /// (<paramref name="aheadMeters"/>). <paramref name="leaderAt"/> is the leader's distance along it.
    /// </summary>
    private static List<Vec2> LeaderPath(Squad squad, Unit leader, double aheadMeters, out double leaderAt)
    {
        var here = leader.Position.Xy;
        var path = new List<Vec2>();
        if (squad.TrailLeaderId == leader.Id)
        {
            foreach (var point in squad.LeaderTrail)
            {
                Append(path, point);
            }
        }

        Append(path, here);
        leaderAt = Length(path);
        var reach = 0.0;
        foreach (var point in RouteAhead(leader))
        {
            if (reach > aheadMeters)
            {
                break; // the path is extended straight from here, and no slot lies this far ahead
            }

            reach += path[^1].DistanceTo(point);
            Append(path, point);
        }

        return path;
    }

    /// <summary>Where the leader is going: its planned corridor route, or the destination of its order.</summary>
    private static IEnumerable<Vec2> RouteAhead(Unit leader)
    {
        switch (leader.CurrentOrder)
        {
            case MoveThroughCorridor order when leader.CorridorProgress is { Route.Count: > 0 } progress &&
                ReferenceEquals(progress.Order, order) && progress.State == CorridorState.Moving:
                return progress.Route.Skip(progress.Next);
            case MoveThroughCorridor order:
                return [order.Destination];
            case MoveTo move:
                return [move.Destination];
            default:
                return [];
        }
    }

    /// <summary>Longest step back that is treated as a route's jog round a cell center rather than a turn, in metres.</summary>
    private const double JogMeters = 3;

    /// <summary>
    /// Adds a point to the path. A route through cell centers can jog back a little (past the last cell center to the
    /// exact destination, or from the start to its cell center); such a short step back would turn the path round, so
    /// the point it steps back from is dropped.
    /// </summary>
    private static void Append(List<Vec2> path, Vec2 point)
    {
        if (path.Count > 0 && path[^1].DistanceTo(point) <= 1e-9)
        {
            return;
        }

        while (path.Count >= 2 && path[^1].DistanceTo(path[^2]) <= JogMeters &&
            Dot(point - path[^1], path[^1] - path[^2]) < 0)
        {
            path.RemoveAt(path.Count - 1);
        }

        if (path.Count == 0 || path[^1].DistanceTo(point) > 1e-9)
        {
            path.Add(point);
        }
    }

    private static double Dot(Vec2 a, Vec2 b) => (a.X * b.X) + (a.Y * b.Y);

    private static double Length(List<Vec2> path)
    {
        var length = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            length += path[i - 1].DistanceTo(path[i]);
        }

        return length;
    }

    /// <summary>
    /// The point <paramref name="distance"/> metres along the path, extended straight beyond its ends, and the direction of
    /// the path there.
    /// </summary>
    private static Vec2 PointAt(List<Vec2> path, double distance, out Vec2 direction)
    {
        if (path.Count < 2)
        {
            direction = new Vec2(1, 0); // no direction known yet: the leader has neither walked nor planned
            return path[0] + (direction * distance);
        }

        if (distance <= 0)
        {
            direction = (path[1] - path[0]).Normalized();
            return path[0] + (direction * distance);
        }

        var travelled = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var length = path[i - 1].DistanceTo(path[i]);
            direction = (path[i] - path[i - 1]).Normalized();
            if (distance < travelled + length || i == path.Count - 1)
            {
                return path[i - 1] + (direction * (distance - travelled));
            }

            travelled += length;
        }

        throw new InvalidOperationException("unreachable");
    }

    private static Vec2 Left(Vec2 direction) => new(-direction.Y, direction.X);

    /// <summary>
    /// The slot's point on the leader's path; if the member cannot stand there (behind the start of a corridor, for
    /// example), the nearest point on the path toward the leader where it can.
    /// </summary>
    private static Vec2 OnPathUsable(
        Simulation simulation, Unit member, Corridor? corridor, List<Vec2> path, double leaderAt, double along, out Vec2 direction)
    {
        var steps = (int)Math.Ceiling(Math.Abs(along) / SearchStepMeters);
        for (var i = 0; i <= steps; i++)
        {
            var at = steps == 0 ? leaderAt : leaderAt + (along * (1 - ((double)i / steps)));
            var point = PointAt(path, at, out direction);
            if (CanStand(simulation, member, corridor, point))
            {
                return point;
            }
        }

        return PointAt(path, leaderAt, out direction);
    }

    /// <summary>
    /// The slot itself if the member can stand there; otherwise the nearest point that it can on the line toward
    /// <paramref name="onPath"/>, its point on the leader's path.
    /// </summary>
    private static Vec2 Usable(Simulation simulation, Unit member, Corridor? corridor, Vec2 slot, Vec2 onPath)
    {
        var distance = slot.DistanceTo(onPath);
        var steps = (int)Math.Ceiling(distance / SearchStepMeters);
        for (var i = 0; i <= steps; i++)
        {
            var point = steps == 0 ? slot : slot + ((onPath - slot) * Math.Min(1, (double)i / steps));
            if (CanStand(simulation, member, corridor, point))
            {
                return point;
            }
        }

        return onPath;
    }

    /// <summary>True if <paramref name="member"/> can stand at <paramref name="point"/>: on the map, passable, inside the corridor.</summary>
    internal static bool CanStand(Simulation simulation, Unit member, Corridor? corridor, Vec2 point)
    {
        if (!simulation.Terrain.Contains(point))
        {
            return false;
        }

        if (corridor is not null && Segments.DistanceToPolyline(point, corridor.Centerline) > corridor.OuterLimit)
        {
            return false;
        }

        return simulation.Terrain is not IWorldQuery world || member.Profile is not { } profile ||
            profile.BaseSpeed(world.ClassAt(point.X, point.Y)) > 0;
    }

    /// <summary>True if the member is a casualty the squad does not leave: pinned or out of action.</summary>
    public static bool IsCasualty(Unit unit) => unit.Health is Health.Pinned or Health.OutOfAction;
}
