using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Corridors;

/// <summary>
/// Corridor geometry in pathfinding (spec v0.1, "Käytävä"): no penalty within w/2 of the centerline, cost factor
/// k(d) = 1 + α((d − w/2)/m)² in the soft zone, impassable beyond w/2 + m. Hard boundaries cannot be crossed
/// except within a permit's radius. C1–C3, C6.
/// </summary>
public class CorridorPathTests
{
    // C1/C2: a 240 m long lake fills the north half of the inner zone and reaches deep into the northern soft zone.
    // A and B are near the inner zone's northern edge, so going round the lake's north side is shorter but runs
    // deep in the soft zone; going round its south side stays in the inner zone but is longer.
    private static readonly Vec2 LakeA = new(11, 75);
    private static readonly Vec2 LakeB = new(289, 75);
    private static readonly IReadOnlyList<Vec2> LakeCenterline = [new(0, 60), new(300, 60)];
    private const double LakeCorridorWidth = 40;

    private static RasterWorld LakeWorld() => TestMap.Create(widthMeters: 300, heightMeters: 120)
        .Fill(TerrainClass.Water, fromX: 30, fromY: 46, toX: 270, toY: 92)
        .Build();

    private static Corridor LakeCorridor(double alpha) =>
        new(LakeCenterline, LakeCorridorWidth, SpecV01.SoftMargin, alpha, HardBoundaries: []);

    private static double MaxDistanceFromCenterline(PathResult path, IReadOnlyList<Vec2> centerline) =>
        path.Waypoints.Max(p => TestGeometry.DistanceToPolyline(p, centerline));

    [Fact]
    public void C1_with_zero_penalty_the_path_uses_the_soft_zone_at_no_extra_cost()
    {
        var world = LakeWorld();

        var inCorridor = Pathfinder.FindPath(world, SpecV01.Infantry, LakeA, LakeB, new PathOptions(Corridor: LakeCorridor(alpha: 0)));
        var unconstrained = Pathfinder.FindPath(world, SpecV01.Infantry, LakeA, LakeB);

        Assert.True(MaxDistanceFromCenterline(inCorridor, LakeCenterline) > LakeCorridorWidth / 2, "path should use the soft zone");
        Assert.Equal(unconstrained.CostSeconds, inCorridor.CostSeconds, 9);
    }

    [Fact]
    public void C2_with_a_penalty_the_path_stays_in_the_inner_zone_when_the_inner_detour_is_short_enough()
    {
        var world = LakeWorld();

        var free = Pathfinder.FindPath(world, SpecV01.Infantry, LakeA, LakeB, new PathOptions(Corridor: LakeCorridor(alpha: 0)));
        var penalised = Pathfinder.FindPath(world, SpecV01.Infantry, LakeA, LakeB, new PathOptions(Corridor: LakeCorridor(alpha: SpecV01.SoftPenalty)));

        // Precondition: without the penalty the soft-zone route is the cheaper one (about 8 % here).
        Assert.True(MaxDistanceFromCenterline(free, LakeCenterline) > LakeCorridorWidth / 2);
        Assert.True(penalised.Found);
        Assert.True(MaxDistanceFromCenterline(penalised, LakeCenterline) <= (LakeCorridorWidth / 2) + 1e-9,
            "with α > 0 the path should stay in the inner zone");
    }

    [Fact]
    public void C3_the_path_never_goes_beyond_the_outer_limit()
    {
        // An L-shaped corridor on open ground: the straight line from A to B runs far outside it.
        var world = TestMap.Create(widthMeters: 200, heightMeters: 200).Build();
        IReadOnlyList<Vec2> centerline = [new(20, 20), new(20, 180), new(180, 180)];
        var corridor = new Corridor(centerline, Width: 20, SoftMargin: 10, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: []);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(21, 21), new Vec2(179, 179), new PathOptions(Corridor: corridor));

