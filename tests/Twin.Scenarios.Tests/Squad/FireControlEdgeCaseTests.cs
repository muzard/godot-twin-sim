using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Edge cases of fire control found in review (backlog 0004 S5b): a member fires back at its own attacker outside its
/// fire area, and after a squad cease fire, and a cease fire calls off a distribution already due. The layout is
/// <see cref="FireControlTests"/>'s: the squad in a row facing north from rj at (100, 61), red-left and red-right 100 m
/// north, here with red-attacker due east of the row's end at (190, 61), firing a rifle that never hits at t1, the
/// easternmost member, once it is ordered to: t1 then has no squad mate in its own fire sector toward it, and red-attacker
/// none of its own side in its sector toward t1.
/// </summary>
public class FireControlEdgeCaseTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    private static WeaponProfile NeverHits(SquadMemberSpec member) =>
        SpecSuppression.NeverHits(member.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);

    private static ScenarioRun Squad(bool redRight = true)
    {
        var scenario = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(100, 61), rules: RulesOfEngagement.ReturnFire, weapons: NeverHits)
            .WithFighter("red-left", new Vec2(60, 161), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("red-attacker", new Vec2(190, 61), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle), experience: Experience.SpecialForces);
        if (redRight)
        {
            scenario.WithFighter("red-right", new Vec2(140, 161), Side.Red, RulesOfEngagement.HoldFire);
        }

        return scenario.Build().Order("red-attacker", new Watch(new Vec2(-1, 0)));
    }

    private static ScenarioRun Distributed(ScenarioRun sim) =>
        sim.RunUntil(s => s.Unit("t1").FireArea is not null, TwoMinutes, "t1 has its fire area").RunFor(TimeSpan.FromSeconds(10));

    // red-attacker fires a few shots at the member and stops, so the member's suppression wears off and it returns fire.
    private static ScenarioRun Attacked(ScenarioRun sim, string id)
    {
        sim.RunUntilKnows("red-attacker", id, TwoMinutes);
        sim.Order("red-attacker", new FireAt(id)).RunFor(TimeSpan.FromSeconds(5));
        return sim.Order("red-attacker", new CeaseFire()).RunFor(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void A_member_fires_back_at_its_own_attacker_outside_its_fire_area()
    {
        var sim = Attacked(Distributed(Squad()), "t1");

        Assert.False(sim.Unit("t1").FireArea!.Covers(sim.Unit("red-attacker").Position.Xy - sim.Unit("t1").Position.Xy),
            "precondition: red-attacker is outside t1's fire area");
        Assert.Contains(sim.Events.OfType<ShotFired>(), e => e.AgentId == "t1" && e.TargetId == "red-attacker");
    }

    [Fact]
    public void After_a_squad_cease_fire_a_member_still_fires_back_at_its_own_attacker()
    {
        var sim = Distributed(Squad());
        sim.Command("rj", new SquadCeaseFire());
        sim.RunFor(TimeSpan.FromSeconds(20));

        Attacked(sim, "t1"); // (red-attacker is a new enemy to rj, so rj distributes fire again meanwhile)

        Assert.Contains(sim.Events.OfType<ShotFired>(), e => e.AgentId == "t1" && e.TargetId == "red-attacker");
    }

    [Fact]
    public void A_squad_cease_fire_calls_off_a_distribution_already_due()
    {
        // Only red-left in front, so rj learns of no other enemy that would bring a new distribution.
        var sim = Squad(redRight: false);
        sim.RunUntil(s => s.Simulation.KnowsOf("rj", "red-left"), TwoMinutes, "rj knows of red-left");

        sim.Order("rj", new SquadCeaseFire()).RunFor(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(sim.Events.OfType<CommandIssued>(),
            e => e.AgentId == "rj" && e.Command.StartsWith(SpecSquad.DirectSquadFireCommand, StringComparison.Ordinal) &&
                e.Time > sim.Events.OfType<CommandIssued>().Last(c => c.Command.StartsWith(nameof(SquadCeaseFire), StringComparison.Ordinal)).Time);
    }
}
