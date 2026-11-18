using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Suppression slows command (backlog 0004 S5a2: UF5, UF6). A member acts on a command after 1.5 × its reaction time
/// while it is suppressed and 3 × while it is heavily suppressed (0003's two levels), judged when the command reaches it;
/// a team leader's relay is slowed the same way by its own state; a successor takes command after 1.5 × or 3 × the
/// takeover time. Red fires with weapons that never hit, so suppression is all they do; the squad holds fire, so no one
/// is in the drill.
/// </summary>
public class SuppressedCommandTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly WeaponProfile SuppressingMachineGun = SpecSuppression.NeverHits(SpecSuppression.MachineGun);
    private static readonly WeaponProfile SuppressingRifle = SpecSuppression.NeverHits(SpecShooting.Rifle);

    // The reference squad in a row, 3 m apart, from rj at (21, 61) to t1 at (45, 61); red-mg and red-rifle 40 m south
    // of the row, holding fire until ordered.
    private static ScenarioRun Squad(SquadSpec? squad = null) => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 120).Build())
        .WithSquad(squad ?? SpecSquad.Reference(), new Vec2(21, 61))
        .WithFighter("red-mg", new Vec2(30, 21), Side.Red, RulesOfEngagement.HoldFire, SuppressingMachineGun)
        .WithFighter("red-rifle", new Vec2(40, 21), Side.Red, RulesOfEngagement.HoldFire, SuppressingRifle)
        .Build();

    private static ScenarioRun HeavilySuppress(ScenarioRun sim, string id) =>
        sim.Order("red-mg", new FireAt(id)).RunUntil(s => s.Unit(id).IsHeavilySuppressed, OneMinute, $"{id} heavily suppressed");

    [Fact]
    public void UF5_a_heavily_suppressed_member_acts_on_a_command_after_three_times_its_reaction_time()
    {
        var sim = HeavilySuppress(Squad(), "k2"); // experienced: 1 s

        var command = sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        sim.RunFor(TimeSpan.FromSeconds(10));

        sim.ShouldHaveActedOn("k2", command, 3 * SpecSquad.ExperiencedReactionSeconds);
    }

    [Fact]
    public void UF5_a_suppressed_member_acts_on_a_command_after_one_and_a_half_times_its_reaction_time()
    {
        var sim = Squad().Order("red-rifle", new FireAt("k2"))
            .RunUntil(s => s.Unit("k2").IsSuppressed, OneMinute, "k2 suppressed");
        Assert.False(sim.Unit("k2").IsHeavilySuppressed, "precondition: k2 is suppressed, not heavily");

        var command = sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        sim.RunFor(TimeSpan.FromSeconds(10));

        sim.ShouldHaveActedOn("k2", command, 1.5 * SpecSquad.ExperiencedReactionSeconds);
    }

    [Fact]
    public void UF5_an_unsuppressed_member_acts_after_its_normal_reaction_time()
    {
        var sim = HeavilySuppress(Squad(), "k2");

        var command = sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        sim.RunFor(TimeSpan.FromSeconds(10));

        sim.ShouldHaveActedOn("j2", command, SpecSquad.ExperiencedReactionSeconds); // 18 m from k2, not suppressed
        Assert.False(sim.Unit("j2").IsSuppressed);
    }

    [Fact]
    public void UF5_a_heavily_suppressed_team_leader_passes_a_command_on_three_times_later()
    {
        // The point team leader pj stands first in the row (x = 21) and its inexperienced k1 last (x = 45), out of reach of
        // the fire at pj. pj (inexperienced) acts after 3 × 3 s and passes the command on after 3 × 2 s; k1, not suppressed,
        // acts 3 s after that.
        var reordered = SpecSquad.Reference() with
        {
            Members = [.. SpecSquad.Reference().Members.Where(m => m.UnitId == "pj"),
                .. SpecSquad.Reference().Members.Where(m => m.UnitId is not ("pj" or "k1")),
                .. SpecSquad.Reference().Members.Where(m => m.UnitId == "k1")],
        };
        var sim = HeavilySuppress(Squad(reordered), "pj");

        var command = sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.False(sim.Unit("k1").IsSuppressed, "precondition: k1 is not suppressed");
        sim.ShouldHaveActedOn("pj", command, 3 * SpecSquad.InexperiencedReactionSeconds);
        sim.ShouldHaveActedOn("k1", command,
            (3 * SpecSquad.InexperiencedReactionSeconds) + (3 * SpecSquad.RelaySeconds) + SpecSquad.InexperiencedReactionSeconds);
    }

    [Fact]
    public void UF6_a_heavily_suppressed_second_in_command_takes_command_after_three_times_the_takeover_time()
    {
        // red-killer, 13 m west of rj, takes rj out of action while red-mg, 40 m north of vj, keeps vj heavily suppressed.
        // Neither stands in the other's fire sector (a friend there would stop it firing).
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 120).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(21, 61))
            .WithFighter("red-mg", new Vec2(39, 101), Side.Red, RulesOfEngagement.HoldFire, SuppressingMachineGun)
            .WithFighter("red-killer", new Vec2(8, 61), Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        HeavilySuppress(sim, "vj");

        var outOfAction = sim.RunUntilOutOfAction("rj", "red-killer", TimeSpan.FromMinutes(5));
        Assert.True(sim.Unit("vj").IsHeavilySuppressed, "precondition: vj is heavily suppressed when rj falls");
        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", OneMinute, "vj in command");

        var change = sim.Events.OfType<LeaderChanged>().Single();
        var due = outOfAction + (3 * SpecSquad.TakeoverSeconds);
        Assert.InRange(change.Time, due - 1e-9, due + SpecV01.DtSeconds + 1e-9);
    }
}