        Assert.True(path.Found);
        Assert.True(MaxDistanceFromCenterline(path, centerline) <= 20 + 1e-9);
    }

    [Fact]
    public void C3_if_the_only_way_runs_outside_the_outer_limit_there_is_no_path()
    {
        // A wall across the whole corridor; the gap in it is outside the outer limit.
        var world = TestMap.Create(widthMeters: 200, heightMeters: 100)
            .Fill(TerrainClass.Building, fromX: 0, fromY: 50, toX: 160, toY: 52)
            .Build();
        var corridor = new Corridor([new(51, 0), new(51, 100)], Width: 40, SoftMargin: 25, SoftPenalty: 0, HardBoundaries: []);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(51, 21), new Vec2(51, 81), new PathOptions(Corridor: corridor));

        Assert.False(path.Found);
    }

    [Fact]
    public void Without_a_permit_the_path_does_not_cross_a_hard_boundary()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 100).Build();
        var road = new HardBoundary("road", [new(0, 50), new(95, 50)], BoundarySource.Order);
        var corridor = WideCorridor(road);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(51, 31), new Vec2(51, 71), new PathOptions(Corridor: corridor));

        Assert.True(path.Found);
        Assert.Empty(TestGeometry.Crossings(path.Waypoints, road.Line));
    }

    [Fact]
    public void Reference_path_that_ignores_hard_boundaries_crosses_them()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 100).Build();
        var road = new HardBoundary("road", [new(0, 50), new(100, 50)], BoundarySource.Order);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(51, 31), new Vec2(51, 71),
            new PathOptions(Corridor: WideCorridor(road), IgnoreHardBoundaries: true));

        Assert.True(path.Found);
        Assert.Single(TestGeometry.Crossings(path.Waypoints, road.Line));
        Assert.Equal(40 / SpecV01.Infantry.OpenMps, path.CostSeconds, 9);
    }

    [Fact]
    public void Leaving_forest_crosses_a_hard_boundary_but_entering_forest_does_not()
    {
        // Decision 2026-11-08: every forest edge is a hard boundary when leaving the forest (losing overhead concealment).
        var map = TestMap.Parse("""
            FFFFF
            FFFFF
            FFFFF
            .....
            .....
            """);
        var inForest = TestMap.CellCenter(column: 2, row: 3);
        var inOpen = TestMap.CellCenter(column: 2, row: 0);

        var outOfForest = Pathfinder.FindPath(map.World, SpecV01.Infantry, inForest, inOpen);
        var intoForest = Pathfinder.FindPath(map.World, SpecV01.Infantry, inOpen, inForest);

        Assert.False(outOfForest.Found);
        Assert.True(intoForest.Found);
    }

    [Fact]
    public void C6_with_a_permit_the_crossing_happens_within_the_permit_radius_and_nowhere_else()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 100).Build();
        var road = new HardBoundary("road", [new(0, 50), new(100, 50)], BoundarySource.Order);
        var permit = new CrossingPermit("road", At: new Vec2(85, 50), Radius: 10);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(51, 31), new Vec2(51, 71),
            new PathOptions(Corridor: WideCorridor(road), Permits: [permit]));

        Assert.True(path.Found);
        var crossing = Assert.Single(TestGeometry.Crossings(path.Waypoints, road.Line));
        Assert.True(crossing.DistanceTo(permit.At) <= permit.Radius + 1e-9,
            $"crossed at {crossing}, {crossing.DistanceTo(permit.At):0.##} m from the permit point");
    }

    [Fact]
    public void C6_a_permit_for_one_boundary_does_not_open_another()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 100).Build();
        var road = new HardBoundary("road", [new(0, 50), new(100, 50)], BoundarySource.Order);
        var permit = new CrossingPermit("ditch", At: new Vec2(51, 50), Radius: 50);

        var path = Pathfinder.FindPath(world, SpecV01.Infantry, new Vec2(51, 31), new Vec2(51, 71),
            new PathOptions(Corridor: WideCorridor(road), Permits: [permit]));

        Assert.False(path.Found);
    }

    // Centerline along x = 51; inner zone x 21..81, outer limit x -4..106: covers the whole 100 m map.
    private static Corridor WideCorridor(HardBoundary boundary) =>
        new([new(51, 0), new(51, 100)], Width: 60, SoftMargin: SpecV01.SoftMargin, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: [boundary]);
}
