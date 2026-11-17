using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Edge cases found in the code review of backlog 0003 (decisions of 2026-11-17):
/// <list type="bullet">
/// <item>An attacker counts as known, so its fire no longer starts the drill, only once a drill has answered it or the
/// leader gave an order while it was firing. Fire that came while a <see cref="FireAt"/> order kept the drill from
/// starting does not count.</item>
/// <item>A fallen team-mate still blocks the fire sector: no fire towards one's own, out of action or not.</item>
/// </list>
/// </summary>
public class KnownAttackerTests
{
    [Fact]
    public void An_attacker_whose_fire_met_only_a_fire_at_order_starts_the_drill_once_the_order_is_done()
    {
        // Blue (return fire) is ordered to fire at red-2, 30 m east. Red-1 fires at blue from 50 m north, never hitting;
        // red-2 is 31° off red-1's line to blue, outside its sector. While the order lasts there is no drill. When red-2
        // is out of action the order is done, and red-1's next round starts the drill.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red-1", new Vec2(21, 71), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .WithFighter("red-2", new Vec2(51, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new FireAt("red-2"))
            .Order("red-1", new FireAt("blue"));

        sim.RunUntil(s => s.Unit("red-2").Health == Health.OutOfAction, TimeSpan.FromMinutes(5), "red-2 out of action");
        var orderDone = sim.TimeSeconds;
        Assert.DoesNotContain(sim.Events.OfType<ContactDrillStageChanged>(), e => e.AgentId == "blue");

        sim.RunUntil(s => s.Unit("blue").DrillStage != ContactDrillStage.None, TimeSpan.FromSeconds(10), "blue starts the drill");

        var start = sim.Events.OfType<ContactDrillStageChanged>().First(e => e.AgentId == "blue");
        Assert.Equal(ContactDrillStage.SnapShots, start.Stage);
        Assert.Contains(sim.ShotsBy("red-1"), s => s.Time >= orderDone && s.Time <= start.Time);
    }

    [Fact]
    public void A_fallen_team_mate_still_blocks_the_fire_sector()
    {
        // Blue-2 stands 25° off blue's line to red and is put out of action by red-2 at close range. Blue, on free fire,
        // still does not fire at red past its fallen team-mate.
        var blue = new Vec2(21, 101);
        var mate = blue + new Vec2(Math.Cos(25 * Math.PI / 180) * 50, Math.Sin(25 * Math.PI / 180) * 50);
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 200).Build())
            .WithFighter("blue", blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("blue-2", mate, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("red-2", mate + new Vec2(0, 5), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red-2", new FireAt("blue-2"));

        sim.RunUntil(s => s.Unit("blue-2").Health == Health.OutOfAction, TimeSpan.FromMinutes(2), "blue-2 out of action");
        var fallen = sim.TimeSeconds;
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(sim.Simulation.CanSee("blue", "red"), "precondition: blue sees red");
        Assert.DoesNotContain(sim.ShotsBy("blue"), s => s.Time > fallen);
    }
}
