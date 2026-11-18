using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Contact by fire reaches the squad leader (backlog 0004 S5a2: UF1). A member shot at reports contact in 0003's drill;
/// the report is spoken as a spot report (S5a1): squad mates within voice range learn of the attacker, and it is passed on
/// until the leader knows, and then the squad halts. The leader's own movement order survives its drill: after it the
/// squad is halted, and <see cref="ResumeMovement"/> resumes the order.
/// <para>
/// So that contact reports are the only way the squad learns of red, blue cannot spot anyone in the time these tests run
/// (its spotting takes 1000 s), red's rifle has no muzzle flash, and red, special forces, spots quickly.
/// </para>
/// </summary>
public class ContactReportTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>Spotting as <see cref="SpecSpotting"/>, except that conscripts and regulars need 1000 s; special forces 1 s.</summary>
    internal static CombatRules BlindBlue { get; } = SpecSpotting.Rules with
    {
        Observation = SpecSpotting.Observation with { ConscriptSeconds = 1000, RegularSeconds = 1000, SpecialForcesSeconds = 1 },
    };

    /// <summary>A rifle that never hits and has no muzzle flash.</summary>
    internal static WeaponProfile HiddenRifle { get; } = SpecSuppression.NeverHits(SpecShooting.Rifle) with { FlashSeconds = 0 };

    // The reference squad stands in a row, 3 m apart, from rj at (21, 61) to t1 at (45, 61), under return fire. It has
    // not moved, so everyone watches north. Red, special forces under free fire, lies 20 m south of t1's end of the row.
    // Voice range 10 m: rj (x = 21) hears nobody east of x = 31.
    private static ScenarioRun RowWithRedSouthOfItsTail(double voiceRangeMeters) => Scenario.ForSquadSpotting()
        .WithCombatRules(BlindBlue)
        .WithSquadDoctrine(SpecSpotting.Doctrine with { VoiceRangeMeters = voiceRangeMeters })
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire)
        .WithFighter("red", new Vec2(50, 41), Side.Red, RulesOfEngagement.FreeFire, HiddenRifle, experience: Experience.SpecialForces)
        .Build()
        .Order("red", new Watch(new Vec2(0, 1)));

    private static ContactReported RunUntilAMemberReportsContact(ScenarioRun sim)
    {
        sim.RunUntil(s => s.Events.OfType<ContactReported>().Any(e => e.AgentId != "red"), OneMinute, "a member reports contact");
        return sim.Events.OfType<ContactReported>().First(e => e.AgentId != "red");
    }

    [Fact]
    public void UF1_squad_mates_within_voice_range_learn_of_the_attacker_from_a_contact_report()
    {
        var sim = RowWithRedSouthOfItsTail(voiceRangeMeters: 10);
        var contact = RunUntilAMemberReportsContact(sim);
        var reporter = sim.Unit(contact.AgentId).Position.Xy;

        Assert.Equal("red", contact.AttackerId);
        Assert.True(reporter.X > 31, $"precondition: {contact.AgentId} is out of rj's voice range");
        // A member that already knew of red (from its own contact report in the same step, say) learns nothing new.
        bool KnewAlready(string id) => sim.Events.Any(e => e.AgentId == id && e.Time <= contact.Time + 1e-9 &&
            (e is ContactReported or EnemySpotted || (e is SpotReceived r && r.ReporterId != contact.AgentId)));
        foreach (var member in SpecSquad.Reference().Members.Where(m => m.UnitId != contact.AgentId && !KnewAlready(m.UnitId)))
        {
            var heard = sim.ReceivedFrom(member.UnitId, contact.AgentId);
            if (sim.Unit(member.UnitId).Position.Xy.DistanceTo(reporter) <= 10)
            {
                Assert.True(heard is not null, $"{member.UnitId} is within 10 m of {contact.AgentId} and should hear its contact report");
                Assert.Equal(("red", contact.Time), (heard.EnemyId, heard.Time));
            }
            else
            {
                Assert.True(heard is null, $"{member.UnitId} is out of voice range of {contact.AgentId}");
            }
        }
    }

    [Fact]
    public void UF1_a_contact_report_is_passed_on_to_a_leader_out_of_voice_range_and_the_squad_halts()
    {
        var sim = RowWithRedSouthOfItsTail(voiceRangeMeters: 10);
        var contact = RunUntilAMemberReportsContact(sim);

        sim.RunUntil(s => s.Events.OfType<SquadHalted>().Any(), OneMinute, "the squad halts");

        Assert.True(sim.Simulation.KnowsOf("rj", "red"), "rj should know of red");
        Assert.Null(sim.SpottedBy("rj", "red"));
        var told = sim.Events.OfType<SpotReceived>().First(e => e.AgentId == "rj" && e.EnemyId == "red");
        Assert.True(told.Time >= contact.Time + SpecSquad.RelaySeconds - 1e-9, "rj hears it only when it is passed on");
        Assert.Equal("red", sim.Events.OfType<SquadHalted>().First().EnemyId);
    }

    [Fact]
    public void UF1_a_leader_shot_at_knows_its_attacker_from_its_own_contact_report()
    {
        // As the row above, but red stands 20 m south of rj and holds fire until it is ordered to fire at rj.
        var sim = Scenario.ForSquadSpotting()
            .WithCombatRules(BlindBlue)
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 100).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(21, 41), Side.Red, RulesOfEngagement.HoldFire, HiddenRifle, experience: Experience.SpecialForces)
            .Build()
            .Order("red", new Watch(new Vec2(0, 1)));
        sim.RunUntilKnows("red", "rj", OneMinute);

        sim.Order("red", new FireAt("rj")).RunUntil(s => s.Events.OfType<ContactReported>().Any(e => e.AgentId == "rj"), OneMinute, "rj reports contact");

        Assert.True(sim.Simulation.KnowsOf("rj", "red"), "rj should know of the enemy it reported");
        sim.RunUntil(s => s.Events.OfType<SquadHalted>().Any(), TimeSpan.FromSeconds(5), "the squad halts");
    }

    [Fact]
    public void The_leaders_movement_order_survives_its_drill_and_resumes_after_the_halt()
    {
        // The squad walks west in file from rj at (80, 61), its row standing east of rj on the file's path, and forms up on
        // the way; red, 20 m south of the route at x = 55, holds fire until it is ordered to fire at rj, once rj is past
        // x = 65 and the squad has formed up.
        var corridor = new Corridor([new Vec2(80, 61), new Vec2(5, 61)], 20, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);
        var order = new MoveThroughCorridor(corridor);
        var sim = Scenario.ForSquadSpotting()
            .WithCombatRules(BlindBlue)
            .WithTerrain(TestMap.Create(widthMeters: 120, heightMeters: 100).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(80, 61), rules: RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(55, 41), Side.Red, RulesOfEngagement.HoldFire, HiddenRifle, experience: Experience.SpecialForces)
            .Build()
            .Order("red", new Watch(new Vec2(0, 1)))
            .Order("rj", order);
        sim.RunUntil(s => s.Unit("rj").Position.X < 65 && s.Unit("rj").Activity == Twin.Sim.Units.UnitActivity.Moving,
            TimeSpan.FromMinutes(3), "rj past x = 65, on the move");
        sim.RunUntilKnows("red", "rj", OneMinute);
        sim.Order("red", new FireAt("rj"))
            .RunUntil(s => s.Events.OfType<ContactReported>().Any(e => e.AgentId == "rj"), OneMinute, "rj reports contact")
            .RunUntil(s => s.Squad("r1").IsHalted, TimeSpan.FromSeconds(5), "the squad halts");
        sim.Order("red", new CeaseFire()).RunFor(TimeSpan.FromSeconds(10));
        var halted = sim.Unit("rj").Position.Xy;

        sim.Order("rj", new ResumeMovement()).RunFor(TimeSpan.FromSeconds(20));

        Assert.Same(order, sim.Unit("rj").CurrentOrder);
        Assert.True(sim.Unit("rj").Position.X < halted.X - 5, "rj should be on its way again");
    }
}
