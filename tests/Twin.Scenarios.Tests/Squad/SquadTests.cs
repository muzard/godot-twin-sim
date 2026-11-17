using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Forming a squad (backlog 0004, S1: SQ1–SQ2). A squad is formed from agents already on the map: forming it gives each
/// member a role and a team, and gives the squad one leader and every team one leader. A member's experience is its own
/// as a fighter (backlog 0003). It does not move anyone (placing members in formation slots comes with squad movement,
/// S2). Invalid squads are rejected when they are formed; squad orders go only to the squad's leader.
/// </summary>
public class SquadTests
{
    private static readonly Vec2 Start = new(21, 21);

    private static ScenarioRun ReferenceSquad() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(SpecSquad.Reference(), Start)
        .WithFighter("red", new Vec2(21, 41), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    // Fighters a–f on blue, "red" on red and "civilian" with no weapon, none of them in a squad yet.
    private static ScenarioRun AgentsWithoutSquad()
    {
        var scenario = Scenario.ForSquad().WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build());
        var x = 21.0;
        foreach (var id in new[] { "a", "b", "c", "d", "e", "f" })
        {
            scenario.WithFighter(id, new Vec2(x, 21), Side.Blue, RulesOfEngagement.HoldFire);
            x += Scenario.SquadSpacingMeters;
        }

        return scenario
            .WithFighter("red", new Vec2(21, 41), Side.Red, RulesOfEngagement.HoldFire)
            .WithUnit("civilian", new Vec2(21, 31), SpecV01.Infantry)
            .Build();
    }

    private static SquadMember Leader(string id, string team = "one") => new(id, SquadRole.SquadLeader, team);

    private static SquadMember TeamLeader(string id, string team) => new(id, SquadRole.TeamLeader, team);

    private static SquadMember Rifleman(string id, string team = "one") => new(id, SquadRole.Rifleman, team);

    private static SquadDefinition Definition(IReadOnlyList<string> teams, params SquadMember[] members) => new("s", teams, members);

    private static void FormingIsRejected(SquadDefinition squad)
    {
        var sim = AgentsWithoutSquad();
        Assert.Throws<ArgumentException>(() => sim.Simulation.AddSquad(squad));
    }

    [Fact]
    public void SQ1_a_squad_is_formed_from_placed_agents_with_their_roles_teams_and_experience()
    {
        var sim = ReferenceSquad();

        var squad = sim.Squad("r1");

        Assert.Equal("rj", squad.LeaderId);
        Assert.Equal(SpecSquad.ReferenceTeams, squad.Teams);
        foreach (var expected in SpecSquad.Reference().Members)
        {
            Assert.Equal(expected.ToMember(), squad.Member(expected.UnitId));
            Assert.Equal(expected.Experience, sim.Unit(expected.UnitId).Combatant?.Experience);
            Assert.Same(squad, sim.Simulation.SquadOf(expected.UnitId));
        }
    }

    [Fact]
    public void SQ1_each_team_is_led_by_its_leader_and_the_squad_leader_leads_the_command_team()
    {
        var squad = ReferenceSquad().Squad("r1");

        Assert.Equal("rj", squad.TeamLeaderId(SpecSquad.CommandTeam));
        Assert.Equal("pj", squad.TeamLeaderId(SpecSquad.PointTeam));
        Assert.Equal("vj", squad.TeamLeaderId(SpecSquad.SupportTeam));
    }

    [Fact]
    public void SQ1_forming_a_squad_does_not_move_its_members()
    {
        var sim = ReferenceSquad();

        var members = SpecSquad.Reference().Members;
        for (var i = 0; i < members.Count; i++)
        {
            sim.Unit(members[i].UnitId).Should().BeAt(Start + new Vec2(i * Scenario.SquadSpacingMeters, 0)).BeIdle();
        }
    }

    [Fact]
    public void SQ1_an_agent_outside_any_squad_has_no_squad()
    {
        var sim = ReferenceSquad();

        Assert.Null(sim.Simulation.SquadOf("red"));
    }

    [Fact]
    public void SQ1_forming_a_squad_is_logged_with_its_leader()
    {
        var sim = ReferenceSquad();

        var formed = Assert.Single(sim.Events.OfType<SquadFormed>());
        Assert.Equal("r1", formed.SquadId);
        Assert.Equal("rj", formed.AgentId);
    }

