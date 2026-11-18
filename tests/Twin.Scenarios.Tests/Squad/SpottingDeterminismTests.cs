using System.Text;
using Twin.Sim.Events;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Determinism and the event log with spotting (backlog 0004 S5a1: SP12, as SQ6). The run: the wedge on the move sees
/// red, reports it, halts, and is ordered on 10 s later. The same seed gives the same log byte for byte; the new events
/// survive a round trip through the log; and every agent's known enemies and the squad's halt can be rebuilt from the
/// log at every step.
/// </summary>
public class SpottingDeterminismTests
{
    private static ScenarioRun Run(ScenarioRun sim)
    {
        sim.RedStandsUp().RunUntilKnows("rj", "red", TimeSpan.FromMinutes(1)).RunFor(TimeSpan.FromSeconds(10));
        sim.Order("rj", new ResumeMovement());
        return sim.RunFor(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SP12_spotting_with_the_same_seed_gives_byte_identical_logs()
    {
        var first = EventLog.ToJsonLines(Run(SpottingLayouts.AheadOnTheMove(seed: 1)).Events);
        var second = EventLog.ToJsonLines(Run(SpottingLayouts.AheadOnTheMove(seed: 1)).Events);

        foreach (var name in new[] { nameof(EnemySpotted), nameof(SpotReported), nameof(SpotReceived), nameof(SquadHalted), nameof(SquadResumed) })
        {
            Assert.Contains(name, first, StringComparison.Ordinal);
        }

        Assert.Equal(Encoding.UTF8.GetBytes(first), Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void SP12_spotting_events_survive_a_round_trip_through_the_log()
    {
        var text = EventLog.ToJsonLines(Run(SpottingLayouts.AheadOnTheMove()).Events);

        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }

    [Fact]
    public void SP12_known_enemies_and_the_halt_rebuilt_from_the_log_match_the_live_state()
    {
        var sim = SpottingLayouts.AheadOnTheMove().RedStandsUp();
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };
        double? resumeAt = null;
        while (resumeAt is null || sim.TimeSeconds < resumeAt + 30)
        {
            if (resumeAt is null && sim.Squad("r1").IsHalted && sim.Events.OfType<SquadHalted>().Single().Time + 10 <= sim.TimeSeconds)
            {
                sim.Order("rj", new ResumeMovement());
                resumeAt = sim.TimeSeconds;
            }

            sim.Step();
            live.Add(sim.Simulation.Snapshot());
            Assert.True(sim.TimeSeconds < 300, "the squad should halt and be ordered on within 5 minutes");
        }

        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.True(expected.Agents.SequenceEqual(replayed.Agents), $"At t={expected.TimeSeconds} s the agents differ.");
            Assert.True(expected.KnownEnemies.SequenceEqual(replayed.KnownEnemies), $"At t={expected.TimeSeconds} s the known enemies differ.");
            Assert.Equal(expected.Squads.Select(s => s.Halted), replayed.Squads.Select(s => s.Halted));
        }

        Assert.Contains(live, s => s.Squads.Single().Halted);
        Assert.Contains(live[^1].KnownEnemies, k => k.AgentId == "rj" && k.EnemyId == "red");
        // Red watches too and may know of squad members; nobody in the squad knows of red before it stands up.
        Assert.DoesNotContain(live[0].KnownEnemies, k => k.EnemyId == "red");
    }
}
