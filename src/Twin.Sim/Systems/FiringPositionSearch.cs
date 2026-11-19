using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;

namespace Twin.Sim.Systems;

/// <summary>
/// Firing positions in contact (backlog 0005), scored rather than nearest first (0003's drill keeps its own search):
/// <list type="bullet">
/// <item>A candidate is a point the agent can fire from at one of its targets: crouching it sees the target's known
/// position, in range, with its fire sector clear of its own side.</item>
/// <item>Score: cover (no target sees it lying prone there) and concealment (forest there) count for it, and so does
/// height above the target's ground; the way there counts against it, more so over open ground. Highest wins, ties by
/// 0003's grid order (nearest first).</item>
/// </list>
/// A fighter's own search walks 0003's grid from where it stands, through ground it can walk, never across a hard
/// boundary of its squad's corridor and out of forest only on its squad's permit (ADR 0009). A team's search tries every
/// grid point near a centre as the middle of a line of the team's spots across the direction to the enemy, at the squad
/// interval, and takes the one from which the most members can fire. Deterministic: fixed grid order, no hashing.
/// </summary>
internal static class FiringPositionSearch
{
    /// <summary>An enemy to fire at, and where the searcher knows it to be.</summary>
    internal readonly record struct Target(Unit Enemy, Vec2 Known);