    [Fact]
    public void SQ2_a_squad_without_a_leader_is_rejected() =>
        FormingIsRejected(Definition(["one"], TeamLeader("a", "one"), Rifleman("b")));

    [Fact]
    public void SQ2_a_squad_with_two_leaders_is_rejected() =>
        FormingIsRejected(Definition(["one", "two"], Leader("a"), Leader("b", "two")));

    [Fact]
    public void SQ2_a_squad_with_two_seconds_in_command_is_rejected() =>
        FormingIsRejected(Definition(["one", "two", "three"],
            Leader("a"),
            new SquadMember("b", SquadRole.SecondInCommand, "two"),
            new SquadMember("c", SquadRole.SecondInCommand, "three")));

    [Fact]
    public void SQ2_a_team_without_a_leader_is_rejected() =>
        FormingIsRejected(Definition(["one", "two"], Leader("a"), Rifleman("b", "two")));

    [Fact]
    public void SQ2_a_listed_team_with_no_members_is_rejected() =>
        FormingIsRejected(Definition(["one", "two"], Leader("a"), Rifleman("b")));

    [Fact]
    public void SQ2_a_team_with_two_leaders_is_rejected() =>
        FormingIsRejected(Definition(["one", "two"],
            Leader("a"),
            TeamLeader("b", "two"),
            new SquadMember("c", SquadRole.SecondInCommand, "two")));

    [Fact]
    public void SQ2_a_member_of_an_unlisted_team_is_rejected() =>
        FormingIsRejected(Definition(["one"], Leader("a"), Rifleman("b", "nowhere")));

    [Fact]
    public void SQ2_a_member_who_is_not_on_the_map_is_rejected() =>
        FormingIsRejected(Definition(["one"], Leader("a"), Rifleman("ghost")));

    [Fact]
    public void SQ2_the_same_agent_twice_in_a_squad_is_rejected() =>
        FormingIsRejected(Definition(["one"], Leader("a"), Rifleman("b"), Rifleman("b")));

    [Fact]
    public void SQ2_a_member_who_is_not_a_fighter_is_rejected() =>
        FormingIsRejected(Definition(["one"], Leader("a"), Rifleman("civilian")));

    [Fact]
    public void SQ2_members_on_different_sides_are_rejected() =>
        FormingIsRejected(Definition(["one"], Leader("a"), Rifleman("red")));

    [Fact]
    public void SQ2_an_agent_already_in_a_squad_cannot_join_another()
    {
        var sim = AgentsWithoutSquad();
        sim.Simulation.AddSquad(new SquadDefinition("first", ["one"], [Leader("a"), Rifleman("b")]));

        Assert.Throws<ArgumentException>(() =>
            sim.Simulation.AddSquad(new SquadDefinition("second", ["one"], [Leader("c"), Rifleman("b")])));
    }

    [Fact]
    public void SQ2_a_second_squad_with_the_same_id_is_rejected()
    {
        var sim = AgentsWithoutSquad();
        sim.Simulation.AddSquad(new SquadDefinition("s", ["one"], [Leader("a"), Rifleman("b")]));

        Assert.Throws<ArgumentException>(() =>
            sim.Simulation.AddSquad(new SquadDefinition("s", ["one"], [Leader("c"), Rifleman("d")])));
    }

    [Fact]
    public void SQ2_a_rejected_squad_leaves_no_trace()
    {
        var sim = AgentsWithoutSquad();

        Assert.Throws<ArgumentException>(() => sim.Simulation.AddSquad(Definition(["one"], Leader("a"), Rifleman("ghost"))));

        Assert.Null(sim.Simulation.SquadOf("a"));
        Assert.Empty(sim.Events.OfType<SquadFormed>());
        sim.Simulation.AddSquad(Definition(["one"], Leader("a"), Rifleman("b"))); // "a" is still free to join a valid squad
    }

    [Fact]
    public void SQ2_a_squad_order_to_a_member_who_is_not_the_leader_is_rejected()
    {
        var sim = ReferenceSquad();

        Assert.Throws<OrderRejectedException>(() => sim.Order("k1", new SetSquadPosture(Posture.Prone)));
    }

    [Fact]
    public void SQ2_a_squad_order_to_an_agent_outside_any_squad_is_rejected()
    {
        var sim = ReferenceSquad();

        Assert.Throws<OrderRejectedException>(() => sim.Order("red", new SetSquadPosture(Posture.Prone)));
    }
}
