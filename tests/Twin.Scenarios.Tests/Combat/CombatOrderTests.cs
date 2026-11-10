using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Combat orders meeting other orders, and orders that are refused (edge cases found while implementing backlog 0002).
/// </summary>
public class CombatOrderTests
{
    private static readonly Vec2 Blue = new(21, 21);

    private static ScenarioRun BlueAndRed(double redDistance = 100) => Scenario.ForCombat()
        .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 40).Build())
        .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
        .WithFighter("red", Blue + new Vec2(redDistance, 0), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    [Fact]
    public void A_fire_at_order_that_replaces_a_move_stops_the_agent()
    {
        var corridor = new Corridor([Blue, new Vec2(281, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        var sim = BlueAndRed(redDistance: 0.1).Order("blue", new MoveThroughCorridor(corridor)).Step(4);

        sim.Order("blue", new FireAt("red")).Step();
        var position = sim.Unit("blue").Position;
        sim.Order("blue", new CeaseFire()).Step(4);

        Assert.Equal(position, sim.Unit("blue").Position);
        Assert.Equal(Vec3.Zero, sim.Unit("blue").Velocity);
        Assert.NotEqual(UnitActivity.Moving, sim.Unit("blue").Activity);
    }

    [Fact]
    public void A_crawling_agent_moves_slowly_under_a_move_to_order_too()
    {
        var sim = BlueAndRed().Order("blue", new SetPosture(Posture.Prone)).RunFor(TimeSpan.FromSeconds(1.5));
        sim.Order("blue", new MoveTo(new Vec2(21, 1))).Step(2);
        var before = sim.Unit("blue").Position.Xy;

        sim.Step();

        var expected = sim.Unit("blue").MaxSpeedMps * SpecShooting.Prone.SpeedFactor * SpecV01.DtSeconds;
        Assert.Equal(expected, sim.Unit("blue").Position.Xy.DistanceTo(before), 9);
    }

    [Fact]
    public void An_agent_out_of_action_takes_no_orders()
    {
        var sim = BlueAndRed(redDistance: 10).Order("red", new FireAt("blue"));
        sim.RunUntil(s => s.Unit("blue").Health == Health.OutOfAction, TimeSpan.FromMinutes(5), "blue out of action");

        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new MoveTo(new Vec2(50, 21))));
        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new SetPosture(Posture.Standing)));
    }

    [Fact]
    public void Combat_orders_need_fighters_on_both_ends()
    {
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 40).Build())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithUnit("civilian", Blue + new Vec2(20, 0), SpecV01.Infantry)
            .Build();

        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new FireAt("civilian")));
        Assert.Throws<OrderRejectedException>(() => sim.Order("civilian", new FireAt("blue")));
        Assert.Throws<OrderRejectedException>(() => sim.Order("civilian", new SetPosture(Posture.Prone)));
        Assert.Throws<OrderRejectedException>(() => sim.Order("civilian", new CeaseFire()));
    }
}
