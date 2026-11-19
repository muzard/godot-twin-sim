using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// When a member finds no firing position of its own, its team leader moves the team (backlog 0005, FP6–FP11). A member
/// tells its team leader (<see cref="CannotFireReported"/>); a team leader with fewer than two of its team able to fire,
/// and its machine gun unable to, moves the whole team on its own (a squad command named
/// <see cref="SpecFiringPositions.TeamMoveCommand"/>); otherwise it asks the squad leader (<see cref="TeamMoveRequested"/>),
/// who grants when no other team is moving and the squad has fire superiority (<see cref="TeamMoveAnswered"/>). A team
/// leader that finds no team position tells the squad leader, who searches wider. The command team's leader is the squad
/// leader, who decides for its own team without asking.
/// <para>
/// The layout is <see cref="FireControlTests"/>'s: the reference squad in a row facing north from rj at (100, 61), its
/// weapons never hitting; red-left at (60, 161) and red-right at (140, 161), holding fire. After the fire distribution
/// red-right lies in the point team's fire area (pj, k1, k2 at x 109–115, about 104 m from it) and red-left in the
/// support team's (vj, kk, t1 at x 118–124, 110–119 m from it). Shortened weapons keep chosen members from engaging.
/// </para>
/// </summary>
public class FiringPositionTeamTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly Vec2 Leader = new(100, 61);
    private static readonly string[] PointTeam = ["pj", "k1", "k2"];
    private static readonly string[] SupportTeam = ["vj", "kk", "t1"];

    private static WeaponProfile NeverHits(SquadMemberSpec member) =>
        SpecSuppression.NeverHits(member.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);

    /// <summary>Members in <paramref name="shortened"/> carry weapons reaching only <paramref name="rangeMeters"/>.</summary>
    private static Func<SquadMemberSpec, WeaponProfile> Reaching(double rangeMeters, params string[] shortened) =>
        member => shortened.Contains(member.UnitId) ? SpecFiringPositions.ShortRange(NeverHits(member), rangeMeters) : NeverHits(member);

    private static ScenarioRun Squad(Func<SquadMemberSpec, WeaponProfile> weapons, bool redCentre = false)
    {
        var scenario = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference(), Leader, rules: RulesOfEngagement.ReturnFire, weapons: weapons)
            .WithFighter("red-left", new Vec2(60, 161), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("red-right", new Vec2(140, 161), Side.Red, RulesOfEngagement.HoldFire);
        if (redCentre)
        {
            scenario.WithFighter("red-centre", new Vec2(100, 161), Side.Red, RulesOfEngagement.HoldFire);
        }

        var sim = scenario.Build();
        string[] reds = redCentre ? ["red-left", "red-right", "red-centre"] : ["red-left", "red-right"];
        sim.RunUntil(s => reds.All(r => s.Simulation.KnowsOf("rj", r)), TwoMinutes, "rj knows of every red fighter");
        return sim;
    }

    private static bool IsTeamMove(CommandIssued e, string leaderId) =>
        e.AgentId == leaderId && e.Command.StartsWith(SpecFiringPositions.TeamMoveCommand, StringComparison.Ordinal);

    private static ScenarioRun RunUntilTeamMove(ScenarioRun sim, string leaderId, TimeSpan timeout) =>
        sim.RunUntil(s => s.Events.OfType<CommandIssued>().Any(e => IsTeamMove(e, leaderId)), timeout, $"{leaderId} moves a team");

    private static ScenarioRun RunUntilAllFire(ScenarioRun sim, IEnumerable<string> members, string targetId, TimeSpan timeout) =>
        sim.RunUntil(s => members.All(m => s.ShotsBy(m).Any(e => e.TargetId == targetId)), timeout, $"all of them fire at {targetId}");

    [Fact]
    public void FP6_a_member_that_finds_no_firing_position_tells_its_team_leader()
    {
        // The point team's rifles reach 60 m: red-right is about 104 m off, 44 m more than any point within 30 m closes.
        var sim = Squad(Reaching(60, PointTeam));

        sim.RunUntil(s => s.Events.OfType<CannotFireReported>().Any(e => e.AgentId == "k1"), TwoMinutes, "k1 reports");

        Assert.Equal("pj", sim.Events.OfType<CannotFireReported>().First(e => e.AgentId == "k1").LeaderId);
    }

    [Fact]
    public void FP7_a_team_leader_whose_team_cannot_fire_moves_the_team_without_asking_and_the_team_fires()
    {
        // No one in the point team can engage red-right, and the team has no machine gun.
        var sim = Squad(Reaching(60, PointTeam));

        RunUntilTeamMove(sim, "pj", TwoMinutes);
        RunUntilAllFire(sim, PointTeam, "red-right", FiveMinutes);

        Assert.DoesNotContain(sim.Events.OfType<TeamMoveRequested>(), e => e.AgentId == "pj");
        foreach (var member in PointTeam)
        {
            var at = sim.Unit(member).Position.Xy;
            Assert.True(at.DistanceTo(new Vec2(140, 161)) <= 60, $"{member} fired from {at}, out of its range");
        }
    }

    [Fact]
    public void FP8_a_team_leader_with_two_of_its_team_still_firing_asks_the_squad_leader_and_moves_on_the_grant()
    {
        // Only k2's rifle falls short; pj and k1 can engage red-right where they stand. Red holds fire, so the squad has
        // fire superiority, and no other team is moving: rj grants.
        var sim = Squad(Reaching(60, "k2"));

        RunUntilTeamMove(sim, "pj", FiveMinutes);

        var request = Assert.Single(sim.Events.OfType<TeamMoveRequested>(), e => e.AgentId == "pj");
        Assert.Equal(SpecSquad.PointTeam, request.Team);
        var answer = Assert.Single(sim.Events.OfType<TeamMoveAnswered>(), e => e.Team == SpecSquad.PointTeam);
        Assert.Equal("rj", answer.AgentId);
        Assert.True(answer.Granted, "rj should grant: no other team is moving and red is silent");
        Assert.True(sim.Events.OfType<CommandIssued>().First(e => IsTeamMove(e, "pj")).Time >= answer.Time,
            "pj should move the team only after the grant");
    }

    [Fact]
    public void FP9_the_squad_leader_denies_a_team_move_while_another_team_is_moving_and_the_team_leader_asks_again_after_the_retry_time()
    {
        // The whole support team falls short of red-left, its machine gun too, so vj moves it without asking. Meanwhile
        // only k2 falls short in the point team, so pj asks rj, while the support team is on its way.
        var sim = Squad(Reaching(60, ["k2", .. SupportTeam]));

        sim.RunUntil(s => s.Events.OfType<TeamMoveAnswered>().Any(e => e.Team == SpecSquad.PointTeam), FiveMinutes, "rj answers pj");
        var denial = sim.Events.OfType<TeamMoveAnswered>().First(e => e.Team == SpecSquad.PointTeam);
        Assert.True(sim.Events.OfType<CommandIssued>().Any(e => IsTeamMove(e, "vj") && e.Time <= denial.Time),
            "precondition: vj moved the support team before rj answered pj");
        Assert.False(denial.Granted, "rj should deny while the support team is moving");

        sim.RunUntil(s => s.Events.OfType<TeamMoveRequested>().Count(e => e.AgentId == "pj") >= 2, FiveMinutes, "pj asks again");

        var again = sim.Events.OfType<TeamMoveRequested>().Where(e => e.AgentId == "pj").ElementAt(1);
        Assert.True(again.Time >= denial.Time + SpecFiringPositions.RetrySeconds - 1e-6,
            $"pj asked again {again.Time - denial.Time:0.0} s after the denial");
    }

    [Fact]
    public void FP10_a_team_leader_that_finds_no_team_position_tells_the_squad_leader_who_moves_the_team_from_its_wider_search()
    {
        // The point team's rifles reach 40 m: from about 104 m off that needs 64 m more, beyond pj's 60 m search but
        // within rj's 100 m.
        var sim = Squad(Reaching(40, PointTeam));

        RunUntilTeamMove(sim, "rj", FiveMinutes);
        RunUntilAllFire(sim, PointTeam, "red-right", FiveMinutes);

        Assert.Contains(sim.Events.OfType<CannotFireReported>(), e => e.AgentId == "pj" && e.LeaderId == "rj");
        Assert.DoesNotContain(sim.Events.OfType<CommandIssued>(), e => IsTeamMove(e, "pj"));
    }

    [Fact]
    public void FP10_when_the_squad_leader_finds_no_position_either_the_team_stays_and_nobody_reports_again()
    {
        // The point team's rifles reach 1 m: no position within anyone's search brings red-right into range. (Changed with
        // the product owner's approval, 2026-11-19, from 10 m: rj's 100 m search from the team's centre reaches about 4 m
        // from red-right, which 10 m covers.)
        var sim = Squad(Reaching(1, PointTeam));
        sim.RunUntil(s => s.Events.OfType<CannotFireReported>().Any(e => e.AgentId == "pj" && e.LeaderId == "rj"), TwoMinutes, "pj reports to rj");
        sim.RunFor(TimeSpan.FromSeconds(30)); // rj searches and finds nothing
        var reports = sim.Events.OfType<CannotFireReported>().Count();
        var positions = PointTeam.Select(m => sim.Unit(m).Position.Xy).ToList();

        sim.RunFor(TwoMinutes);

        Assert.Equal(reports, sim.Events.OfType<CannotFireReported>().Count());
        Assert.DoesNotContain(sim.Events.OfType<CommandIssued>(), e => IsTeamMove(e, "pj") || IsTeamMove(e, "rj"));
        for (var i = 0; i < PointTeam.Length; i++)
        {
            Assert.True(sim.Unit(PointTeam[i]).Position.Xy.DistanceTo(positions[i]) <= 0.5, $"{PointTeam[i]} moved");
        }
    }

    [Fact]
    public void FP11_the_squad_leader_moves_its_own_command_team_without_asking()
    {
        // A third red fighter at (100, 161), in the command team's fire area. Only j1's rifle falls short (60 m; red-centre
        // is 100 m off, 40 m more than any point within 30 m closes); rj and j2 can engage, yet rj, its team leader,
        // decides at once.
        var sim = Squad(Reaching(60, "j1"), redCentre: true);

        RunUntilTeamMove(sim, "rj", FiveMinutes);

        Assert.Contains(sim.Events.OfType<CannotFireReported>(), e => e.AgentId == "j1" && e.LeaderId == "rj");
        Assert.Empty(sim.Events.OfType<TeamMoveRequested>());
    }
}
