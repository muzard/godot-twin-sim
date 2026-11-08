using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// The foundation for casualty care (backlog 0002, C8–C9). A hit that wounds or puts an agent out of action logs a
/// <see cref="CasualtyOccurred"/> event, the hook a later care system will listen to. Care itself is not implemented
/// yet, so no one reacts: C9 changes when it is.
/// </summary>
public class CasualtyTests
{
    private static ScenarioRun RedFiresAtBlueWithTeamMateNearby() => Scenario.ForCombat()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.HoldFire, unitId: "1st-team")
        .WithFighter("blue-2", new Vec2(25, 21), Side.Blue, RulesOfEngagement.HoldFire, unitId: "1st-team")
        .WithFighter("red", new Vec2(21, 31), Side.Red, RulesOfEngagement.HoldFire)
        .Build()
        .Order("red", new FireAt("blue"));

    [Fact]
    public void C8_a_wounding_hit_logs_a_casualty_with_its_position()
    {
        var sim = RedFiresAtBlueWithTeamMateNearby();

        var hit = sim.RunUntilHit("blue", TimeSpan.FromMinutes(5));

        var casualty = Assert.Single(sim.Events.OfType<CasualtyOccurred>());
        Assert.Equal("blue", casualty.AgentId);
        Assert.Equal(hit.Time, casualty.Time);
        Assert.Equal(sim.Unit("blue").Position.Xy, casualty.Position);
        Assert.Equal(Health.Pinned, casualty.Health);
        Assert.Equal("1st-team", sim.Unit("blue").UnitId);
    }

    [Fact]
    public void C9_no_one_gives_aid_yet()
    {
        var sim = RedFiresAtBlueWithTeamMateNearby();
        sim.RunUntilHit("blue", TimeSpan.FromMinutes(5));
        sim.Order("red", new CeaseFire());
        var mate = sim.Unit("blue-2").Position;

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(mate, sim.Unit("blue-2").Position);
        Assert.Null(sim.Unit("blue-2").CurrentOrder);
        Assert.DoesNotContain(sim.Simulation.Units, u => u.Activity == UnitActivity.GivingAid);
    }
}
