using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// The geometry of deploying at the forest edge (backlog 0004, S2c): where the RJ stops on its route, which way the edge
/// runs, and each member's spot in the open line (the members that do not fit the corridor in further lines behind it).
/// </summary>
internal static class EdgeDeployment
{
    /// <summary>Step when walking a route back from the edge, in metres.</summary>
    private const double SampleMeters = 0.25;

    /// <summary>Edge cells within this distance of the crossing give the edge's direction, in metres.</summary>
    private const double EdgeFitMeters = 15;

    /// <summary>A spot counts as on its depth if it is within this of it, in metres.</summary>
    private const double DepthToleranceMeters = 1;

    /// <summary>Lines tried behind the first before a member just stays behind the leader.</summary>
    private const int MaxLines = 5;

    /// <summary>
    /// The RJ's route to its deployment point: along <paramref name="route"/> to the last point before the crossing it must
    /// ask for (<paramref name="crossingAt"/>, the route point just outside the forest) that lies
    /// <paramref name="insetMeters"/> inside the edge, or where it stands if it is already closer. Edges the route leaves
    /// the forest at earlier (into a clearing, with a permit already) do not count.
    /// </summary>
    public static List<Vec2> RouteToEdge(IWorldQuery world, IReadOnlyList<Vec2> route, Vec2 crossingAt, double insetMeters)
    {
        var end = 1;
        while (end < route.Count - 1 && route[end].DistanceTo(crossingAt) > 1e-9)
        {
            end++;
        }

        var samples = new List<(Vec2 Point, int Segment)> { (route[0], 0) };
        for (var i = 1; i < end; i++)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(route[i - 1].DistanceTo(route[i]) / SampleMeters));
            for (var s = 1; s <= steps; s++)
            {
                samples.Add((route[i - 1] + ((route[i] - route[i - 1]) * ((double)s / steps)), i));
            }
        }

        for (var i = samples.Count - 1; i >= 0; i--)
        {
            if (DistanceInside(world, samples[i].Point, insetMeters + 1) >= insetMeters)
            {
                var path = route.Take(samples[i].Segment).ToList();
                if (path.Count == 0 || path[^1].DistanceTo(samples[i].Point) > 1e-9)
                {
                    path.Add(samples[i].Point);
                }

                return path;
            }
        }

        return [route[0]];
    }

    /// <summary>
    /// The open line for <paramref name="squad"/> with its leader at <paramref name="leaderAt"/>, the route leaving the
    /// forest at <paramref name="crossingAt"/>.
    /// </summary>
    public static EdgeLine Line(Simulation simulation, Squad squad, Vec2 leaderAt, Vec2 crossingAt)
    {
        var world = (IWorldQuery)simulation.Terrain;
        var doctrine = simulation.Config.SquadDoctrine;
        var corridor = squad.Corridor;
        var outward = Outward(world, leaderAt, crossingAt);
        var left = new Vec2(-outward.Y, outward.X);
        var leaderId = squad.LeaderId!;
        var members = FormationSlots.LineOrder(squad.Definition)
            .Where(id => id == leaderId || (squad.IsFollowing(id) && simulation.GetUnit(id).Health != Health.OutOfAction))
            .ToList();
        var leaderIndex = members.IndexOf(leaderId);

        var spots = new SortedDictionary<string, Vec2>(StringComparer.Ordinal) { [leaderId] = leaderAt };
        var firstLine = new List<int> { leaderIndex };
        var leftOver = new List<string>();
        for (var i = 0; i < members.Count; i++)
        {
            if (i == leaderIndex)
            {
                continue;
            }

            var nominal = leaderAt + (left * ((leaderIndex - i) * doctrine.IntervalMeters));
            if (Spot(simulation, world, members[i], corridor, nominal, outward, doctrine.EdgeInsetMeters) is { } spot)
            {
                spots.Add(members[i], spot);
                firstLine.Add(i);
            }
            else
            {
                leftOver.Add(members[i]);
            }
        }

        for (var line = 1; leftOver.Count > 0; line++)
        {
            var depth = line * doctrine.SecondLineDepthMeters;
            var centre = leaderAt - (outward * depth);
            var still = new List<string>();
            for (var j = 0; j < leftOver.Count; j++)
            {
                var nominal = centre + (left * ((((leftOver.Count - 1) / 2.0) - j) * doctrine.IntervalMeters));
                var spot = line >= MaxLines
                    ? centre
                    : Spot(simulation, world, leftOver[j], corridor, nominal, outward, doctrine.EdgeInsetMeters + depth);
                if (spot is { } usable)
                {
                    spots.Add(leftOver[j], usable);
                }
                else
                {
                    still.Add(leftOver[j]);
                }
            }

            leftOver = still;
        }

        var halfWidth = firstLine.Max(i => spots[members[i]].DistanceTo(leaderAt));
        return new EdgeLine(new DeployAtForestEdge(), leaderAt, outward, spots, halfWidth,
            members[firstLine.Min()], members[firstLine.Max()]);
    }

    /// <summary>
    /// A member's spot: <paramref name="nominal"/> moved along <paramref name="outward"/> to the forest cell
    /// <paramref name="depthMeters"/> inside the edge; null if there is none it can stand on inside the corridor.
    /// </summary>
    private static Vec2? Spot(
        Simulation simulation, IWorldQuery world, string unitId, Corridor? corridor, Vec2 nominal, Vec2 outward, double depthMeters)
    {
        var point = nominal;
        var search = depthMeters + 10;
        for (var i = 0; i < 8; i++)
        {
            var inside = DistanceInside(world, point, search);
            var move = IsForest(world, point) ? inside - depthMeters : -(depthMeters + world.CellSize);
            if (Math.Abs(move) < SampleMeters)
            {
                break;
            }

            point += outward * move;
        }

        var member = simulation.GetUnit(unitId);
        return IsForest(world, point) && Math.Abs(DistanceInside(world, point, search) - depthMeters) <= DepthToleranceMeters &&
            SquadSlots.CanStand(simulation, member, corridor, point)
            ? point
            : null;
    }

    /// <summary>
    /// Out of the forest at the crossing: perpendicular to the edge fitted through the edge's cell sides near
    /// <paramref name="crossingAt"/>, pointing from the leader toward the crossing.
    /// </summary>
    private static Vec2 Outward(IWorldQuery world, Vec2 leaderAt, Vec2 crossingAt)
    {
        var toCrossing = (crossingAt - leaderAt).Length > 1e-9 ? (crossingAt - leaderAt).Normalized() : new Vec2(1, 0);
        var points = new List<Vec2>();
        var cell = world.CellSize;
        var reach = (int)Math.Ceiling(EdgeFitMeters / cell);
        var (column, row) = CellOf(world, crossingAt);
        for (var r = row - reach; r <= row + reach; r++)
        {
            for (var c = column - reach; c <= column + reach; c++)
            {
                var center = Center(world, c, r);
                if (center.DistanceTo(crossingAt) > EdgeFitMeters || !world.IsInside(center.X, center.Y) || !IsForest(world, center))
                {
                    continue;
                }

                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var next = Center(world, c + dc, r + dr);
                    if (world.IsInside(next.X, next.Y) && Leaves(world.ClassAt(next.X, next.Y)))
                    {
                        points.Add((center + next) * 0.5);
                    }
                }
            }
        }

        if (points.Count < 2)
        {
            return toCrossing;
        }

        var mean = new Vec2(points.Average(p => p.X), points.Average(p => p.Y));
        double a = 0, b = 0, d = 0;
        foreach (var p in points)
        {
            var q = p - mean;
            a += q.X * q.X;
            b += q.X * q.Y;
            d += q.Y * q.Y;
        }

        var angle = 0.5 * Math.Atan2(2 * b, a - d);
        var normal = new Vec2(-Math.Sin(angle), Math.Cos(angle));
        return normal.Dot(toCrossing) >= 0 ? normal : normal * -1;
    }

    /// <summary>
    /// How far <paramref name="point"/> is inside the forest: the distance to the nearest cell whose terrain counts as
    /// leaving it (ADR 0009), at most <paramref name="maxMeters"/>; 0 outside the forest.
    /// </summary>
    public static double DistanceInside(IWorldQuery world, Vec2 point, double maxMeters)
    {
        if (!IsForest(world, point))
        {
            return 0;
        }

        var cell = world.CellSize;
        var reach = (int)Math.Ceiling(maxMeters / cell) + 1;
        var (column, row) = CellOf(world, point);
        var best = maxMeters;
        for (var r = row - reach; r <= row + reach; r++)
        {
            for (var c = column - reach; c <= column + reach; c++)
            {
                var center = Center(world, c, r);
                if (!world.IsInside(center.X, center.Y) || !Leaves(world.ClassAt(center.X, center.Y)))
                {
                    continue;
                }

                var dx = Math.Max(Math.Max(center.X - (cell / 2) - point.X, 0), point.X - (center.X + (cell / 2)));
                var dy = Math.Max(Math.Max(center.Y - (cell / 2) - point.Y, 0), point.Y - (center.Y + (cell / 2)));
                best = Math.Min(best, Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        return best;
    }

    private static bool IsForest(IWorldQuery world, Vec2 point) =>
        world.IsInside(point.X, point.Y) && world.ClassAt(point.X, point.Y) == TerrainClass.Forest;

    /// <summary>True if stepping from the forest onto this terrain leaves the forest (as the pathfinder decides).</summary>
    private static bool Leaves(TerrainClass terrain) =>
        terrain is not (TerrainClass.Forest or TerrainClass.Water or TerrainClass.Building);

    private static (int Column, int Row) CellOf(IWorldQuery world, Vec2 point) =>
        ((int)Math.Floor((point.X - world.Bounds.MinX) / world.CellSize), (int)Math.Floor((point.Y - world.Bounds.MinY) / world.CellSize));

    private static Vec2 Center(IWorldQuery world, int column, int row) =>
        new(world.Bounds.MinX + ((column + 0.5) * world.CellSize), world.Bounds.MinY + ((row + 0.5) * world.CellSize));
}
