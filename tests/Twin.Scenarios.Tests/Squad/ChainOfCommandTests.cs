using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The chain of command (backlog 0003, S1: SQ3–SQ4). The squad leader commands at once; a member acts on the command
/// after a delay that depends on its experience and on its team leader:
/// <list type="bullet">
/// <item>an experienced member acts on the leader's command directly, after its own reaction time (1 s);</item>
/// <item>an inexperienced member acts only when its team leader passes the command on: the team leader's own reaction
/// time, then the relay time (2 s), then the member's reaction time (3 s);</item>
/// <item>when the commanding leader is the member's own team leader (the command team), no relay is needed.</item>
/// </list>
/// A team whose leader is out of action is taken over by its next member after the takeover time (5 s). A command given
/// while the team has no leader lapses for its inexperienced members: the leader must give it again.
/// The first squad command is <see cref="SetSquadPosture"/> ("Maahan!"): every member takes the posture when it acts.
/// </summary>
public class ChainOfCommandTests
{
    private const double Experienced = SpecSquad.ExperiencedReactionSeconds;
    private const double Inexperienced = SpecSquad.InexperiencedReactionSeconds;
    private const double Relay = SpecSquad.RelaySeconds;
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly Vec2 Start = new(21, 21);

    private static ScenarioRun ReferenceSquad() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(SpecSquad.Reference(), Start)
        .Build();

    // A point team of four: pj (inexperienced leader), k1 (inexperienced, next in line), k2 (experienced),
    // k3 (inexperienced). Members stand 3 m apart from x = 21, so pj is at x = 27; red stands 5 m north of it.
    private static ScenarioRun PointTeamWithAnEnemyNextToItsLeader() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(new SquadDefinition("r1", [SpecSquad.CommandTeam, SpecSquad.PointTeam],
        [
            SpecSquad.Member("rj", SquadRole.SquadLeader, SpecSquad.CommandTeam, Experience.Experienced),
            SpecSquad.Member("j1", SquadRole.Rifleman, SpecSquad.CommandTeam, Experience.Experienced),
            SpecSquad.Member("pj", SquadRole.TeamLeader, SpecSquad.PointTeam, Experience.Inexperienced),
            SpecSquad.Member("k1", SquadRole.Rifleman, SpecSquad.PointTeam, Experience.Inexperienced),
            SpecSquad.Member("k2", SquadRole.Rifleman, SpecSquad.PointTeam, Experience.Experienced),
            SpecSquad.Member("k3", SquadRole.Rifleman, SpecSquad.PointTeam, Experience.Inexperienced),
        ]), Start)
        .WithFighter("red", new Vec2(27, 26), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    private static SetSquadPosture GetDown => new(Posture.Prone);

    [Fact]
    public void SQ3_the_leader_commands_on_the_tick_the_order_takes_effect()
    {
        var sim = ReferenceSquad();

        sim.Order("rj", GetDown).Step();

        var issued = Assert.Single(sim.Events.OfType<CommandIssued>());
        Assert.Equal("rj", issued.AgentId);
        Assert.Equal("r1", issued.SquadId);
        Assert.Equal(sim.TimeSeconds, issued.Time, precision: 9);
    }

    [Fact]
    public void SQ3_the_leader_takes_the_commanded_posture_itself_without_delay()
    {
        var sim = ReferenceSquad();

        sim.Command("rj", GetDown);
        sim.RunFor(TimeSpan.FromSeconds(SpecShooting.Rules.PostureChangeSeconds)).Step();

        sim.Unit("rj").Should().BeIn(Posture.Prone);
    }

    [Fact]
    public void SQ3_an_experienced_member_acts_after_its_own_reaction_time_whatever_its_team_leader()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        sim.ShouldHaveActedOn("j2", command, Experienced); // command team
        sim.ShouldHaveActedOn("k2", command, Experienced); // point team, whose leader is inexperienced
        sim.ShouldHaveActedOn("vj", command, Experienced); // support team's own leader
    }

