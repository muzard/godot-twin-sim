using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Scenarios;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Sim.Tests.Scenarios;

/// <summary>
/// The scenario file's <c>squad</c>, <c>enemies</c> and <c>world</c> (backlog 0004, S2d). A scenario has either one agent
/// or a squad; a squad's members are listed one by one and start in their slots of the starting formation along the
/// corridor's first segment; enemies are fighters placed with a side, posture, rules, weapon, experience and a point to
/// watch.
/// </summary>
public class ScenarioSquadTests
{
    // ETRS points; local = ETRS − (385000, 6672000).
    private const string Common = """
          "schemaVersion": 1,
          "corridor": { "centerline": [[385080.0, 6672101.0], [385420.0, 6672101.0]], "width": 80, "softMargin": 25, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 20, "permitRadius": 10 }
        """;

    private const string ReferenceSquad = """
          "squad": {
            "id": "r1",
            "start": [385080.0, 6672101.0],
            "formation": "wedge",
            "teams": ["command", "point", "support"],
            "members": [
              { "id": "rj", "role": "squadLeader",     "team": "command", "experience": "regular" },
              { "id": "j1", "role": "rifleman",        "team": "command", "experience": "conscript" },
              { "id": "j2", "role": "rifleman",        "team": "command", "experience": "regular" },
              { "id": "pj", "role": "teamLeader",      "team": "point",   "experience": "conscript" },
              { "id": "k1", "role": "rifleman",        "team": "point",   "experience": "conscript" },
              { "id": "k2", "role": "rifleman",        "team": "point",   "experience": "regular" },
              { "id": "vj", "role": "secondInCommand", "team": "support", "experience": "regular" },
              { "id": "kk", "role": "machineGunner",   "team": "support", "experience": "conscript", "weapon": "machineGun" },
              { "id": "t1", "role": "rifleman",        "team": "support", "experience": "conscript" }
            ]
          }
        """;

    private static string Scenario(params string[] sections) => "{\n" + string.Join(",\n", [Common, .. sections]) + "\n}";

    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 East = new(1, 0);

    [Fact]
    public void A_squad_section_defines_the_squad_and_each_members_fighter()
    {
        var scenario = TestScenarioFolder.Load(Scenario(ReferenceSquad));

        Assert.Equal("rj", scenario.AgentId); // the agent the corridor order goes to: the squad leader
        var squad = Assert.IsType<SquadDefinition>(scenario.Squad);
        Assert.Equal("r1", squad.Id);
        Assert.Equal(SquadFormation.Wedge, squad.Formation);
        Assert.Equal(SpecSquad.Reference().Definition.Members, squad.Members);
        Assert.Equal(SpecSquad.Reference().Definition.Teams, squad.Teams);
        Assert.Equal(Start, scenario.SquadStart);
        foreach (var member in SpecSquad.Reference().Members)
        {
            var fighter = scenario.SquadCombatants[member.UnitId];
            Assert.Equal((Side.Blue, member.Experience, RulesOfEngagement.ReturnFire), (fighter.Side, fighter.Experience, fighter.Rules));
            Assert.Equal(member.UnitId == "kk" ? WeaponProfile.MachineGun : WeaponProfile.Rifle, fighter.Weapon);
        }
    }

    [Fact]
    public void A_members_experience_weapon_and_rules_default_to_regular_rifle_and_the_squads_rules_and_the_formation_to_file()
    {
        var squad = """
              "squad": {
                "id": "s", "start": [385080.0, 6672101.0], "teams": ["a"], "rules": "freeFire",
                "members": [ { "id": "lead", "role": "squadLeader", "team": "a" }, { "id": "man", "role": "rifleman", "team": "a" } ]
              }
            """;

        var scenario = TestScenarioFolder.Load(Scenario(squad));

        Assert.Equal(SquadFormation.File, scenario.Squad!.Formation);
        var fighter = scenario.SquadCombatants["man"];
        Assert.Equal((Experience.Regular, RulesOfEngagement.FreeFire), (fighter.Experience, fighter.Rules));
        Assert.Equal(WeaponProfile.Rifle, fighter.Weapon);
    }

    [Fact]
    public void Members_start_in_their_slots_of_the_starting_formation_along_the_corridor()
    {
        var simulation = TestScenarioFolder.Load(Scenario(ReferenceSquad)).CreateSimulation(LeaderAnswer.Grant);

        Assert.Equal(SquadFormation.Wedge, simulation.Squads.Single().Formation);
        Assert.Equal("rj", simulation.Squads.Single().LeaderId);
        foreach (var slot in FormationSlots.For(SquadFormation.Wedge, SpecSquad.Reference().Definition, SquadDoctrine.Default.IntervalMeters))
        {
            var expected = SquadMovementAssertions.OnStraightRoute(Start, East, slot);
            var at = simulation.GetUnit(slot.UnitId).Position.Xy;
            Assert.True(at.DistanceTo(expected) < 0.01, $"{slot.UnitId} starts at {at}, expected its slot {expected}");
        }
    }

