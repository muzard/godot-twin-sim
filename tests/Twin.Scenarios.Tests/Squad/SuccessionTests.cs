using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Succession (backlog 0004, S1: SQ5). When the squad leader is out of action, the second-in-command takes command
/// after the takeover time (5 s) and keeps leading its own team; until then the squad has no leader and takes no squad
/// orders. The command team gets its next member as its leader. If the second-in-command is out too, the point team's
/// leader takes command. (Passing the squad's movement order and permits to the new leader is tested with squad
/// movement, S2.)
/// </summary>
public class SuccessionTests
{
    private const double Takeover = SpecSquad.TakeoverSeconds;
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Plenty = TimeSpan.FromSeconds(30);

    // The reference squad stands 3 m apart from x = 21 (rj at 21, vj at 39); red stands 5 m north of rj, red-2 5 m
    // north of vj.
    private static ScenarioRun ReferenceSquadWithEnemiesNextToItsLeaders() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(21, 21))
        .WithFighter("red", new Vec2(21, 26), Side.Red, RulesOfEngagement.HoldFire)
        .WithFighter("red-2", new Vec2(39, 26), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    private static ScenarioRun AfterTheSecondInCommandTookCommand()
    {
        var sim = ReferenceSquadWithEnemiesNextToItsLeaders();
        sim.RunUntilOutOfAction("rj", "red", FiveMinutes);
        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", Plenty, "vj in command");
        return sim;
    }

    private static void AtTakeoverTime(double time, double outOfAction) =>
        Assert.InRange(time, outOfAction + Takeover - 1e-9, outOfAction + Takeover + SpecV01.DtSeconds + 1e-9);

    [Fact]
    public void SQ5_when_the_leader_is_out_of_action_the_second_in_command_takes_command_after_the_takeover_time()
    {
        var sim = ReferenceSquadWithEnemiesNextToItsLeaders();

        var outOfAction = sim.RunUntilOutOfAction("rj", "red", FiveMinutes);
        sim.RunFor(TimeSpan.FromSeconds(Takeover + 1));

        var change = Assert.Single(sim.Events.OfType<LeaderChanged>());
        Assert.Equal(("vj", "r1"), (change.AgentId, change.SquadId));
        AtTakeoverTime(change.Time, outOfAction);
        Assert.Equal("vj", sim.Squad("r1").LeaderId);
    }

    [Fact]
    public void SQ5_until_the_takeover_the_squad_has_no_leader_and_takes_no_squad_orders()
    {
        var sim = ReferenceSquadWithEnemiesNextToItsLeaders();

        sim.RunUntilOutOfAction("rj", "red", FiveMinutes);

        Assert.Null(sim.Squad("r1").LeaderId);
        Assert.Throws<OrderRejectedException>(() => sim.Order("vj", new SetSquadPosture(Posture.Prone)));
    }

    [Fact]
    public void SQ5_the_second_in_command_keeps_leading_its_own_team()
    {
        var sim = AfterTheSecondInCommandTookCommand();

        Assert.Equal("vj", sim.Squad("r1").TeamLeaderId(SpecSquad.SupportTeam));
    }

    [Fact]
    public void SQ5_the_command_team_is_taken_over_by_its_next_member()
    {
        var sim = ReferenceSquadWithEnemiesNextToItsLeaders();

        var outOfAction = sim.RunUntilOutOfAction("rj", "red", FiveMinutes);
        sim.RunFor(TimeSpan.FromSeconds(Takeover + 1));

        var takeover = Assert.Single(sim.Events.OfType<TeamLeaderChanged>());
        Assert.Equal(("j1", SpecSquad.CommandTeam), (takeover.AgentId, takeover.Team));
        AtTakeoverTime(takeover.Time, outOfAction);
        Assert.Equal("j1", sim.Squad("r1").TeamLeaderId(SpecSquad.CommandTeam));
    }

    [Fact]
    public void SQ5_the_new_leader_commands_the_squad()
    {
        var sim = AfterTheSecondInCommandTookCommand();

        var command = sim.Command("vj", new SetSquadPosture(Posture.Prone));
        sim.RunFor(Plenty);

        Assert.Equal("r1", command.SquadId);
        sim.ShouldHaveActedOn("k2", command, SpecSquad.ExperiencedReactionSeconds);
        // pj (inexperienced) passes it on to k1, as under the old leader.
        sim.ShouldHaveActedOn("k1", command,
            SpecSquad.InexperiencedReactionSeconds + SpecSquad.RelaySeconds + SpecSquad.InexperiencedReactionSeconds);
    }

    [Fact]
    public void SQ5_the_new_leaders_own_team_acts_directly_on_its_command()
    {
        var sim = AfterTheSecondInCommandTookCommand();

        var command = sim.Command("vj", new SetSquadPosture(Posture.Prone));
        sim.RunFor(Plenty);

        // vj leads the support team, so its inexperienced members need no relay, like the command team under rj.
        sim.ShouldHaveActedOn("kk", command, SpecSquad.InexperiencedReactionSeconds);
        sim.ShouldHaveActedOn("t1", command, SpecSquad.InexperiencedReactionSeconds);
    }

    [Fact]
    public void SQ5_orders_to_the_old_leader_are_rejected_after_the_takeover()
    {
        var sim = AfterTheSecondInCommandTookCommand();

        Assert.Throws<OrderRejectedException>(() => sim.Order("rj", new SetSquadPosture(Posture.Prone)));
    }

    [Fact]
    public void SQ5_when_the_second_in_command_is_out_too_the_point_team_leader_takes_command()
    {
        var sim = AfterTheSecondInCommandTookCommand();

        var secondOut = sim.RunUntilOutOfAction("vj", "red-2", FiveMinutes);
        sim.RunFor(TimeSpan.FromSeconds(Takeover + 1));

        var change = sim.Events.OfType<LeaderChanged>().Last();
        Assert.Equal("pj", change.AgentId);
        AtTakeoverTime(change.Time, secondOut);
        Assert.Equal("pj", sim.Squad("r1").LeaderId);
    }
}
