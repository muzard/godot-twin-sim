using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Positions for the drill under fire (backlog 0003), judged against the agent's known attackers:
/// <list type="bullet">
/// <item>Cover: no attacker can see the agent there lying prone.</item>
/// <item>Firing position: cover, and from there the agent sees an attacker when crouching, in range, with a clear fire
/// sector.</item>
/// <item>Clear-sector position (fallback): the agent sees an attacker lying prone, in range, with a clear sector.</item>
/// </list>
/// The search walks a grid of the doctrine's spacing from the agent, within the search distance, through passable
/// ground it can walk (slope, cliffs) without crossing the hard boundaries of its corridor order or leaving forest
/// (ADR 0009), and takes the nearest
/// point in a straight line, ties by grid order. Firing positions are looked for forward first: points no farther from
/// the nearest attacker than the agent is now. Each search returns the route to the point, starting where the agent is.
/// Deterministic: fixed grid and neighbour order, no hashing.
/// </summary>
internal static class CoverSearch
{
    private static readonly (int Di, int Dj)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>The route to the nearest cover, or null if there is none within reach.</summary>
    public static List<Vec2>? Cover(Simulation simulation, Unit agent, IReadOnlyList<Unit> attackers) =>
        Search(simulation, agent, attackers, forward: false, (ctx, p) => ctx.Hidden(p));

    /// <summary>The route to the nearest firing position, forward first, or null if there is none within reach.</summary>
    public static List<Vec2>? FiringPosition(Simulation simulation, Unit agent, IReadOnlyList<Unit> attackers) =>
        Search(simulation, agent, attackers, forward: true,
            (ctx, p) => ctx.Hidden(p) && ctx.CanFireFrom(p, ctx.Rules.Crouching.EyeHeight));

    /// <summary>The route to the nearest point with a clear fire sector, cover or not, forward first; or null.</summary>
    public static List<Vec2>? ClearSectorPosition(Simulation simulation, Unit agent, IReadOnlyList<Unit> attackers) =>
        Search(simulation, agent, attackers, forward: true, (ctx, p) => ctx.CanFireFrom(p, ctx.Rules.Prone.EyeHeight));

