using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The squad-wide forest-edge permit (backlog 0004, decided with the product owner 2026-11-19): once the squad has a permit
/// to leave the forest, it covers every member's every forest-edge crossing for the rest of that order, anywhere in the
/// corridor. A drawn boundary's permit keeps S2b's radius (permit radius plus the formation's half-width). A permit lasts
/// one order.
/// </summary>
public class SquadForestPermitTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);
    private const double PermitRadius = 10;

    // Forest west of x = 200, then open land with a 20 m strip of trees across the whole map at x 300–320, then open land
    // to B at (550, 101): the route leaves the forest at x = 200 and again 120 m on, at x = 320, far beyond the first
    // permit's radius plus the squad's half-width.
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(550, 101);
    private static readonly Vec2 SecondEdge = new(321, 101);

    private static RasterWorld ForestThenAStripOfTrees() =>
        TestMap.Create(widthMeters: 600, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 200, 0, 600, 200)
            .Fill(TerrainClass.Forest, 300, 0, 320, 200).Build();

    private static Corridor Corridor() => new([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);

    private static ScenarioRun Squad(RasterWorld world, SquadFormation formation) => Scenario.ForSquad()
        .WithTerrain(world)
        .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
        .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: PermitRadius)
        .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
        .Build()
        .Order("rj", new MoveThroughCorridor(Corridor()));

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void A_permit_to_leave_the_forest_covers_every_later_forest_edge_crossing_of_the_order(SquadFormation formation)
    {
        var world = ForestThenAStripOfTrees();
        var sim = Squad(world, formation);

        sim.RunUntilSquadSettled("r1", HalfAnHour);

        sim.Unit("rj").Should().BeAt(B);
        var request = Assert.Single(sim.Events.OfType<PermissionRequested>());
        Assert.Equal(HardBoundary.ForestEdgeId, request.BoundaryId);
        Assert.Contains(sim.Squad("r1").Permits, p => p.Covers(HardBoundary.ForestEdgeId, SecondEdge));
        foreach (var member in SpecSquad.Reference().Members)
        {
            var violations = RouteAudit.Violations(world, Corridor(), sim.Trail(member.UnitId), sim.Squad("r1").Permits);
            Assert.True(violations.Count == 0, $"{member.UnitId}: " + string.Join("; ", violations));
        }
    }

    [Fact]
    public void A_permit_to_cross_a_drawn_boundary_still_covers_only_its_radius_plus_the_half_width()
    {
        // Open land with a road across it at x = 250: the squad asks to cross the road, as in S2b.
        var road = new HardBoundary("road", [new(250, 0), new(250, 200)], BoundarySource.Order);
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, Start)
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: PermitRadius)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build()
            .Order("rj", new MoveThroughCorridor(new Corridor([Start, B], 80, SpecV01.SoftMargin, SpecV01.SoftPenalty, [road])));

        sim.RunUntil(s => s.Squad("r1").Permits.Count > 0, HalfAnHour, "the squad has a permit for the road");

        var permit = Assert.Single(sim.Squad("r1").Permits);
        Assert.Equal("road", permit.BoundaryId);
        Assert.Equal(PermitRadius + (5 * SpecSquad.WedgeStepMeters), permit.Radius, precision: 6);
        Assert.False(permit.Covers("road", new Vec2(250, 190)), "a road permit should not reach 89 m along the road");
    }

    [Fact]
    public void A_squad_wide_forest_edge_permit_lasts_one_order()
    {
        var sim = Squad(ForestThenAStripOfTrees(), SquadFormation.File);
        sim.RunUntil(s => s.Squad("r1").Permits.Any(p => p.BoundaryId == HardBoundary.ForestEdgeId), HalfAnHour, "the forest-edge permit");

        sim.Order("rj", new MoveThroughCorridor(Corridor())).Step(); // a new order from the platoon leader

        Assert.DoesNotContain(sim.Squad("r1").Permits, p => p.Covers(HardBoundary.ForestEdgeId, SecondEdge));
    }
}
