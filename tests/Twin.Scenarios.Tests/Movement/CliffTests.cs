using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Movement;

/// <summary>
/// Cliffs (MML Topographic Database "jyrkänne" lines, docs/adr/0010): a route may not cross a cliff, up or down, unless
/// the unit can rappel. The 2 m elevation model smooths many cliffs below the slope limit, so the line itself blocks.
/// </summary>
public class CliffTests
{
    private static readonly MovementProfile Infantry = SpecV01.Infantry;
    private static readonly MovementProfile Rappellers = SpecV01.Infantry with { CanRappel = true };
    private static readonly Vec2 Below = new(51, 21);
    private static readonly Vec2 Above = new(51, 61);

    // Gentle ground (slope 0.1, well under the 0.8 limit) with a cliff line across the whole map at y = 40.
    private static RasterWorld CliffAcross(params IReadOnlyList<Vec2>[] cliffs) =>
        TestMap.Create(widthMeters: 100, heightMeters: 80).WithHeights(p => 0.1 * p.Y).Build()
            .WithCliffs(cliffs.Length > 0 ? cliffs : [[new(0, 40), new(100, 40)]]);

    [Fact]
    public void A_cliff_cannot_be_crossed_down_or_up_even_where_the_elevation_model_is_gentle()
    {
        var world = CliffAcross();

        Assert.False(Pathfinder.FindPath(world, Infantry, Above, Below).Found, "down the cliff");
        Assert.False(Pathfinder.FindPath(world, Infantry, Below, Above).Found, "up the cliff");
    }

    [Fact]
    public void The_route_goes_round_the_end_of_a_cliff()
    {
        var cliff = new[] { new Vec2(0, 40), new Vec2(80, 40) };
        var world = CliffAcross(cliff);

        var path = Pathfinder.FindPath(world, Infantry, Above, Below);

        Assert.True(path.Found);
        Assert.Empty(TestGeometry.Crossings(path.Waypoints, cliff));
    }

    [Fact]
    public void A_unit_that_can_rappel_crosses_a_cliff_both_ways()
    {
        var world = CliffAcross();

        Assert.True(Pathfinder.FindPath(world, Rappellers, Above, Below).Found, "down the cliff");
        Assert.True(Pathfinder.FindPath(world, Rappellers, Below, Above).Found, "up the cliff");
    }

    [Fact]
    public void A_unit_that_can_rappel_crosses_a_cliff_steeper_than_its_slope_limit()
    {
        // A 6 m step at the cliff: one arc over it rises 6 m in 2 m.
        var world = TestMap.Create(widthMeters: 100, heightMeters: 80).WithHeights(p => p.Y > 40 ? 6 : 0).Build()
            .WithCliffs([[new(0, 40), new(100, 40)]]);

        Assert.True(Pathfinder.FindPath(world, Rappellers, Above, Below).Found);
        Assert.False(Pathfinder.FindPath(world, Infantry with { MaxSlope = 10 }, Above, Below).Found,
            "without rappelling, a cliff blocks however gentle the slope limit");
    }

    [Fact]
    public void A_cliff_is_impassable_terrain_not_a_boundary_to_ask_about()
    {
        // The reference route (hard boundaries ignored) cannot cross a cliff either, so there is nothing to ask for.
        var world = CliffAcross();
        var corridor = new Corridor([Above, Below], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: []);

        var reference = Pathfinder.FindPath(world, Infantry, Above, Below, new PathOptions(corridor, IgnoreHardBoundaries: true));

        Assert.False(reference.Found);
    }

    [Fact]
    public void An_agent_facing_a_cliff_it_cannot_rappel_reports_blocked_without_asking()
    {
        var world = CliffAcross();
        var corridor = new Corridor([Above, Below], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.Create().WithTerrain(world).WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: Above, profile: Infantry).Build()
            .Order("alpha", new MoveThroughCorridor(corridor));

        sim.RunUntil(s => s.Unit("alpha").Activity == UnitActivity.Blocked, TimeSpan.FromSeconds(5), "alpha blocked");

        Assert.Empty(sim.Events.OfType<PermissionRequested>());
        sim.Unit("alpha").Should().BeAt(Above);
    }

    [Fact]
    public void An_agent_does_not_step_over_a_cliff_between_its_position_and_its_cell_center()
    {
        // The agent stands just above a cliff that runs through its own cell, between it and the cell center (51, 41).
        var start = new Vec2(50.2, 41.8);
        var world = CliffAcross(new[] { new Vec2(0, 41.4), new Vec2(100, 41.4) });
        var corridor = new Corridor([start, Below], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.Create().WithTerrain(world).WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: start, profile: Infantry).Build()
            .Order("alpha", new MoveThroughCorridor(corridor));

        sim.RunUntil(s => s.Unit("alpha").Activity == UnitActivity.Blocked, TimeSpan.FromSeconds(5), "alpha blocked");

        Assert.Empty(TestGeometry.Crossings(sim.Trail("alpha"), world.Cliffs[0]));
    }

    [Fact]
    public void A_rappelling_agent_reaches_the_bottom_of_the_cliff()
    {
        var world = CliffAcross();
        var corridor = new Corridor([Above, Below], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.Create().WithTerrain(world).WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: Above, profile: Rappellers).Build()
            .Order("alpha", new MoveThroughCorridor(corridor));

        sim.RunUntil(s => s.Unit("alpha").IsIdle, TimeSpan.FromMinutes(5), "alpha idle");

        sim.Unit("alpha").Should().BeAt(Below);
    }
}
