using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Determinism and the event log with a squad (backlog 0003, S1: SQ6). The same seed gives the same log byte for byte,
/// and the squads' leaders and team leaders can be rebuilt from the log at every step, as agents can (A4, D3).
/// The run: the squad leader orders the squad down, then red shoots it out of action and the second-in-command takes
/// command.
/// </summary>
public class SquadDeterminismTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static ScenarioRun SquadUnderFire(ulong seed) => Scenario.ForSquad()
        .WithSeed(seed)
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 60).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(21, 21))
        .WithFighter("red", new Vec2(21, 26), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    private static ScenarioRun Run(ScenarioRun sim)
    {
        sim.Command("rj", new SetSquadPosture(Posture.Prone));
        sim.RunUntilOutOfAction("rj", "red", FiveMinutes);
        return sim.RunFor(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SQ6_a_squad_run_with_the_same_seed_gives_byte_identical_logs()
    {
        var first = EventLog.ToJsonLines(Run(SquadUnderFire(seed: 1)).Events);
        var second = EventLog.ToJsonLines(Run(SquadUnderFire(seed: 1)).Events);

        foreach (var type in new[] { "SquadFormed", "CommandIssued", "CommandReceived", "LeaderChanged", "TeamLeaderChanged" })
        {
            Assert.Contains(type, first, StringComparison.Ordinal);
        }

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void SQ6_squad_events_survive_a_round_trip_through_the_log()
    {
        var events = Run(SquadUnderFire(seed: 1)).Events;

        var text = EventLog.ToJsonLines(events);

        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }

    [Fact]
    public void SQ6_squads_and_agents_rebuilt_from_the_log_match_the_live_state()
    {
        var sim = SquadUnderFire(seed: 1);
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };
        sim.Order("rj", new SetSquadPosture(Posture.Prone)).Order("red", new FireAt("rj"));
        var steps = (int)(TimeSpan.FromMinutes(2).TotalSeconds * SpecV01.TickRateHz);
        for (var i = 0; i < steps; i++)
        {
            sim.Step();
            live.Add(sim.Simulation.Snapshot());
        }

        Assert.Contains(sim.Events, e => e is LeaderChanged);
        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.True(expected.Agents.SequenceEqual(replayed.Agents), $"At t={expected.TimeSeconds} s the agents differ.");
            Assert.True(SameSquads(expected, replayed),
                $"At t={expected.TimeSeconds} s live {Describe(expected)} but replayed {Describe(replayed)}");
        }
    }

    private static bool SameSquads(WorldSnapshot a, WorldSnapshot b) =>
        a.Squads.Count == b.Squads.Count && a.Squads.Zip(b.Squads).All(p =>
            p.First.Id == p.Second.Id && p.First.LeaderId == p.Second.LeaderId &&
            p.First.TeamLeaderIds.SequenceEqual(p.Second.TeamLeaderIds));

    private static string Describe(WorldSnapshot snapshot) =>
        string.Join("; ", snapshot.Squads.Select(s =>
            $"{s.Id} led by {s.LeaderId ?? "nobody"}, teams led by [{string.Join(", ", s.TeamLeaderIds.Select(t => t ?? "nobody"))}]"));
}