    private static List<Vec2>? Search(
        Simulation simulation, Unit agent, IReadOnlyList<Unit> attackers, bool forward, Func<Context, Vec2, bool> accept)
    {
        if (attackers.Count == 0 || simulation.Terrain is not IWorldQuery world || agent.Profile is null ||
            agent.Combatant is null)
        {
            return null;
        }

        var drill = simulation.Config.Combat.Doctrine.Drill;
        var grid = new Grid(world, agent, drill.SearchGridMeters, drill.CoverSearchMeters, BoundariesOf(agent));
        grid.Explore();
        var context = new Context(simulation, world, agent, attackers);
        var nearestAttacker = attackers.OrderBy(a => a.Position.Xy.DistanceTo(grid.Start)).ThenBy(a => a.Id, StringComparer.Ordinal).First();
        var here = grid.Start.DistanceTo(nearestAttacker.Position.Xy);

        var candidates = grid.Reached().ToList();
        IEnumerable<int> order = forward
            ? candidates.Where(n => grid.Point(n).DistanceTo(nearestAttacker.Position.Xy) <= here + 1e-9)
                .Concat(candidates.Where(n => grid.Point(n).DistanceTo(nearestAttacker.Position.Xy) > here + 1e-9))
            : candidates;
        foreach (var node in order)
        {
            if (accept(context, grid.Point(node)))
            {
                return grid.RouteTo(node);
            }
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyList<Vec2>> BoundariesOf(Unit agent) =>
        (agent.CurrentOrder ?? agent.Drill?.SuspendedOrder) is MoveThroughCorridor corridor
            ? corridor.Corridor.HardBoundaries.Select(b => b.Line).ToList()
            : [];

    /// <summary>What a candidate point is judged by.</summary>
    private sealed class Context(Simulation simulation, IWorldQuery world, Unit agent, IReadOnlyList<Unit> attackers)
    {
        public CombatRules Rules { get; } = simulation.Config.Combat;

        /// <summary>No attacker sees the agent lying prone at the point.</summary>
        public bool Hidden(Vec2 p)
        {
            var aim = p.WithZ(world.GroundHeight(p.X, p.Y) + Rules.Prone.AimHeight);
            return attackers.All(a => !LineOfSight.Between(world, a.EyePosition, aim, Rules).IsVisible(Rules));
        }

        /// <summary>From the point, with its eyes at <paramref name="eyeHeight"/>, the agent may fire at an attacker.</summary>
        public bool CanFireFrom(Vec2 p, double eyeHeight)
        {
            var eye = p.WithZ(world.GroundHeight(p.X, p.Y) + eyeHeight);
            return attackers.Any(a =>
                eye.DistanceTo(a.AimPoint) <= agent.Combatant!.Weapon.MaxRangeMeters &&
                LineOfSight.Between(world, eye, a.AimPoint, Rules).IsVisible(Rules) &&
                CombatQueries.SectorClear(simulation, agent, p, a.Position.Xy));
        }
    }

    /// <summary>
    /// The square grid around the agent and a breadth-first walk over it. Leaving forest needs one of
    /// <c>permits</c> (the drill has none; a squad member in contact carries its squad's, backlog 0005).
    /// </summary>
    internal sealed class Grid
    {
        private readonly IWorldQuery _world;
        private readonly Unit _agent;
        private readonly double _spacing;
        private readonly double _radius;
        private readonly IReadOnlyList<IReadOnlyList<Vec2>> _boundaries;
        private readonly int _half;
        private readonly int _width;
        private readonly int[] _parent;
        private readonly IReadOnlyList<Corridors.CrossingPermit> _permits;

        public Grid(
            IWorldQuery world, Unit agent, double spacing, double radius, IReadOnlyList<IReadOnlyList<Vec2>> boundaries,
            IReadOnlyList<Corridors.CrossingPermit>? permits = null)
        {
            _permits = permits ?? [];
            _world = world;
            _agent = agent;
            _spacing = spacing;
            _radius = radius;
            _boundaries = boundaries;
            _half = (int)Math.Floor(radius / spacing);
            _width = (2 * _half) + 1;
            _parent = Enumerable.Repeat(Unvisited, _width * _width).ToArray();
            Start = agent.Position.Xy;
        }

        private const int Unvisited = -2;
        private const int Root = -1;

        public Vec2 Start { get; }

        public Vec2 Point(int node) => Start + new Vec2(I(node) * _spacing, J(node) * _spacing);

        /// <summary>Visits every point reachable from the start within the radius.</summary>
        public void Explore()
        {
            var start = Node(0, 0);
            _parent[start] = Root;
            var queue = new List<int> { start };
            for (var head = 0; head < queue.Count; head++)
            {
                var from = queue[head];
                foreach (var (di, dj) in Steps)
                {
                    var (i, j) = (I(from) + di, J(from) + dj);
                    if (Math.Abs(i) > _half || Math.Abs(j) > _half || Math.Sqrt((i * i) + (j * j)) * _spacing > _radius + 1e-9)
                    {
                        continue;
                    }

                    var to = Node(i, j);
                    if (_parent[to] != Unvisited || !CanStep(from, to, di, dj))
                    {
                        continue;
                    }

                    _parent[to] = from;
                    queue.Add(to);
                }
            }
        }

        /// <summary>Reached points, nearest to the start first (straight distance), ties by grid order.</summary>
        public IEnumerable<int> Reached() =>
            Enumerable.Range(0, _parent.Length)
                .Where(n => _parent[n] != Unvisited)
                .OrderBy(n => Math.Sqrt((I(n) * I(n)) + (J(n) * J(n))))
                .ThenBy(n => n);

        /// <summary>The route from the agent's position through the grid to the node.</summary>
        public List<Vec2> RouteTo(int node)
        {
            var points = new List<Vec2>();
            for (var n = node; n != Root; n = _parent[n])
            {
                points.Add(Point(n));
            }

            points.Reverse();
            return points;
        }

        private int I(int node) => (node % _width) - _half;

        private int J(int node) => (node / _width) - _half;

        private int Node(int i, int j) => i + _half + ((j + _half) * _width);

        private bool Passable(Vec2 p) =>
            _world.IsInside(p.X, p.Y) && _agent.Profile!.BaseSpeed(_world.ClassAt(p.X, p.Y)) > 0;

        private bool CanStep(int from, int to, int di, int dj)
        {
            var a = Point(from);
            var b = Point(to);
            if (!Passable(b))
            {
                return false;
            }

            // A diagonal step may not cut the corner of an impassable cell.
            if (di != 0 && dj != 0 && (!Passable(a + new Vec2(di * _spacing, 0)) || !Passable(a + new Vec2(0, dj * _spacing))))
            {
                return false;
            }

            // ADR 0009: leaving forest crosses a hard boundary, and the drill has no permit for it.
            if (_world.ClassAt(a.X, a.Y) == TerrainClass.Forest && _world.ClassAt(b.X, b.Y) != TerrainClass.Forest &&
                !_permits.Any(p => p.Covers(Corridors.HardBoundary.ForestEdgeId, b)))
            {
                return false;
            }

            var profile = _agent.Profile!;
            var slope = Math.Abs(_world.GroundHeight(b.X, b.Y) - _world.GroundHeight(a.X, a.Y)) / a.DistanceTo(b);
            if (slope > profile.MaxSlope)
            {
                return false;
            }

            if (!profile.CanRappel && _world.Cliffs.Any(cliff => Segments.FirstIntersection(a, b, cliff) is not null))
            {
                return false;
            }

            return _boundaries.All(line => Segments.FirstIntersection(a, b, line) is null);
        }
    }
}
