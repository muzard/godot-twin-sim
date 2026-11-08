using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Determinism and the event log with combat (backlog 0002, D): the same seed gives the same engagement byte for byte,
/// another seed gives another one, and the state (including health, posture and suppression) can be rebuilt from the log.
/// Blue and red fight it out 150 m apart in the open, both on free fire.
/// </summary>
public class CombatDeterminismTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static ScenarioRun Duel(ulong seed) => Scenario.ForCombat()
        .WithSeed(seed)
        .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 40).Build())
        .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.FreeFire)
        .WithFighter("red", new Vec2(171, 21), Side.Red, RulesOfEngagement.FreeFire)
        .Build();

    [Fact]
    public void D1_an_engagement_with_the_same_seed_gives_byte_identical_logs()
    {
        var first = EventLog.ToJsonLines(Duel(seed: 1).RunFor(FiveMinutes).Events);
        var second = EventLog.ToJsonLines(Duel(seed: 1).RunFor(FiveMinutes).Events);

        Assert.Contains("ShotFired", first, StringComparison.Ordinal);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void D2_a_different_seed_gives_a_different_outcome()
    {
        var one = EventLog.ToJsonLines(Duel(seed: 1).RunFor(FiveMinutes).Events);
        var two = EventLog.ToJsonLines(Duel(seed: 2).RunFor(FiveMinutes).Events);

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void D3_health_suppression_and_posture_rebuilt_from_the_log_match_the_live_state()
    {
        var sim = Duel(seed: 1);
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };
        var steps = (int)(FiveMinutes.TotalSeconds * SpecV01.TickRateHz);
        for (var i = 0; i < steps; i++)
        {
            sim.Step();
            live.Add(sim.Simulation.Snapshot());
        }

        Assert.Contains(sim.Events, e => e is HealthChanged);
        Assert.Contains(sim.Events, e => e is SuppressionStarted);
        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.True(expected.Agents.SequenceEqual(replayed.Agents),
                $"At t={expected.TimeSeconds} s live {Describe(expected)} but replayed {Describe(replayed)}");
        }
    }

    private static string Describe(WorldSnapshot snapshot) =>
        string.Join("; ", snapshot.Agents.Select(a => $"{a.Id} {a.Activity} {a.Health} {a.Posture} suppressed={a.Suppressed} at {a.Position}"));
}