    [Fact]
    public void SQ3_a_team_leader_acts_after_its_own_reaction_time()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        sim.ShouldHaveActedOn("pj", command, Inexperienced);
        sim.ShouldHaveActedOn("vj", command, Experienced);
    }

    [Fact]
    public void SQ3_an_inexperienced_member_acts_when_its_team_leader_passes_the_command_on()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        // pj is inexperienced: 3 s to act, 2 s to pass it on, then k1's own 3 s.
        sim.ShouldHaveActedOn("k1", command, Inexperienced + Relay + Inexperienced);
    }

    [Fact]
    public void SQ3_an_experienced_team_leader_gets_the_command_to_its_team_sooner()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        // vj is experienced: 1 s to act, 2 s to pass it on, then the member's own 3 s.
        sim.ShouldHaveActedOn("kk", command, Experienced + Relay + Inexperienced);
        sim.ShouldHaveActedOn("t1", command, Experienced + Relay + Inexperienced);
    }

    [Fact]
    public void SQ3_inexperienced_members_of_the_command_team_act_directly_on_the_leaders_command()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        sim.ShouldHaveActedOn("j1", command, Inexperienced);
    }

    [Fact]
    public void SQ3_no_member_acts_before_its_delay()
    {
        var sim = ReferenceSquad();

        var command = sim.Command("rj", GetDown);

        // One tick before 1 s: no one has acted yet.
        sim.RunFor(TimeSpan.FromSeconds(Experienced - SpecV01.DtSeconds));
        foreach (var member in SpecSquad.Reference().Members.Where(m => m.UnitId != "rj"))
        {
            sim.ShouldNotHaveActedOn(member.UnitId, command);
        }

        // One tick before 3 s: still no inexperienced member.
        sim.RunFor(TimeSpan.FromSeconds(Inexperienced - Experienced));
        foreach (var id in new[] { "j1", "pj", "k1", "kk", "t1" })
        {
            sim.ShouldNotHaveActedOn(id, command);
        }
    }

    [Fact]
    public void SQ3_every_member_takes_the_commanded_posture_once_it_has_acted()
    {
        var sim = ReferenceSquad();

        sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        foreach (var member in SpecSquad.Reference().Members)
        {
            sim.Unit(member.UnitId).Should().BeIn(Posture.Prone);
        }
    }

    [Fact]
    public void SQ3_a_member_is_still_standing_until_it_acts()
    {
        var sim = ReferenceSquad();

        sim.Command("rj", GetDown);
        sim.RunFor(TimeSpan.FromSeconds(Inexperienced + Relay)); // k1 acts only at 8 s

        sim.Unit("k1").Should().BeIn(Posture.Standing);
    }

    [Fact]
    public void SQ4_the_next_member_of_a_team_takes_it_over_after_the_takeover_time()
    {
        var sim = PointTeamWithAnEnemyNextToItsLeader();

        var outOfAction = sim.RunUntilOutOfAction("pj", "red", FiveMinutes);
        sim.RunFor(TimeSpan.FromSeconds(SpecSquad.TakeoverSeconds + 1));

        var takeover = Assert.Single(sim.Events.OfType<TeamLeaderChanged>());
        Assert.Equal(("k1", "r1", SpecSquad.PointTeam), (takeover.AgentId, takeover.SquadId, takeover.Team));
        Assert.InRange(takeover.Time, outOfAction + SpecSquad.TakeoverSeconds - 1e-9,
            outOfAction + SpecSquad.TakeoverSeconds + SpecV01.DtSeconds + 1e-9);
        Assert.Equal("k1", sim.Squad("r1").TeamLeaderId(SpecSquad.PointTeam));
    }

    [Fact]
    public void SQ4_a_team_has_no_leader_until_the_takeover()
    {
        var sim = PointTeamWithAnEnemyNextToItsLeader();

        sim.RunUntilOutOfAction("pj", "red", FiveMinutes);

        Assert.Null(sim.Squad("r1").TeamLeaderId(SpecSquad.PointTeam));
    }

    [Fact]
    public void SQ4_an_inexperienced_member_does_not_act_on_a_command_given_while_its_team_has_no_leader()
    {
        var sim = PointTeamWithAnEnemyNextToItsLeader();
        var outOfAction = sim.RunUntilOutOfAction("pj", "red", FiveMinutes);

        var command = sim.Command("rj", GetDown);
        Assert.True(command.Time < outOfAction + SpecSquad.TakeoverSeconds, "the command must come before the takeover");
        sim.RunFor(Plenty);

        sim.ShouldNotHaveActedOn("k3", command);
        sim.ShouldNotHaveActedOn("k1", command); // the new team leader did not lead the team when the command came
        sim.Unit("k3").Should().BeIn(Posture.Standing);
    }

    [Fact]
    public void SQ4_an_experienced_member_acts_on_a_command_given_while_its_team_has_no_leader()
    {
        var sim = PointTeamWithAnEnemyNextToItsLeader();
        sim.RunUntilOutOfAction("pj", "red", FiveMinutes);

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        sim.ShouldHaveActedOn("k2", command, Experienced);
    }

    [Fact]
    public void SQ4_after_the_takeover_a_repeated_command_reaches_the_team_through_its_new_leader()
    {
        var sim = PointTeamWithAnEnemyNextToItsLeader();
        sim.RunUntilOutOfAction("pj", "red", FiveMinutes);
        sim.RunUntil(s => s.Events.OfType<TeamLeaderChanged>().Any(), Plenty, "point team taken over");

        var command = sim.Command("rj", GetDown);
        sim.RunFor(Plenty);

        sim.ShouldHaveActedOn("k1", command, Inexperienced);                         // now the team leader
        sim.ShouldHaveActedOn("k3", command, Inexperienced + Relay + Inexperienced); // through k1
    }
}
