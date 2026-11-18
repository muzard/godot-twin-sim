using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The squad leader's enemy estimate (backlog 0004 S5a2: UF7): the number of distinct enemies the leader knows of, and
/// whether a heavy machine gun is among them, which the leader recognises by the sound of its fire (WS3). Logged as
/// <see cref="EnemyEstimateChanged"/> when it changes. Blue cannot spot anyone here (see
/// <see cref="ContactReportTests"/>), so it learns of red only from contact reports and the sound of red's fire.
/// </summary>
public class EnemyEstimateTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    // The reference squad in a row from rj at (21, 61) to t1 at (45, 61), under return fire, everyone within 30 m (voice
    // range) of rj. Red forces, special forces under free fire, 20 m south of the row: one at each end, or a heavy machine
    // gun at the east end.
    private static ScenarioRun Squad(params (string Id, Vec2 At, WeaponProfile Weapon)[] red)
    {
        var scenario = Scenario.ForSquadSpotting()
            .WithCombatRules(ContactReportTests.BlindBlue)
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire);
        foreach (var (id, at, weapon) in red)
        {
            scenario.WithFighter(id, at, Side.Red, RulesOfEngagement.FreeFire, weapon, experience: Experience.SpecialForces);
        }

        var sim = scenario.Build();
        foreach (var (id, _, _) in red)
        {
            sim.Order(id, new Watch(new Vec2(0, 1)));
        }

        return sim;
    }

    private static WeaponProfile HiddenHeavyMachineGun => SpecSuppression.NeverHits(SpecSuppression.HeavyMachineGun) with { FlashSeconds = 0 };

    [Fact]
    public void UF7_the_estimate_counts_the_distinct_enemies_the_leader_knows_of()
    {
        var sim = Squad(
            ("red-west", new Vec2(15, 41), ContactReportTests.HiddenRifle),
            ("red-east", new Vec2(52, 41), ContactReportTests.HiddenRifle));

        sim.RunUntil(s => s.Squad("r1").EnemyEstimate.Fighters == 2, TwoMinutes, "rj knows of both red fighters");

        // rj may learn of both in the same step (0 → 2), so only the growth to 2 is checked.
        var changes = sim.Events.OfType<EnemyEstimateChanged>().ToList();
        Assert.Equal(2, changes[^1].Fighters);
        Assert.Equal(changes.Select(c => c.Fighters).Order(), changes.Select(c => c.Fighters));
        Assert.All(changes, c => Assert.Equal(("rj", "r1", false), (c.AgentId, c.SquadId, c.HeavyMachineGun)));
        Assert.True(sim.Simulation.KnowsOf("rj", "red-west") && sim.Simulation.KnowsOf("rj", "red-east"));
    }

    [Fact]
    public void UF7_the_leader_recognises_a_heavy_machine_gun_by_the_sound_of_its_fire()
    {
        var sim = Squad(("red-hmg", new Vec2(52, 41), HiddenHeavyMachineGun));

        sim.RunUntil(s => s.Events.OfType<ShotHeard>().Any(e => e.AgentId == "rj"), TwoMinutes, "rj hears red fire");
        sim.Step();

        Assert.True(sim.Squad("r1").EnemyEstimate.HeavyMachineGun, "the estimate should mark a heavy machine gun");
        Assert.Contains(sim.Events.OfType<EnemyEstimateChanged>(), c => c.HeavyMachineGun);
    }

    [Fact]
    public void UF7_rifle_fire_does_not_mark_a_heavy_machine_gun()
    {
        var sim = Squad(("red-east", new Vec2(52, 41), ContactReportTests.HiddenRifle));

        sim.RunUntil(s => s.Squad("r1").EnemyEstimate.Fighters == 1, TwoMinutes, "rj knows of red");
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.False(sim.Squad("r1").EnemyEstimate.HeavyMachineGun);
        Assert.DoesNotContain(sim.Events.OfType<EnemyEstimateChanged>(), c => c.HeavyMachineGun);
    }

    [Fact]
    public void UF7_estimate_changes_survive_a_round_trip_through_the_log()
    {
        var sim = Squad(("red-hmg", new Vec2(52, 41), HiddenHeavyMachineGun));
        sim.RunFor(TimeSpan.FromSeconds(60));
        var text = EventLog.ToJsonLines(sim.Events);

        Assert.Contains(nameof(EnemyEstimateChanged), text, StringComparison.Ordinal);
        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }
}
