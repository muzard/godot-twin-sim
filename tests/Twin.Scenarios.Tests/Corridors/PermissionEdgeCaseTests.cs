using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Corridors;

/// <summary>
/// Edge cases of the permission flow and corridor orders found while implementing C4–C7: several boundaries in a
/// row, a leader slower than the timeout, a boundary inside the start cell, and orders the agent could never carry out.
/// </summary>
public class PermissionEdgeCaseTests
{
    private static readonly Vec2 A = new(51, 31);
    private static readonly Vec2 B = new(51, 71);

    private static HardBoundary FullWidth(string id, double y) => new(id, [new(0, y), new(100, y)], BoundarySource.Order);

    private static ScenarioRun Ordered(Corridor corridor, LeaderAnswer answer, TimeSpan delay, TimeSpan timeout, Vec2? start = null) =>
        Scenario.Create()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
            .WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: start ?? A, profile: SpecV01.Infantry)
            .WithLeader(answer, after: delay, permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: timeout)
            .Build()
            .Order("alpha", new MoveThroughCorridor(corridor));

    private static Corridor CorridorWith(params HardBoundary[] boundaries) =>
        new([A, B], Width: 60, SoftMargin: SpecV01.SoftMargin, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: boundaries);

    [Fact]
    public void After_a_grant_the_agent_asks_again_for_the_next_boundary_and_then_arrives()
    {
        var sim = Ordered(CorridorWith(FullWidth("road", 45), FullWidth("ditch", 55)), LeaderAnswer.Grant,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));

        sim.RunUntil(s => s.Unit("alpha").IsIdle, TimeSpan.FromMinutes(10), "alpha idle");

        Assert.Equal(["road", "ditch"], sim.Events.OfType<PermissionRequested>().Select(r => r.BoundaryId));
        sim.Unit("alpha").Should().BeAt(B);
    }

    [Fact]
    public void A_leader_slower_than_the_timeout_is_not_waited_for()
    {
        var sim = Ordered(CorridorWith(FullWidth("road", 50)), LeaderAnswer.Grant, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(60));

        sim.RunUntil(s => s.Unit("alpha").Activity == UnitActivity.Blocked, TimeSpan.FromMinutes(5), "alpha blocked");

        Assert.Single(sim.Events.OfType<PermissionTimedOut>());
        Assert.Empty(sim.Events.OfType<PermissionAnswered>());
    }

    [Fact]
    public void An_answer_arriving_exactly_at_the_timeout_counts()
    {
        var sim = Ordered(CorridorWith(FullWidth("road", 50)), LeaderAnswer.Grant, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        sim.RunUntil(s => s.Unit("alpha").IsIdle, TimeSpan.FromMinutes(10), "alpha idle");

        Assert.Single(sim.Events.OfType<PermissionAnswered>());
        Assert.Empty(sim.Events.OfType<PermissionTimedOut>());
    }

    [Fact]
    public void A_boundary_between_the_agent_and_its_cell_center_is_not_crossed_without_permission()
    {
        // The agent stands in cell (25, 15), whose center is (51, 31); the boundary runs between it and the center.
        var start = new Vec2(50.2, 30.2);
        var corridor = new Corridor([start, B], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: [FullWidth("tape", 30.6)]);
        var sim = Ordered(corridor, LeaderAnswer.Deny, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), start);

        sim.RunUntil(s => s.Unit("alpha").Activity == UnitActivity.Blocked, TimeSpan.FromMinutes(2), "alpha blocked");

        Assert.Equal("tape", sim.Events.OfType<PermissionRequested>().Single().BoundaryId);
        Assert.Empty(TestGeometry.Crossings(sim.Trail("alpha"), corridor.HardBoundaries[0].Line));
    }

    [Fact]
    public void Corridor_order_for_an_agent_outside_the_outer_limit_is_rejected()
    {
        var sim = Ordered(CorridorWith(), LeaderAnswer.Grant, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
        var far = new Corridor([new(5, 31), new(5, 71)], Width: 4, SoftMargin: 2, SoftPenalty: 1, HardBoundaries: []);

        Assert.Throws<OrderRejectedException>(() => sim.Order("alpha", new MoveThroughCorridor(far)));
    }

    [Fact]
    public void Corridor_order_ending_in_water_is_rejected()
    {
        var world = TestMap.Create(widthMeters: 100, heightMeters: 100).Fill(TerrainClass.Water, 40, 60, 60, 80).Build();
        var sim = Scenario.Create().WithTerrain(world).WithUnit("alpha", at: A, profile: SpecV01.Infantry).Build();

        Assert.Throws<OrderRejectedException>(() => sim.Order("alpha", new MoveThroughCorridor(CorridorWith())));
    }
}