    /// <summary>The route to the agent's best firing position within <paramref name="radius"/>, or null if there is none.</summary>
    public static List<Vec2>? Best(Simulation simulation, Unit agent, IReadOnlyList<Target> targets, double radius)
    {
        if (targets.Count == 0 || simulation.Terrain is not IWorldQuery world || agent.Profile is null || agent.Combatant is null)
        {
            return null;
        }

        var squad = simulation.SquadOf(agent.Id);
        var grid = new CoverSearch.Grid(world, agent, simulation.Config.Combat.Doctrine.Drill.SearchGridMeters, radius,
            BoundariesOf(agent, squad), squad?.Permits ?? []);
        grid.Explore();
        List<Vec2>? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var node in grid.Reached())
        {
            var point = grid.Point(node);
            if (!SquadSlots.CanStand(simulation, agent, squad?.Corridor, point) ||
                FirstTargetFrom(simulation, world, agent, point, targets, exclude: null, extraFriends: []) is not { } target)
            {
                continue;
            }

            var route = grid.RouteTo(node);
            var score = Score(simulation, world, route, target, targets);
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                best = route;
            }
        }

        return best;
    }

    /// <summary>
    /// The best spots for <paramref name="members"/> (in squad order) in a line within <paramref name="radius"/> of
    /// <paramref name="centre"/>: the most of them able to fire, then the highest summed score, ties by grid order (rows
    /// from the south-west). Null unless from somewhere more than <paramref name="moreThan"/> of them can fire.
    /// </summary>
    public static IReadOnlyDictionary<string, Vec2>? TeamSpots(
        Simulation simulation, Squad squad, IReadOnlyList<Unit> members, IReadOnlyList<Target> targets, Vec2 centre, double radius,
        int moreThan = 0)
    {
        if (members.Count == 0 || targets.Count == 0 || simulation.Terrain is not IWorldQuery world)
        {
            return null;
        }

        var target = targets.OrderBy(t => t.Known.DistanceTo(centre)).ThenBy(t => t.Enemy.Id, StringComparer.Ordinal).First();
        var spacing = simulation.Config.Combat.Doctrine.Drill.SearchGridMeters;
        var interval = simulation.Config.SquadDoctrine.IntervalMeters;
        var half = (int)Math.Floor(radius / spacing);
        var team = new SortedSet<string>(members.Select(m => m.Id), StringComparer.Ordinal);
        SortedDictionary<string, Vec2>? best = null;
        var bestCount = moreThan;
        var bestScore = double.NegativeInfinity;
        for (var j = -half; j <= half; j++)
        {
            for (var i = -half; i <= half; i++)
            {
                if (Math.Sqrt((i * i) + (j * j)) * spacing > radius + 1e-9)
                {
                    continue;
                }

                var middle = centre + new Vec2(i * spacing, j * spacing);
                var toEnemy = (target.Known - middle).Normalized();
                if (toEnemy == Vec2.Zero)
                {
                    continue;
                }

                var across = new Vec2(-toEnemy.Y, toEnemy.X);
                var spots = new SortedDictionary<string, Vec2>(StringComparer.Ordinal);
                for (var k = 0; k < members.Count; k++)
                {
                    spots[members[k].Id] = middle + (across * ((k - ((members.Count - 1) / 2.0)) * interval));
                }

                if (!members.All(m => Reachable(simulation, world, squad, m, spots[m.Id])))
                {
                    continue;
                }

                var count = 0;
                foreach (var member in members)
                {
                    var spot = spots[member.Id];
                    var others = spots.Where(s => s.Key != member.Id).Select(s => s.Value).ToList();
                    if (FirstTargetFrom(simulation, world, member, spot, [target], team, others) is not null)
                    {
                        count++;
                    }
                }

                if (count < bestCount || count == 0)
                {
                    continue; // it cannot win: skip the score
                }

                var score = members.Sum(m => Score(simulation, world, [m.Position.Xy, spots[m.Id]], target, targets));
                if (count > bestCount || score > bestScore + 1e-9)
                {
                    bestCount = count;
                    bestScore = score;
                    best = spots;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The first target the agent may fire at from <paramref name="point"/> when crouching: in range, in sight of its known
    /// position, and no one of its side (but those in <paramref name="exclude"/>, and with those standing at
    /// <paramref name="extraFriends"/>) within its fire sector.
    /// </summary>
    private static Target? FirstTargetFrom(
        Simulation simulation, IWorldQuery world, Unit agent, Vec2 point, IReadOnlyList<Target> targets,
        ISet<string>? exclude, IReadOnlyList<Vec2> extraFriends)
    {
        var rules = simulation.Config.Combat;
        var eye = point.WithZ(world.GroundHeight(point.X, point.Y) + rules.Crouching.EyeHeight);
        foreach (var target in targets)
        {
            var aim = AimAt(world, target);
            if (eye.DistanceTo(aim) <= agent.Combatant!.Weapon.MaxRangeMeters &&
                SectorClear(simulation, agent, point, target.Known, exclude, extraFriends) &&
                LineOfSight.Between(world, eye, aim, rules).IsVisible(rules))
            {
                return target;
            }
        }

        return null;
    }

    private static bool SectorClear(
        Simulation simulation, Unit agent, Vec2 from, Vec2 to, ISet<string>? exclude, IReadOnlyList<Vec2> extraFriends)
    {
        if (exclude is null && extraFriends.Count == 0)
        {
            return CombatQueries.SectorClear(simulation, agent, from, to);
        }

        var aim = to - from;
        if (aim.Length == 0)
        {
            return true;
        }

        var direction = aim.Normalized();
        var limit = Math.Cos(simulation.Config.Combat.Doctrine.Drill.FireSectorHalfAngleDegrees * Math.PI / 180);
        var range = agent.Combatant!.Weapon.MaxRangeMeters;
        var friends = simulation.Units
            .Where(u => u != agent && u.Combatant?.Side == agent.Combatant.Side && !(exclude?.Contains(u.Id) ?? false))
            .Select(u => u.Position.Xy)
            .Concat(extraFriends);
        foreach (var friend in friends)
        {
            var toFriend = friend - from;
            var length = toFriend.Length;
            if (length > 0 && length <= range && direction.Dot(toFriend * (1 / length)) > limit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The score of the position at the end of <paramref name="route"/> for firing at <paramref name="target"/>.</summary>
    private static double Score(Simulation simulation, IWorldQuery world, IReadOnlyList<Vec2> route, Target target, IReadOnlyList<Target> targets)
    {
        var weights = simulation.Config.SquadDoctrine.FiringPositions;
        var rules = simulation.Config.Combat;
        var point = route[^1];
        var prone = point.WithZ(world.GroundHeight(point.X, point.Y) + rules.Prone.AimHeight);
        var cover = targets.All(t => !LineOfSight.Between(world, EyeAt(world, t), prone, rules).IsVisible(rules)) ? 1 : 0;
        var concealment = world.ClassAt(point.X, point.Y) == TerrainClass.Forest ? 1 : 0;
        var height = Math.Max(0, world.GroundHeight(point.X, point.Y) - world.GroundHeight(target.Known.X, target.Known.Y));
        double length = 0, open = 0;
        for (var i = 1; i < route.Count; i++)
        {
            var step = route[i - 1].DistanceTo(route[i]);
            length += step;
            var middle = (route[i - 1] + route[i]) * 0.5;
            if (world.ClassAt(middle.X, middle.Y) != TerrainClass.Forest)
            {
                open += step;
            }
        }

        return (weights.CoverWeight * cover) + (weights.ConcealmentWeight * concealment) + (weights.HeightWeightPerMeter * height) -
            (weights.DistanceWeightPerMeter * length) - (weights.OpenGroundWeightPerMeter * open);
    }

    /// <summary>
    /// True if the member can walk straight to the spot: passable, inside the corridor, no slope too steep, no cliff it cannot
    /// rappel down, no unpermitted crossing.
    /// </summary>
    private static bool Reachable(Simulation simulation, IWorldQuery world, Squad squad, Unit member, Vec2 spot)
    {
        if (member.Profile is not { } profile || !world.IsInside(spot.X, spot.Y) ||
            !SquadSlots.CanStand(simulation, member, squad.Corridor, spot))
        {
            return false;
        }

        var from = member.Position.Xy;
        var samples = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(spot)));
        var previous = from;
        for (var s = 1; s <= samples; s++)
        {
            var point = from + ((spot - from) * ((double)s / samples));
            if (profile.BaseSpeed(world.ClassAt(point.X, point.Y)) <= 0 ||
                Math.Abs(world.GroundHeight(point.X, point.Y) - world.GroundHeight(previous.X, previous.Y)) / previous.DistanceTo(point) > profile.MaxSlope)
            {
                return false;
            }

            if (world.ClassAt(previous.X, previous.Y) == TerrainClass.Forest && world.ClassAt(point.X, point.Y) != TerrainClass.Forest &&
                !squad.Permits.Any(p => p.Covers(HardBoundary.ForestEdgeId, point)))
            {
                return false;
            }

            previous = point;
        }

        if (!profile.CanRappel && world.Cliffs.Any(cliff => Segments.FirstIntersection(from, spot, cliff) is not null))
        {
            return false;
        }

        return squad.Corridor?.HardBoundaries.All(b => Segments.FirstIntersection(from, spot, b.Line) is null) ?? true;
    }

    private static IReadOnlyList<IReadOnlyList<Vec2>> BoundariesOf(Unit agent, Squad? squad) =>
        ((agent.CurrentOrder as MoveThroughCorridor)?.Corridor ?? squad?.Corridor)?.HardBoundaries
            .Select(b => (IReadOnlyList<Vec2>)b.Line).ToList() ?? [];

    private static Vec3 AimAt(IWorldQuery world, Target target) =>
        target.Known.WithZ(world.GroundHeight(target.Known.X, target.Known.Y) + (target.Enemy.AimPoint.Z - target.Enemy.Position.Z));

    private static Vec3 EyeAt(IWorldQuery world, Target target) =>
        target.Known.WithZ(world.GroundHeight(target.Known.X, target.Known.Y) + (target.Enemy.EyePosition.Z - target.Enemy.Position.Z));
}
