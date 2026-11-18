using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Every order given straight to a squad member, not only a movement order, detaches it from the squad's movement until
/// it is ordered <see cref="Rejoin"/> (backlog 0004, S2a, decided by the product owner on 2026-11-18). The squad does not
/// wait for a detached member.
/// </summary>
public class DirectOrderTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    // The reference squad on its way in file on open ground; red stands well off the route and holds fire.
    private static ScenarioRun SquadOnTheMove()
    {
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference(), Start)
            .WithFighter("red", new Vec2(160, 180), Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        return sim.Order("rj", new MoveThroughCorridor(new Corridor([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])))
            .RunUntil(s => s.Unit("rj").Position.X > 150, TenMinutes, "rj past x = 150");
    }

    public static TheoryData<Order> DirectOrders => [new FireAt("red"), new SetPosture(Posture.Prone), new CeaseFire(), new Stop()];

    [Theory]
    [MemberData(nameof(DirectOrders))]
    public void Any_order_given_straight_to_a_member_detaches_it(Order order)
    {
        var sim = SquadOnTheMove();

        sim.Order("k1", order).Step();

        Assert.False(sim.Squad("r1").IsFollowing("k1"));
        Assert.Null(sim.Simulation.SlotOf("k1"));
    }

    [Fact]
    public void The_squad_does_not_wait_for_a_member_ordered_to_fire()
    {
        var sim = SquadOnTheMove();
        var ordered = sim.TimeSeconds;

        sim.Order("k1", new FireAt("red")).RunFor(TimeSpan.FromSeconds(60));

        Assert.DoesNotContain(sim.Events.OfType<Twin.Sim.Events.ActivityChanged>(),
            e => e.AgentId == "rj" && e.Activity == UnitActivity.WaitingForSquad && e.Time > ordered);
        sim.Unit("rj").Should().HaveActivity(UnitActivity.Moving);
    }
}
