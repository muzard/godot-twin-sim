using Twin.Sim.Events;

namespace Twin.Testing;

/// <summary>
/// Vocabulary for spotting and spot reports (backlog 0004 S5a1). Spotting builds up step by step, so "spotted after t
/// seconds" means: no earlier than t, and no later than one tick after t (as <see cref="CommandAssertions"/>).
/// </summary>
public static class SpottingAssertions
{
    private const double Epsilon = 1e-9;

    /// <summary>When <paramref name="observerId"/> spotted <paramref name="enemyId"/>, or null if it has not.</summary>
    public static EnemySpotted? SpottedBy(this ScenarioRun sim, string observerId, string enemyId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.Events.OfType<EnemySpotted>().FirstOrDefault(e => e.AgentId == observerId && e.EnemyId == enemyId);
    }

    /// <summary>The first <see cref="EnemySpotted"/> of <paramref name="enemyId"/> by anyone, or null if no one has.</summary>
    public static EnemySpotted? FirstSpotting(this ScenarioRun sim, string enemyId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.Events.OfType<EnemySpotted>().FirstOrDefault(e => e.EnemyId == enemyId);
    }

    /// <summary>Runs until <paramref name="observerId"/> spots <paramref name="enemyId"/>; fails after <paramref name="timeout"/>.</summary>
    public static EnemySpotted RunUntilSpotted(this ScenarioRun sim, string observerId, string enemyId, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sim);
        sim.RunUntil(s => s.SpottedBy(observerId, enemyId) is not null, timeout, $"{observerId} spots {enemyId}");
        return sim.SpottedBy(observerId, enemyId)!;
    }

    /// <summary>Runs until anyone spots <paramref name="enemyId"/>; fails after <paramref name="timeout"/>.</summary>
    public static EnemySpotted RunUntilAnyoneSpots(this ScenarioRun sim, string enemyId, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sim);
        sim.RunUntil(s => s.FirstSpotting(enemyId) is not null, timeout, $"someone spots {enemyId}");
        return sim.FirstSpotting(enemyId)!;
    }

    /// <summary>Runs until <paramref name="agentId"/> knows of <paramref name="enemyId"/>; fails after <paramref name="timeout"/>.</summary>
    public static ScenarioRun RunUntilKnows(this ScenarioRun sim, string agentId, string enemyId, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.RunUntil(s => s.Simulation.KnowsOf(agentId, enemyId), timeout, $"{agentId} knows of {enemyId}");
    }

    /// <summary>
    /// <paramref name="observerId"/> spotted <paramref name="enemyId"/> <paramref name="seconds"/> after
    /// <paramref name="fromTime"/> (within one tick after).
    /// </summary>
    public static void ShouldHaveSpottedAfter(
        this ScenarioRun sim, string observerId, string enemyId, double seconds, double fromTime = 0)
    {
        ArgumentNullException.ThrowIfNull(sim);
        var due = fromTime + seconds;
        var tick = sim.Simulation.Config.TickSeconds;
        var spotted = sim.SpottedBy(observerId, enemyId) ?? throw new ScenarioAssertionException(
            $"Expected '{observerId}' to spot '{enemyId}' at t={due:0.###} s, but it has not spotted it by " +
            $"t={sim.TimeSeconds:0.###} s.{Environment.NewLine}{sim.Describe()}");
        if (spotted.Time < due - Epsilon || spotted.Time > due + tick + Epsilon)
        {
            throw new ScenarioAssertionException(
                $"Expected '{observerId}' to spot '{enemyId}' {seconds:0.###} s after t={fromTime:0.###} s, i.e. at " +
                $"t={due:0.###} s (+ at most one tick), but it spotted it at t={spotted.Time:0.###} s.");
        }
    }

    /// <summary><paramref name="observerId"/> has not spotted <paramref name="enemyId"/> so far.</summary>
    public static void ShouldNotHaveSpotted(this ScenarioRun sim, string observerId, string enemyId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        if (sim.SpottedBy(observerId, enemyId) is { } spotted)
        {
            throw new ScenarioAssertionException(
                $"Expected '{observerId}' not to spot '{enemyId}', but it spotted it at t={spotted.Time:0.###} s." +
                $"{Environment.NewLine}{sim.Describe()}");
        }
    }

    /// <summary>Every <see cref="SpotReported"/> of <paramref name="enemyId"/> so far, in order: the report and its relays.</summary>
    public static IReadOnlyList<SpotReported> ReportsOf(this ScenarioRun sim, string enemyId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.Events.OfType<SpotReported>().Where(e => e.EnemyId == enemyId).ToList();
    }

    /// <summary>The <see cref="SpotReceived"/> of <paramref name="agentId"/> from <paramref name="reporterId"/>, or null.</summary>
    public static SpotReceived? ReceivedFrom(this ScenarioRun sim, string agentId, string reporterId)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return sim.Events.OfType<SpotReceived>().FirstOrDefault(e => e.AgentId == agentId && e.ReporterId == reporterId);
    }

    /// <summary>The event happened <paramref name="seconds"/> after <paramref name="fromTime"/> (within one tick after).</summary>
    public static void ShouldBeAt(this ScenarioRun sim, SimEvent e, double seconds, double fromTime, string what)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(e);
        var due = fromTime + seconds;
        var tick = sim.Simulation.Config.TickSeconds;
        if (e.Time < due - Epsilon || e.Time > due + tick + Epsilon)
        {
            throw new ScenarioAssertionException(
                $"Expected {what} {seconds:0.###} s after t={fromTime:0.###} s, i.e. at t={due:0.###} s (+ at most one " +
                $"tick), but it happened at t={e.Time:0.###} s: {e}.");
        }
    }
}
