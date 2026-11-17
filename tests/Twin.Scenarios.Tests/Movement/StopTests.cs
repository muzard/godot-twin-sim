using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Movement;

/// <summary>
/// Behaviour of the Stop order (backlog 0001). One test per acceptance criterion.
/// </summary>
public class StopTests
{
    private static readonly Vec2 Start = new(100, 100);
    private static readonly Vec2 Destination = new(500, 500);
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(5);
    private const double SpeedMps = 5;

    // Criterion 1
    [Fact]
    public void Moving_unit_ordered_to_stop_is_idle_with_zero_velocity_after_the_next_tick()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: Start, speedMps: SpeedMps).Build();
        sim.Order("alpha", new MoveTo(Destination)).RunFor(TimeSpan.FromSeconds(10));
        sim.Unit("alpha").Should().BeMovingToward(Destination); // precondition: really moving

        sim.Order("alpha", new Stop()).Step();

        sim.Unit("alpha").Should().BeIdle();
    }

    // Decision 2026-11-16 (docs/onboarding-notes.md, finding 3)
    [Fact]
    public void Stopped_unit_has_no_current_order()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: Start, speedMps: SpeedMps).Build();
        sim.Order("alpha", new MoveTo(Destination)).RunFor(TimeSpan.FromSeconds(10));
        sim.Unit("alpha").Should().BeMovingToward(Destination); // precondition: really moving

        sim.Order("alpha", new Stop()).Step();

        sim.Unit("alpha").Should().HaveNoOrder();
    }

    // Criterion 2
    [Fact]
    public void Stopped_unit_halts_where_it_was_and_stays_on_the_ground()
    {
        var sim = Scenario.Create()
            .WithTerrain(TestTerrain.Hill(peak: new(300, 300), heightMeters: 25))
            .WithUnit("alpha", at: Start, speedMps: SpeedMps)
            .Build();
        sim.Order("alpha", new MoveTo(Destination)).RunFor(TimeSpan.FromSeconds(10));
        sim.Unit("alpha").Should().BeMovingToward(Destination); // precondition: really moving
        var whereOrdered = sim.Unit("alpha").Position.Xy;
        var oneTickTravel = SpeedMps * sim.Simulation.Config.TickSeconds;

        sim.Order("alpha", new Stop()).Step();
        var whereStopped = sim.Unit("alpha").Position.Xy;
        sim.RunFor(TimeSpan.FromSeconds(10));

        // Allowed slack: the unit may complete at most the tick in which the order takes effect.
        Assert.InRange(whereStopped.DistanceTo(whereOrdered), 0, oneTickTravel);
        sim.Unit("alpha").Should().BeAt(whereStopped).BeOnGround(sim.Terrain);
    }

    // Criterion 3
    [Fact]
    public void Stopping_an_idle_unit_is_accepted_and_has_no_effect()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: Start).Build();

        sim.Order("alpha", new Stop()).RunFor(TimeSpan.FromSeconds(5));

        sim.Unit("alpha").Should().BeIdle().BeAt(Start);
    }

    // Criterion 4
    [Fact]
    public void Move_order_after_stop_reaches_its_destination()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: Start, speedMps: SpeedMps).Build();
        var newDestination = new Vec2(100, 400);
        sim.Order("alpha", new MoveTo(Destination)).RunFor(TimeSpan.FromSeconds(10));
        sim.Order("alpha", new Stop()).Step();

        sim.Order("alpha", new MoveTo(newDestination))
           .RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        sim.Unit("alpha").Should().BeAt(newDestination).BeIdle();
    }

    // Criterion 5
    [Fact]
    public void Stop_for_unknown_unit_is_rejected()
    {
        var sim = Scenario.Create().Build();

        Assert.Throws<OrderRejectedException>(() => sim.Order("ghost", new Stop()));
    }
}
