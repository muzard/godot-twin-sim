using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Fire control (backlog 0004 S5b). Before the leader distributes fire, a member in contact fires at the first enemy it
/// spots. The leader distributes fire on its own, its reaction time after it learns of a new enemy (a squad command,
/// <see cref="DirectSquadFire"/>): the front spans the bearings to the enemies it knows of, widened by 10° on each side;
/// each team gets an equal share left to right, widened by half so neighbours overlap, as a direction sector from each
/// fighter; the machine gunner gets the whole front. A member watches its sector and fires only at spotted enemies in it
/// (and back at its own attacker); the machine gun also fires at known enemies in it that it has not spotted.
/// <see cref="SquadCeaseFire"/> ends the distribution.
/// <para>
/// The reference squad stands in a row facing north from rj at (100, 61) to t1 at (124, 61), its weapons never hitting.
/// Red stands 100 m north, holding fire: red-left at (60, 161), 21.8° left of north from rj, and red-right at
/// (140, 161), 21.8° right. Facing north the support team is on the left, the command team in the middle and the point
/// team on the right; red-left lies only in the support team's share, red-right only in the point team's.
/// </para>
/// </summary>
public class FireControlTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    private static readonly Vec2 Leader = new(100, 61);
    private static readonly Vec2 RedLeft = new(60, 161);
    private static readonly Vec2 RedRight = new(140, 161);
    private static readonly string[] SupportRiflemen = ["vj", "t1"];
    private static readonly string[] CommandTeam = ["rj", "j1", "j2"];
    private static readonly string[] PointTeam = ["pj", "k1", "k2"];

    private static WeaponProfile NeverHits(SquadMemberSpec member) =>
        SpecSuppression.NeverHits(member.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);

    private static ScenarioRun Squad(
        RulesOfEngagement rules = RulesOfEngagement.ReturnFire, bool redRight = true, CombatRules? combat = null,
        Func<SquadMemberSpec, WeaponProfile>? weapons = null, bool redRightHidden = false)
    {
        var map = TestMap.Create(widthMeters: 200, heightMeters: 200);
        if (redRightHidden)
        {
            map = map.WithMound(fromX: 130, fromY: 157, toX: 150, toY: 159, height: 0.6); // hides red-right lying prone
        }

        var scenario = Scenario.ForSquadSpotting()
            .WithTerrain(map.Build())
            .WithSquad(SpecSquad.Reference(), Leader, rules: rules, weapons: weapons ?? NeverHits)
            .WithFighter("red-left", RedLeft, Side.Red, RulesOfEngagement.HoldFire);
        if (combat is not null)
        {
            scenario.WithCombatRules(combat);
        }

        if (redRight)
        {
            scenario.WithFighter("red-right", RedRight, Side.Red, RulesOfEngagement.HoldFire);
        }

        var sim = scenario.Build();
        return redRightHidden ? sim.Order("red-right", new SetPosture(Posture.Prone)) : sim;
    }

    private static bool IsFireCommand(CommandIssued e) =>
        e.AgentId == "rj" && e.Command.StartsWith(SpecSquad.DirectSquadFireCommand, StringComparison.Ordinal);

    private static IReadOnlyList<CommandIssued> FireCommands(ScenarioRun sim) => sim.Events.OfType<CommandIssued>().Where(IsFireCommand).ToList();

    /// <summary>Runs until the leader knows of both red fighters and has distributed fire, then until everyone acted on it.</summary>
    private static CommandIssued Distributed(ScenarioRun sim)
    {
        sim.RunUntil(s => s.Simulation.KnowsOf("rj", "red-left") && s.Simulation.KnowsOf("rj", "red-right"), TwoMinutes, "rj knows of both");
        sim.RunUntil(s => FireCommands(s).Count > 0 && FireCommands(s)[^1].Time >= LearnedBoth(s), TwoMinutes, "fire distributed");
        var command = FireCommands(sim)[^1];
        sim.RunFor(TimeSpan.FromSeconds(20)); // the slowest member acts after 3 + 2 + 3 s, or 3 × that suppressed
        return command;
    }

    private static double LearnedBoth(ScenarioRun sim) =>
        new[] { "red-left", "red-right" }.Max(enemy => sim.Events.First(e => e.AgentId == "rj" && Names(e, enemy)).Time);

    private static bool Names(SimEvent e, string enemy) =>
        e is EnemySpotted spotted && spotted.EnemyId == enemy || e is SpotReceived received && received.EnemyId == enemy;

    private static double Degrees(Vec2 a, Vec2 b) => Math.Acos(Math.Clamp(a.Normalized().Dot(b.Normalized()), -1, 1)) * 180 / Math.PI;

    // The front from rj to both red fighters: centred north, 2 × 21.8° + 2 × 10° wide.
    private static readonly double HalfSpread = Degrees(new Vec2(0, 1), RedRight - Leader);
    private static readonly double FrontWidth = (2 * HalfSpread) + (2 * SpecSquad.FrontMarginDegrees);
    private static readonly double ShareWidth = FrontWidth / 3 * (1 + SpecSquad.TeamShareWidening);

    private static Vec2 TeamCentre(string[] team) =>
        team == SupportRiflemen ? SpecSpotting.Turned(new Vec2(0, 1), -FrontWidth / 3)
        : team == PointTeam ? SpecSpotting.Turned(new Vec2(0, 1), FrontWidth / 3)
        : new Vec2(0, 1);

    [Fact]
    public void Before_the_fire_command_a_member_in_contact_fires_at_the_first_enemy_it_spots()
    {
        var sim = Squad();

        sim.RunUntil(s => s.Events.OfType<ShotFired>().Any(e => !e.AgentId.StartsWith("red", StringComparison.Ordinal)), TwoMinutes, "a member fires");

        var shot = sim.Events.OfType<ShotFired>().First(e => !e.AgentId.StartsWith("red", StringComparison.Ordinal));
        Assert.StartsWith("red", shot.TargetId, StringComparison.Ordinal);
        Assert.NotNull(sim.SpottedBy(shot.AgentId, shot.TargetId));
        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.AgentId.StartsWith("red", StringComparison.Ordinal));
        Assert.DoesNotContain(FireCommands(sim), c => sim.ReceivedBy(shot.AgentId, c) is { } received && received.Time <= shot.Time);
    }

    [Fact]
    public void A_squad_holding_fire_does_not_fire_in_contact()
    {
        var sim = Squad(RulesOfEngagement.HoldFire);

        sim.RunUntil(s => s.Squad("r1").IsHalted, TwoMinutes, "the squad halts on red").RunFor(TimeSpan.FromSeconds(30));

        Assert.Empty(sim.Events.OfType<ShotFired>());
    }

    [Fact]
    public void The_leader_distributes_fire_its_reaction_time_after_it_learns_of_an_enemy()
    {
        var sim = Squad(redRight: false);

        sim.RunUntil(s => FireCommands(s).Count > 0, TwoMinutes, "fire distributed");

        var learned = sim.Events.First(e => e.AgentId == "rj" && Names(e, "red-left")).Time;
        sim.ShouldBeAt(FireCommands(sim)[0], SpecSquad.ExperiencedReactionSeconds, learned, "rj's fire command");
    }

    [Fact]
    public void A_new_enemy_brings_a_new_fire_command()
    {
        var sim = Squad(redRightHidden: true);
        sim.RunUntil(s => FireCommands(s).Count > 0, TwoMinutes, "fire distributed for red-left");

        sim.Order("red-right", new SetPosture(Posture.Standing))
            .RunUntil(s => FireCommands(s).Count > 1, TwoMinutes, "fire distributed again for red-right");

        var learned = sim.Events.First(e => e.AgentId == "rj" && Names(e, "red-right")).Time;
        sim.ShouldBeAt(FireCommands(sim)[1], SpecSquad.ExperiencedReactionSeconds, learned, "rj's second fire command");
    }

    [Fact]
    public void The_front_spans_the_known_enemies_widened_by_the_margin()
    {
        var sim = Squad();

        Distributed(sim);

        var front = Assert.IsType<FireArea>(sim.Squad("r1").Front);
        Assert.True(Degrees(front.Centre, new Vec2(0, 1)) < 0.5, $"the front should be centred north, not on {front.Centre}");
        Assert.Equal(FrontWidth, front.WidthDegrees, precision: 3);
    }

    [Fact]
    public void Each_team_gets_an_overlapping_share_of_the_front_left_to_right()
    {
        var sim = Squad();

        Distributed(sim);

        foreach (var team in new[] { SupportRiflemen, CommandTeam, PointTeam })
        {
            foreach (var id in team)
            {
                var area = Assert.IsType<FireArea>(sim.Unit(id).FireArea);
                Assert.True(Degrees(area.Centre, TeamCentre(team)) < 0.5, $"{id}'s fire area should be centred on {TeamCentre(team)}, not {area.Centre}");
                Assert.Equal(ShareWidth, area.WidthDegrees, precision: 3);
            }
        }

        Assert.True(ShareWidth > FrontWidth / 3, "precondition: neighbouring shares overlap");
    }

    [Fact]
    public void The_machine_gunner_gets_the_whole_front()
    {
        var sim = Squad();

        Distributed(sim);

        var area = Assert.IsType<FireArea>(sim.Unit("kk").FireArea);
        Assert.True(Degrees(area.Centre, new Vec2(0, 1)) < 0.5);
        Assert.Equal(FrontWidth, area.WidthDegrees, precision: 3);
    }

    [Fact]
    public void A_member_watches_the_centre_of_its_fire_area()
    {
        var sim = Squad();

        Distributed(sim);

        foreach (var id in new[] { "t1", "j2", "k1" })
        {
            var area = sim.Unit(id).FireArea!;
            Assert.True(Degrees(sim.Unit(id).WatchDirection, area.Centre) < 1, $"{id} watches {sim.Unit(id).WatchDirection}, not {area.Centre}");
        }
    }

    [Fact]
    public void After_the_distribution_a_member_fires_only_at_enemies_in_its_sector()
    {
        var sim = Squad();
        Distributed(sim);
        var from = sim.TimeSeconds;

        sim.RunFor(TimeSpan.FromSeconds(60));

        var shots = sim.Events.OfType<ShotFired>().Where(e => e.Time > from).ToList();
        Assert.Contains(shots, e => PointTeam.Contains(e.AgentId));
        Assert.All(shots.Where(e => PointTeam.Contains(e.AgentId)), e => Assert.Equal("red-right", e.TargetId));
        Assert.Contains(shots, e => SupportRiflemen.Contains(e.AgentId));
        Assert.All(shots.Where(e => SupportRiflemen.Contains(e.AgentId)), e => Assert.Equal("red-left", e.TargetId));
        Assert.DoesNotContain(shots, e => CommandTeam.Contains(e.AgentId)); // neither red is in the middle share
    }

    [Fact]
    public void The_machine_gun_fires_suppressive_bursts_at_a_known_enemy_it_has_not_spotted()
    {
        // Conscripts cannot spot anyone in the time the test runs (1000 s): kk and t1 know of red-left only from reports.
        var conscriptsBlind = SpecSpotting.Rules with { Observation = SpecSpotting.Observation with { ConscriptSeconds = 1000 } };
        var sim = Squad(redRight: false, combat: conscriptsBlind);
        sim.RunUntil(s => FireCommands(s).Count > 0, TwoMinutes, "fire distributed").RunFor(TimeSpan.FromSeconds(30));

        Assert.Null(sim.SpottedBy("kk", "red-left"));
        Assert.Contains(sim.Events.OfType<ShotFired>(), e => e.AgentId == "kk" && e.TargetId == "red-left" && e.Mode == FireMode.Snap);
        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.AgentId == "t1"); // a rifleman fires only at what it spotted
    }

    [Fact]
    public void A_member_whose_sector_holds_no_enemy_in_action_stops_firing()
    {
        // The point team's rifles hit: red-right goes out of action, and red-left lies outside their sector.
        var sim = Squad(weapons: m => PointTeam.Contains(m.UnitId) ? SpecShooting.Rifle : NeverHits(m));
        Distributed(sim);
        sim.RunUntil(s => s.Unit("red-right").Health == Health.OutOfAction, TimeSpan.FromMinutes(5), "red-right out of action");
        var from = sim.TimeSeconds;

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.Time > from && PointTeam.Contains(e.AgentId));
    }

    [Fact]
    public void A_squad_cease_fire_ends_the_distribution_and_the_fire()
    {
        var sim = Squad();
        Distributed(sim);

        sim.Command("rj", new SquadCeaseFire());
        sim.RunFor(TimeSpan.FromSeconds(20));
        var from = sim.TimeSeconds;
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.Time > from && !e.AgentId.StartsWith("red", StringComparison.Ordinal));
        Assert.Null(sim.Unit("k1").FireArea);
    }

    [Fact]
    public void A_fire_distribution_can_be_ordered_from_outside()
    {
        var sim = Squad();
        Distributed(sim);
        var before = FireCommands(sim).Count;

        sim.Command("rj", new DirectSquadFire());

        Assert.Equal(before + 1, FireCommands(sim).Count);
    }
}
