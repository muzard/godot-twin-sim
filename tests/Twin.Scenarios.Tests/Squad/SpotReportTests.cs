using Twin.Sim.Combat;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The spot report (backlog 0004 S5a1: SP8, SP9). A member that spots an enemy stops and crouches at once and reports
/// it after its command reaction delay (1 s experienced, 3 s inexperienced). The report is spoken: squad mates within
/// 30 m learn of the enemy and its position at once. If the leader did not hear it, of the members who know of the enemy
/// the one nearest the leader repeats it after the relay delay (2 s), again and again until the leader knows (review
/// decision, 2026-11-18). A member reports an enemy it spotted once; a relay repeats it.
/// </summary>
public class SpotReportTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact]
    public void SP8_the_spotter_crouches_as_soon_as_it_spots_the_enemy()
    {
        var sim = SpottingLayouts.FlankAtRest();
        var spotted = sim.RedStandsUp().RunUntilSpotted("vj", "red", OneMinute);

        sim.RunUntil(s => s.TimeSeconds >= spotted.Time + SpecShooting.Rules.PostureChangeSeconds + s.Simulation.Config.TickSeconds - 1e-9,
            OneMinute, "a posture change later");

        sim.Unit("vj").Should().BeIn(Posture.Crouching);
    }

    [Fact]
    public void SP8_an_experienced_spotter_reports_after_its_reaction_delay()
    {
        var sim = SpottingLayouts.FlankAtRest();
        var spotted = sim.RedStandsUp().RunUntilSpotted("vj", "red", OneMinute);

        sim.RunUntil(s => s.ReportsOf("red").Count > 0, OneMinute, "a report of red");

        var report = sim.ReportsOf("red")[0];
        Assert.Equal("vj", report.AgentId);
        Assert.Equal(ReportMethod.Voice, report.Method);
        Assert.Equal(sim.Unit("red").Position.Xy, report.EnemyPosition);
        sim.ShouldBeAt(report, SpecSquad.ExperiencedReactionSeconds, spotted.Time, "vj's report");
    }

    [Fact]
    public void SP8_an_inexperienced_spotter_reports_after_its_longer_reaction_delay()
    {
        var sim = SpottingLayouts.RearInFile();
        var spotted = sim.RedStandsUp().RunUntilAnyoneSpots("red", OneMinute);
        Assert.Equal("t1", spotted.AgentId);
        Assert.Equal(SpecSquad.Inexperienced, sim.Unit("t1").Combatant!.Experience);

        sim.RunUntil(s => s.ReportsOf("red").Count > 0, OneMinute, "a report of red");

        sim.ShouldBeAt(sim.ReportsOf("red")[0], SpecSquad.InexperiencedReactionSeconds, spotted.Time, "t1's report");
    }

    [Fact]
    public void SP8_squad_mates_within_voice_range_learn_of_the_enemy_with_the_report()
    {
        var sim = SpottingLayouts.FlankAtRest();
        sim.RedStandsUp().RunUntil(s => s.ReportsOf("red").Count > 0, OneMinute, "a report of red");
        var report = sim.ReportsOf("red")[0];

        foreach (var id in new[] { "rj", "j1", "k2", "t1" })
        {
            var received = sim.ReceivedFrom(id, "vj");
            Assert.True(received is not null, $"{id} is within {SpecSpotting.VoiceRangeMeters} m of vj and should hear its report");
            Assert.Equal(report.Time, received.Time);
            Assert.Equal("red", received.EnemyId);
            Assert.Equal(report.EnemyPosition, received.EnemyPosition);
            Assert.True(sim.Simulation.KnowsOf(id, "red"), $"{id} should know of red");
        }
    }

    [Fact]
    public void SP8_squad_mates_out_of_voice_range_do_not_hear_the_report()
    {
        var sim = SpottingLayouts.FlankAtRest();

        sim.RedStandsUp().RunFor(TimeSpan.FromMinutes(2));

        foreach (var id in new[] { "pj", "k1", "j2", "kk" })
        {
            Assert.Null(sim.ReceivedFrom(id, "vj"));
            Assert.False(sim.Simulation.KnowsOf(id, "red"), $"{id} is more than {SpecSpotting.VoiceRangeMeters} m from vj and should not know of red");
        }
    }

    [Fact]
    public void SP8_the_leader_hearing_the_report_needs_no_relay_and_no_one_reports_the_enemy_twice()
    {
        var sim = SpottingLayouts.FlankAtRest();

        sim.RedStandsUp().RunFor(TimeSpan.FromMinutes(2));

        var report = Assert.Single(sim.ReportsOf("red"));
        Assert.Equal("vj", report.AgentId);
    }

    [Fact]
    public void SP8_squad_mates_who_hear_the_report_stop_and_crouch()
    {
        var sim = SpottingLayouts.FlankAtRest();
        sim.RedStandsUp().RunUntil(s => s.ReportsOf("red").Count > 0, OneMinute, "a report of red");
        var reportedAt = sim.TimeSeconds;

        sim.RunUntil(s => s.TimeSeconds >= reportedAt + SpecShooting.Rules.PostureChangeSeconds + s.Simulation.Config.TickSeconds - 1e-9,
            OneMinute, "a posture change later");

        foreach (var id in new[] { "j1", "k2", "t1" })
        {
            sim.Unit(id).Should().BeIn(Posture.Crouching).HaveGroundSpeed(0);
        }
    }

    [Fact]
    public void SP9_a_report_that_does_not_reach_the_leader_is_repeated_every_relay_time()
    {
        // Voice range 11 m, below the 12 m interval: nobody hears vj, who spotted red.
        var sim = SpottingLayouts.RightOfFile(voiceRangeMeters: 11);
        sim.RedStandsUp().RunUntil(s => s.ReportsOf("red").Count >= 3, OneMinute, "three reports of red");

        var reports = sim.ReportsOf("red");
        Assert.All(reports.Take(3), r => Assert.Equal("vj", r.AgentId));
        sim.ShouldBeAt(reports[1], SpecSquad.RelaySeconds, reports[0].Time, "vj's first repeat");
        sim.ShouldBeAt(reports[2], SpecSquad.RelaySeconds, reports[1].Time, "vj's second repeat");
        Assert.False(sim.Simulation.KnowsOf("rj", "red"));
    }

    [Fact]
    public void SP9_a_leader_out_of_voice_range_does_not_hear_the_spotters_report()
    {
        var sim = SpottingLayouts.RearInFile();

        sim.RedStandsUp().RunUntil(s => s.ReportsOf("red").Count > 0, OneMinute, "a report of red");

        Assert.Equal("t1", sim.ReportsOf("red")[0].AgentId);
        Assert.Null(sim.ReceivedFrom("rj", "t1"));
        Assert.False(sim.Simulation.KnowsOf("rj", "red"), "rj is 60 m from t1");
    }

    [Fact]
    public void SP9_the_report_is_passed_on_toward_the_leader_by_the_hearer_nearest_to_it()
    {
        // t1 (x 340) is heard by kk (352) and vj (364); vj, the nearer to rj (400), repeats it 2 s later and is heard by
        // j2 (376) and j1 (388); j1 repeats it 2 s after that, and rj hears it.
        var sim = SpottingLayouts.RearInFile();
        sim.RedStandsUp().RunUntilKnows("rj", "red", OneMinute);

        var reports = sim.ReportsOf("red");
        Assert.Equal(["t1", "vj", "j1"], reports.Select(r => r.AgentId).ToArray());
        sim.ShouldBeAt(reports[1], SpecSquad.RelaySeconds, reports[0].Time, "vj's relay");
        sim.ShouldBeAt(reports[2], SpecSquad.RelaySeconds, reports[1].Time, "j1's relay");
        var heard = sim.ReceivedFrom("rj", "j1");
        Assert.NotNull(heard);
        Assert.Equal(reports[2].Time, heard.Time);
    }
}