    [Fact]
    public void The_squad_leader_is_given_the_corridor_order()
    {
        var scenario = TestScenarioFolder.Load(Scenario(ReferenceSquad));
        var simulation = scenario.CreateSimulation(LeaderAnswer.Grant);

        simulation.Step();

        var order = Assert.IsType<MoveThroughCorridor>(simulation.GetUnit("rj").CurrentOrder);
        Assert.Equal(scenario.Corridor, order.Corridor);
    }

    private const string Enemies = """
          "enemies": [
            { "id": "red-mg", "position": [385300.0, 6672150.0], "side": "red", "posture": "prone", "rules": "holdFire",
              "weapon": "machineGun", "experience": "conscript", "watchToward": [385300.0, 6672050.0] },
            { "id": "red-1", "position": [385310.0, 6672150.0] }
          ]
        """;

    [Fact]
    public void The_enemies_section_defines_fighters_with_their_side_posture_rules_weapon_experience_and_watch()
    {
        var enemies = TestScenarioFolder.Load(Scenario(ReferenceSquad, Enemies)).Enemies;

        Assert.Equal(["red-mg", "red-1"], enemies.Select(e => e.Id));
        var mg = enemies[0];
        Assert.Equal((new Vec2(300, 150), Posture.Prone, (Vec2?)new Vec2(300, 50)), (mg.Position, mg.Posture, mg.WatchToward));
        Assert.Equal(new Combatant(Side.Red, RulesOfEngagement.HoldFire, WeaponProfile.MachineGun, Experience: Experience.Conscript) with { UnitId = mg.Combatant.UnitId },
            mg.Combatant);
        var rifleman = enemies[1];
        Assert.Equal((Posture.Standing, (Vec2?)null), (rifleman.Posture, rifleman.WatchToward));
        Assert.Equal((Side.Red, RulesOfEngagement.FreeFire, Experience.Regular), (rifleman.Combatant.Side, rifleman.Combatant.Rules, rifleman.Combatant.Experience));
        Assert.Equal(WeaponProfile.Rifle, rifleman.Combatant.Weapon);
    }

    [Fact]
    public void Enemies_are_placed_take_their_posture_and_watch_their_point()
    {
        var simulation = TestScenarioFolder.Load(Scenario(ReferenceSquad, Enemies)).CreateSimulation(LeaderAnswer.Grant);

        // 5 s at 2 Hz: the posture change (1 s) is over, and red-mg has not yet spotted the squad at the edge of its view
        // (it would then watch the squad instead, backlog 0004 S5a1).
        for (var i = 0; i < 10; i++)
        {
            simulation.Step();
        }

        var mg = simulation.GetUnit("red-mg");
        Assert.Equal(new Vec2(300, 150), mg.Position.Xy);
        Assert.Equal(Posture.Prone, mg.Posture);
        Assert.True(mg.WatchDirection.DistanceTo(new Vec2(0, -1)) < 1e-9, $"red-mg watches {mg.WatchDirection}, expected south");
        Assert.False(mg.CurrentOrder is MoveTo or MoveThroughCorridor, "enemies get no movement order");
        Assert.Equal(Side.Red, simulation.GetUnit("red-1").Combatant!.Side);
    }

    [Fact]
    public void The_world_can_be_shared_from_another_directory()
    {
        var scenario = TestScenarioFolder.Load(Scenario(ReferenceSquad, "\"world\": \"../shared/world\""), worldPath: "../shared/world");

        Assert.Equal("r1", scenario.Squad!.Id);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("neither")]
    public void A_scenario_needs_exactly_one_of_an_agent_and_a_squad(string which)
    {
        const string agent = "\"agent\": { \"id\": \"alpha\", \"start\": [385080.0, 6672101.0] }";
        var json = which == "both" ? Scenario(agent, ReferenceSquad) : Scenario();

        Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(json));
    }

    [Fact]
    public void An_invalid_squad_fails_to_load_with_a_message_naming_the_problem()
    {
        var twoLeaders = ReferenceSquad.Replace("\"id\": \"j1\", \"role\": \"rifleman\"", "\"id\": \"j1\", \"role\": \"squadLeader\"", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(Scenario(twoLeaders)));

        Assert.Contains("leader", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_weapon_name_fails_to_load()
    {
        var json = Scenario(ReferenceSquad.Replace("\"weapon\": \"machineGun\"", "\"weapon\": \"bazooka\"", StringComparison.Ordinal));

        var error = Assert.Throws<InvalidDataException>(() => TestScenarioFolder.Load(json));

        Assert.Contains("bazooka", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_machine_gun_has_the_values_of_backlog_0003()
    {
        Assert.Equal(SpecSuppression.MachineGun, WeaponProfile.MachineGun);
    }
}
