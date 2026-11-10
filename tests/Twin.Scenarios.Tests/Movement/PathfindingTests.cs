using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Movement;

/// <summary>
/// A* on the 2 m grid (spec v0.1, "Liikemalli"): 8-neighbour arcs, cost = travel time in seconds, speed = mean of the
/// two cells' base speeds times the slope factor, no corner cutting, slopes steeper than the limit impassable.
/// Paths are the centers of the cells visited, from the start cell to the goal cell. M1–M7.
/// </summary>
public class PathfindingTests
{
    private static readonly MovementProfile Infantry = SpecV01.Infantry;

    [Fact]
    public void M1_on_flat_open_ground_the_path_is_straight_and_costs_distance_over_speed()
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 20).Build();
        var from = new Vec2(11, 11);
        var to = new Vec2(51, 11);

        var path = Pathfinder.FindPath(world, Infantry, from, to);

        Assert.True(path.Found);
        Assert.All(path.Waypoints, p => Assert.Equal(11, p.Y, 9));
        Assert.Equal(from, path.Waypoints[0]);
        Assert.Equal(to, path.Waypoints[^1]);
        Assert.Equal(from.DistanceTo(to) / Infantry.OpenMps, path.CostSeconds, 9);
    }

    [Fact]
    public void M1_a_diagonal_path_on_flat_open_ground_costs_distance_over_speed()
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 60).Build();
        var from = new Vec2(11, 11);
        var to = new Vec2(51, 51);

        var path = Pathfinder.FindPath(world, Infantry, from, to);

        Assert.All(path.Waypoints, p => Assert.Equal(p.X, p.Y, 9));
        Assert.Equal(from.DistanceTo(to) / Infantry.OpenMps, path.CostSeconds, 9);
    }

    [Fact]
    public void M2_when_a_wall_has_a_gap_the_path_goes_through_the_gap()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 60)
            .Fill(TerrainClass.Building, fromX: 0, fromY: 30, toX: 100, toY: 32)
            .Fill(TerrainClass.Open, fromX: 70, fromY: 30, toX: 72, toY: 32)
            .Build();
        var gap = new Vec2(71, 31);

        var path = Pathfinder.FindPath(world, Infantry, new Vec2(21, 11), new Vec2(21, 51));

        Assert.True(path.Found);
        Assert.Contains(gap, path.Waypoints);
    }

    [Fact]
    public void M3_a_diagonal_step_across_a_building_corner_is_not_allowed()
    {
        var map = TestMap.Parse("""
            A#
            .B
            """);

        var path = Pathfinder.FindPath(map.World, Infantry, map.Point('A'), map.Point('B'));

        // The direct diagonal would cut the building's corner, so the path steps around it through the open cell.
        Assert.Equal([map.Point('A'), new Vec2(1, 1), map.Point('B')], path.Waypoints);
        Assert.Equal(2 * 2.0 / Infantry.OpenMps, path.CostSeconds, 9);
    }

    [Fact]
    public void M4_uphill_is_slower_than_downhill_over_the_same_distance()
    {
        const double slope = 0.125; // exactly representable, so float32 heights give exactly this slope
        var world = TestMap.Create(widthMeters: 60, heightMeters: 40).WithHeights(p => slope * p.X).Build();
        var low = new Vec2(11, 21);
        var high = new Vec2(51, 21);
        var distance = low.DistanceTo(high);

        var up = Pathfinder.FindPath(world, Infantry, low, high);
        var down = Pathfinder.FindPath(world, Infantry, high, low);

        Assert.True(up.CostSeconds > down.CostSeconds, $"uphill {up.CostSeconds} s should be slower than downhill {down.CostSeconds} s");
        Assert.Equal(distance / (Infantry.OpenMps * SpecV01.SlopeFactor(slope)), up.CostSeconds, 6);
        Assert.Equal(distance / (Infantry.OpenMps * SpecV01.SlopeFactor(-slope)), down.CostSeconds, 6);
    }

    [Fact]
    public void M4_a_slope_steeper_than_the_limit_is_impassable_both_up_and_down()
    {
        // A 1.5 rise per meter across the whole map between y = 20 and y = 30: even a diagonal arc over it
        // (2 m rise over 2.83 m) is steeper than the 0.8 limit.
        var world = TestMap.Create(widthMeters: 60, heightMeters: 60)
            .WithHeights(p => 1.5 * Math.Clamp(p.Y - 20, 0, 10))
            .Build();
        var below = new Vec2(11, 11);
        var above = new Vec2(11, 41);

        Assert.False(Pathfinder.FindPath(world, Infantry, below, above).Found);
        Assert.False(Pathfinder.FindPath(world, Infantry, above, below).Found);
    }

    [Fact]
    public void M5_a_bog_is_avoided_when_the_way_around_is_faster()
    {
        // 8 m of bog across the way, with dry ground 6 m to the side.
        var world = TestMap.Create(widthMeters: 100, heightMeters: 60)
            .Fill(TerrainClass.Bog, fromX: 0, fromY: 26, toX: 56, toY: 34)
            .Build();

        var path = Pathfinder.FindPath(world, Infantry, new Vec2(51, 11), new Vec2(51, 51));

        Assert.True(path.Found);
        Assert.DoesNotContain(path.Waypoints, p => world.ClassAt(p.X, p.Y) == TerrainClass.Bog);
    }

    [Fact]
    public void M5_a_bog_is_crossed_when_the_way_around_is_slower()
    {
        // 4 m of bog across the way; the dry way around is 40 m to the side.
        var world = TestMap.Create(widthMeters: 100, heightMeters: 60)
            .Fill(TerrainClass.Bog, fromX: 0, fromY: 29, toX: 90, toY: 31)
            .Build();

        var path = Pathfinder.FindPath(world, Infantry, new Vec2(51, 11), new Vec2(51, 51));

        Assert.True(path.Found);
        Assert.Contains(path.Waypoints, p => world.ClassAt(p.X, p.Y) == TerrainClass.Bog);
    }

    [Fact]
    public void M6_in_symmetric_terrain_a_tie_always_resolves_to_the_same_path()
    {
        // A building block exactly in the middle: going round it on the left or on the right costs the same.
        static RasterWorld Symmetric() => TestMap.Create(widthMeters: 42, heightMeters: 42)
            .Fill(TerrainClass.Building, fromX: 16, fromY: 16, toX: 26, toY: 26)
            .Build();
        var from = new Vec2(21, 5);
        var to = new Vec2(21, 37);

        var world = Symmetric();

        var first = Pathfinder.FindPath(world, Infantry, from, to);
        var sameWorldAgain = Pathfinder.FindPath(world, Infantry, from, to);
        var freshWorld = Pathfinder.FindPath(Symmetric(), Infantry, from, to);

        Assert.True(first.Found);
        Assert.Equal(first.Waypoints, sameWorldAgain.Waypoints);
        Assert.Equal(first.Waypoints, freshWorld.Waypoints);
    }

    [Fact(Timeout = 10_000)]
    public async Task M7_searching_for_an_unreachable_point_returns_no_path_and_does_not_hang()
    {
        // B is on an island in a lake.
        var map = TestMap.Parse("""
            ...........
            .~~~~~~~~~.
            .~~~~~~~~~.
            .~~~~B~~~~.
            .~~~~~~~~~.
            .~~~~~~~~~.
            A..........
            """);

        var path = await Task.Run(() => Pathfinder.FindPath(map.World, Infantry, map.Point('A'), map.Point('B')));

        Assert.False(path.Found);
        Assert.Empty(path.Waypoints);
    }
}
