using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Sim.Movement;

/// <summary>
/// A* on the world's raster (spec v0.1, "Liikemalli" and "Käytävä"). Nodes are cells, arcs go to the 8 neighbours.
/// An arc's cost is its length over its speed, times the corridor factor: speed is the mean of the two cells' base
/// speeds times the slope factor, and the corridor factor is the mean of the two cells' k(d). An arc is impassable if
/// either cell is water or a building, if it is steeper than the profile allows, if it leaves the corridor's outer
/// limit, or if it crosses a hard boundary that no permit covers. A diagonal arc also needs both cells beside it to be
/// passable (no corner cutting). An arc may not cross a cliff line unless the profile can rappel; a rappelled arc
/// ignores the slope limit and costs as much as the steepest walkable slope (docs/adr/0010). Ties are broken by cell index, so the same input always gives the same path.
/// The search covers the corridor's bounding rectangle, or the whole world without a corridor.
/// </summary>
public static class Pathfinder
{
    private static readonly (int Dc, int Dr)[] Neighbours =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    ];

    /// <summary>Finds the cheapest path from the cell containing <paramref name="from"/> to the cell containing <paramref name="to"/>.</summary>
    public static PathResult FindPath(IWorldQuery world, MovementProfile profile, Vec2 from, Vec2 to, PathOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        options ??= new PathOptions();
        if (!world.IsInside(from.X, from.Y) || !world.IsInside(to.X, to.Y))
        {
            return PathResult.None;
        }

        // Each call builds its own search grid. Two searches per plan are fast enough at the measured sizes;
        // share the grid if planning becomes a bottleneck.
        var grid = new SearchGrid(world, profile, options);
        return grid.Search(from, to);
    }

    /// <summary>
    /// The first hard-boundary crossing along a path (cell centers) that none of <paramref name="permits"/> covers, or
    /// null. Checks the corridor's drawn boundaries and the forest edge.
    /// </summary>
    public static BoundaryCrossing? FirstUnpermittedCrossing(
        IWorldQuery world, IReadOnlyList<Vec2> path, Corridor? corridor, IReadOnlyList<CrossingPermit> permits)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(permits);
        for (var i = 0; i + 1 < path.Count; i++)
        {
            if (CrossingOf(world, path[i], path[i + 1], corridor?.HardBoundaries ?? [], permits) is { } crossing)
            {
                return crossing;
            }
        }

        return null;
    }

    /// <summary>
    /// True if a route (any points, for example the agent's position, cell centers and the destination) crosses a cliff
    /// line that the profile cannot cross.
    /// </summary>
    public static bool CrossesCliff(IWorldQuery world, MovementProfile profile, IReadOnlyList<Vec2> route)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(route);
        if (profile.CanRappel)
        {
            return false;
        }

        for (var i = 0; i + 1 < route.Count; i++)
        {
            foreach (var cliff in world.Cliffs)
            {
                if (Segments.FirstIntersection(route[i], route[i + 1], cliff) is not null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The first hard boundary the move a–b crosses without a permit, or null.</summary>
    private static BoundaryCrossing? CrossingOf(
        IWorldQuery world, Vec2 a, Vec2 b, IReadOnlyList<HardBoundary> boundaries, IReadOnlyList<CrossingPermit> permits)
    {
        foreach (var boundary in boundaries)
        {
            if (Segments.FirstIntersection(a, b, boundary.Line) is { } hit && !Covered(permits, boundary.Id, hit))
            {
                return new BoundaryCrossing(boundary.Id, hit);
            }
        }

        if (LeavesForest(world.ClassAt(a.X, a.Y), world.ClassAt(b.X, b.Y)) && !Covered(permits, HardBoundary.ForestEdgeId, b))
        {
            return new BoundaryCrossing(HardBoundary.ForestEdgeId, b);
        }

        return null;
    }

    private static bool LeavesForest(TerrainClass from, TerrainClass to) =>
        from == TerrainClass.Forest && to is not (TerrainClass.Forest or TerrainClass.Water or TerrainClass.Building);

    private static bool Covered(IReadOnlyList<CrossingPermit> permits, string boundaryId, Vec2 point)
    {
        foreach (var permit in permits)
        {
            if (permit.Covers(boundaryId, point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The cells of one search: classes, heights and corridor factors of the search rectangle.</summary>
    private sealed class SearchGrid
    {
        private readonly IWorldQuery _world;
        private readonly MovementProfile _profile;
        private readonly PathOptions _options;
        private readonly double _cell;
        private readonly int _col0;
        private readonly int _row0;
        private readonly int _cols;
        private readonly int _rows;
        private readonly double[] _speed;
        private readonly double[] _height;
        private readonly double[] _factor;
        private readonly double[] _distance;
        private readonly TerrainClass[] _class;
        private readonly IReadOnlyList<CrossingPermit> _permits;
        private readonly List<(Vec2 A, Vec2 B)> _cliffSegments = [];
        private readonly List<int>?[] _cliffsNearCell;

        public SearchGrid(IWorldQuery world, MovementProfile profile, PathOptions options)
        {
            _world = world;
            _profile = profile;
            _options = options;
            _permits = options.Permits ?? [];
            _cell = world.CellSize;
            var bounds = world.Bounds;
            var worldCols = (int)Math.Round((bounds.MaxX - bounds.MinX) / _cell);
            var worldRows = (int)Math.Round((bounds.MaxY - bounds.MinY) / _cell);

            int c0 = 0, r0 = 0, c1 = worldCols - 1, r1 = worldRows - 1;
            if (options.Corridor is { } corridor)
            {
                var limit = corridor.OuterLimit;
                var minX = corridor.Centerline.Min(p => p.X) - limit;
                var maxX = corridor.Centerline.Max(p => p.X) + limit;
                var minY = corridor.Centerline.Min(p => p.Y) - limit;
                var maxY = corridor.Centerline.Max(p => p.Y) + limit;
                c0 = Math.Max(c0, (int)Math.Floor((minX - bounds.MinX) / _cell));
                r0 = Math.Max(r0, (int)Math.Floor((minY - bounds.MinY) / _cell));
                c1 = Math.Min(c1, (int)Math.Floor((maxX - bounds.MinX) / _cell));
                r1 = Math.Min(r1, (int)Math.Floor((maxY - bounds.MinY) / _cell));
            }

            _col0 = c0;
            _row0 = r0;
            _cols = Math.Max(0, c1 - c0 + 1);
            _rows = Math.Max(0, r1 - r0 + 1);
            var n = _cols * _rows;
            _speed = new double[n];
            _height = new double[n];
            _factor = new double[n];
            _distance = new double[n];
            _class = new TerrainClass[n];
            for (var r = 0; r < _rows; r++)
            {
                for (var c = 0; c < _cols; c++)
                {
                    var i = (r * _cols) + c;
                    var center = Center(i);
                    _class[i] = world.ClassAt(center.X, center.Y);
                    _speed[i] = profile.BaseSpeed(_class[i]);
                    _height[i] = world.GroundHeight(center.X, center.Y);
                    if (options.Corridor is { } corr)
                    {
                        _distance[i] = Segments.DistanceToPolyline(center, corr.Centerline);
                        _factor[i] = corr.CostFactor(_distance[i]);
                    }
                    else
                    {
                        _factor[i] = 1;
                    }
                }
            }

            _cliffsNearCell = new List<int>?[n];
            IndexCliffs(world);
        }

        /// <summary>
        /// Lists, for every cell, the cliff segments whose bounding box touches it. An arc between two cell centers lies
        /// inside those two cells, so only their segments can cross it.
        /// </summary>
        private void IndexCliffs(IWorldQuery world)
        {
            var bounds = world.Bounds;
            foreach (var cliff in world.Cliffs)
            {
                for (var k = 0; k + 1 < cliff.Count; k++)
                {
                    var (a, b) = (cliff[k], cliff[k + 1]);
                    var segment = _cliffSegments.Count;
                    _cliffSegments.Add((a, b));
                    var cMin = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - bounds.MinX) / _cell) - _col0 - 1);
                    var cMax = Math.Min(_cols - 1, (int)Math.Floor((Math.Max(a.X, b.X) - bounds.MinX) / _cell) - _col0 + 1);
                    var rMin = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, b.Y) - bounds.MinY) / _cell) - _row0 - 1);
                    var rMax = Math.Min(_rows - 1, (int)Math.Floor((Math.Max(a.Y, b.Y) - bounds.MinY) / _cell) - _row0 + 1);
                    for (var r = rMin; r <= rMax; r++)
                    {
                        for (var c = cMin; c <= cMax; c++)
                        {
                            (_cliffsNearCell[(r * _cols) + c] ??= []).Add(segment);
                        }
                    }
                }
            }
        }

        private bool CrossesCliff(int a, int b, Vec2 pa, Vec2 pb)
        {
            foreach (var list in new[] { _cliffsNearCell[a], _cliffsNearCell[b] })
            {
                if (list is null)
                {
                    continue;
                }

                foreach (var segment in list)
                {
                    var (q1, q2) = _cliffSegments[segment];
                    if (Segments.Intersection(pa, pb, q1, q2) is not null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public PathResult Search(Vec2 from, Vec2 to)
        {
            var start = IndexOf(from);
            var goal = IndexOf(to);
            if (start < 0 || goal < 0 || !Open(start) || !Open(goal))
            {
                return PathResult.None;
            }

            var n = _cols * _rows;
            var g = new double[n];
            Array.Fill(g, double.PositiveInfinity);
            var parent = new int[n];
            Array.Fill(parent, -1);
            var closed = new bool[n];
            var queue = new PriorityQueue<int, (double F, int Index)>();
            var goalCenter = Center(goal);
            var secondsPerMeter = 1 / (_profile.MaxBaseSpeed * MovementProfile.MaxSlopeFactor);

            g[start] = 0;
            queue.Enqueue(start, (Heuristic(start, goalCenter, secondsPerMeter), start));
            while (queue.TryDequeue(out var current, out _))
            {
                if (closed[current])
                {
                    continue;
                }

                if (current == goal)
                {
                    return new PathResult(true, Reconstruct(parent, goal), g[goal]);
                }

                closed[current] = true;
                var cc = current % _cols;
                var cr = current / _cols;
                foreach (var (dc, dr) in Neighbours)
                {
                    var nc = cc + dc;
                    var nr = cr + dr;
                    if (nc < 0 || nr < 0 || nc >= _cols || nr >= _rows)
                    {
                        continue;
                    }

                    var next = (nr * _cols) + nc;
                    if (closed[next] || !Open(next))
                    {
                        continue;
                    }

                    if (dc != 0 && dr != 0 && (!Open((cr * _cols) + nc) || !Open((nr * _cols) + cc)))
                    {
                        continue; // no corner cutting
                    }

                    var cost = ArcCost(current, next, dc != 0 && dr != 0);
                    if (double.IsPositiveInfinity(cost))
                    {
                        continue;
                    }

                    var candidate = g[current] + cost;
                    if (candidate < g[next])
                    {
                        g[next] = candidate;
                        parent[next] = current;
                        queue.Enqueue(next, (candidate + Heuristic(next, goalCenter, secondsPerMeter), next));
                    }
                }
            }

            return PathResult.None;
        }

        private double ArcCost(int a, int b, bool diagonal)
        {
            var length = diagonal ? _cell * Math.Sqrt(2) : _cell;
            var slope = (_height[b] - _height[a]) / length;
            var pa = Center(a);
            var pb = Center(b);
            var overCliff = CrossesCliff(a, b, pa, pb);
            if (overCliff ? !_profile.CanRappel : Math.Abs(slope) > _profile.MaxSlope)
            {
                return double.PositiveInfinity;
            }

            if (_options.Corridor is { } corridor)
            {
                if (!StaysInCorridor(a, b, pa, pb, corridor))
                {
                    return double.PositiveInfinity;
                }

                if (!_options.IgnoreHardBoundaries)
                {
                    foreach (var boundary in corridor.HardBoundaries)
                    {
                        if (Segments.FirstIntersection(pa, pb, boundary.Line) is { } hit && !Covered(_permits, boundary.Id, hit))
                        {
                            return double.PositiveInfinity;
                        }
                    }
                }
            }

            if (!_options.IgnoreHardBoundaries && LeavesForest(_class[a], _class[b]) &&
                !Covered(_permits, HardBoundary.ForestEdgeId, pb))
            {
                return double.PositiveInfinity;
            }

            var speed = (_speed[a] + _speed[b]) / 2 * _profile.SlopeSpeedFactor(slope);
            var factor = (_factor[a] + _factor[b]) / 2;
            return length / speed * factor;
        }

        /// <summary>
        /// Both cell centers are within the outer limit (their factor is finite). Near the limit the arc itself is
        /// sampled too, because the limit is not convex where the centerline bends.
        /// </summary>
        private bool StaysInCorridor(int a, int b, Vec2 pa, Vec2 pb, Corridor corridor)
        {
            var limit = corridor.OuterLimit;
            if (_distance[a] <= limit - (2 * _cell) && _distance[b] <= limit - (2 * _cell))
            {
                return true;
            }

            const int Samples = 16;
            for (var s = 1; s < Samples; s++)
            {
                var p = pa + ((pb - pa) * ((double)s / Samples));
                if (Segments.DistanceToPolyline(p, corridor.Centerline) > limit)
                {
                    return false;
                }
            }

            return true;
        }

        private bool Open(int i) => _speed[i] > 0 && !double.IsPositiveInfinity(_factor[i]);

        private double Heuristic(int i, Vec2 goal, double secondsPerMeter)
        {
            var p = Center(i);
            var dx = Math.Abs(goal.X - p.X);
            var dy = Math.Abs(goal.Y - p.Y);
            var octile = Math.Max(dx, dy) + ((Math.Sqrt(2) - 1) * Math.Min(dx, dy));
            return octile * secondsPerMeter;
        }

        private List<Vec2> Reconstruct(int[] parent, int goal)
        {
            var path = new List<Vec2>();
            for (var i = goal; i >= 0; i = parent[i])
            {
                path.Add(Center(i));
            }

            path.Reverse();
            return path;
        }

        private int IndexOf(Vec2 p)
        {
            var bounds = _world.Bounds;
            var worldCols = (int)Math.Round((bounds.MaxX - bounds.MinX) / _cell);
            var worldRows = (int)Math.Round((bounds.MaxY - bounds.MinY) / _cell);
            var c = Math.Clamp((int)Math.Floor((p.X - bounds.MinX) / _cell), 0, worldCols - 1) - _col0;
            var r = Math.Clamp((int)Math.Floor((p.Y - bounds.MinY) / _cell), 0, worldRows - 1) - _row0;
            return c < 0 || r < 0 || c >= _cols || r >= _rows ? -1 : (r * _cols) + c;
        }

        private Vec2 Center(int i)
        {
            var bounds = _world.Bounds;
            var c = _col0 + (i % _cols);
            var r = _row0 + (i / _cols);
            return new Vec2(bounds.MinX + ((c + 0.5) * _cell), bounds.MinY + ((r + 0.5) * _cell));
        }
    }
}
