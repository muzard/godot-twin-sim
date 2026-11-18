using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Edge cases of the enemy estimate found in review (backlog 0004 S5a2): a heavy machine gun on the squad's own side is
/// not an enemy's and does not mark the estimate; one the leader cannot hear marks it when a contact report names it.
/// Blue cannot spot anyone here (see <see cref="ContactReportTests"/>).
/// </summary>
public class EnemyEstimateEdgeCaseTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    [Fact]
    public void A_heavy_machine_gun_on_the_squads_own_side_does_not_mark_the_estimate()
    {
        // blue-hmg, on blue's side but not in the squad, fires at red, which holds fire, 40 m east of the row.
        var sim = Scenario.ForSquadSpotting()
            .WithCombatRules(ContactReportTests.BlindBlue)
            .WithTerrain(TestMap.Create(widthMeters: 160, heightMeters: 100).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire)
            .WithFighter("blue-hmg", new Vec2(21, 21), Side.Blue, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecSuppression.HeavyMachineGun) with { FlashSeconds = 0 }, experience: Experience.SpecialForces)
            .WithFighter("red", new Vec2(85, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue-hmg", new Watch(new Vec2(1, 0)));
        sim.RunUntilKnows("blue-hmg", "red", TwoMinutes);

        sim.Order("blue-hmg", new FireAt("red"))
            .RunUntil(s => s.Events.OfType<ShotHeard>().Any(e => e.AgentId == "rj"), TwoMinutes, "rj hears blue-hmg fire");
        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.False(sim.Squad("r1").EnemyEstimate.HeavyMachineGun);
    }

    [Fact]
    public void A_heavy_machine_gun_the_leader_cannot_hear_marks_the_estimate_when_a_contact_report_names_it()
    {
        // red-hmg's fire carries only 5 m: the member it fires at, at the east end of the row, does not hear it either, but
        // knows what it is from being fired at, and its contact report names it to rj, within voice range.
        var quietHeavyMachineGun = SpecSuppression.NeverHits(SpecSuppression.HeavyMachineGun) with { FlashSeconds = 0, SoundRangeMeters = 5 };
        var sim = Scenario.ForSquadSpotting()
            .WithCombatRules(ContactReportTests.BlindBlue)
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire)
            .WithFighter("red-hmg", new Vec2(52, 41), Side.Red, RulesOfEngagement.FreeFire, quietHeavyMachineGun, experience: Experience.SpecialForces)
            .Build()
            .Order("red-hmg", new Watch(new Vec2(0, 1)));

        sim.RunUntil(s => s.Squad("r1").EnemyEstimate.HeavyMachineGun, TwoMinutes, "the estimate marks a heavy machine gun");

        Assert.DoesNotContain(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "rj");
        Assert.Contains(sim.Events.OfType<ContactReported>(), e => e.AttackerId == "red-hmg");
    }
}
