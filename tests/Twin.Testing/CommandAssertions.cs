using Twin.Sim.Events;

namespace Twin.Testing;

/// <summary>
/// Assertions about when squad members act on a command (backlog 0003). Delays are whole ticks in
/// <see cref="Scenario.ForSquad"/>, but a system may notice a due delay on the tick after, so "acts after d seconds"
/// means: no earlier than d, and no later than one tick after d.
/// </summary>
public static class CommandAssertions
{
    private const double Epsilon = 1e-9;

    /// <summary>
    /// <paramref name="agentId"/> acted on <paramref name="command"/> <paramref name="delaySeconds"/> after it was
    /// issued (within one tick after).
    /// </summary>
    public static void ShouldHaveActedOn(this ScenarioRun sim, string agentId, CommandIssued command, double delaySeconds)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(command);
        var due = command.Time + delaySeconds;
        var tick = sim.Simulation.Config.TickSeconds;
        var received = sim.ReceivedBy(agentId, command) ?? throw new ScenarioAssertionException(
            $"Expected '{agentId}' to act on command {command.CommandId} ({command.Command}) at t={due:0.###} s, " +
            $"but it has not acted on it by t={sim.TimeSeconds:0.###} s.{Environment.NewLine}{sim.Describe()}");
        if (received.Time < due - Epsilon || received.Time > due + tick + Epsilon)
        {
            throw new ScenarioAssertionException(
                $"Expected '{agentId}' to act on command {command.CommandId} ({command.Command}) {delaySeconds:0.###} s after " +
                $"it was issued at t={command.Time:0.###} s, i.e. at t={due:0.###} s (+ at most one tick), but it acted at " +
                $"t={received.Time:0.###} s ({received.Time - command.Time:0.###} s after).");
        }
    }

    /// <summary><paramref name="agentId"/> has not acted on <paramref name="command"/> so far.</summary>
    public static void ShouldNotHaveActedOn(this ScenarioRun sim, string agentId, CommandIssued command)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(command);
        if (sim.ReceivedBy(agentId, command) is { } received)
        {
            throw new ScenarioAssertionException(
                $"Expected '{agentId}' not to act on command {command.CommandId} ({command.Command}) issued at " +
                $"t={command.Time:0.###} s, but it acted at t={received.Time:0.###} s.");
        }
    }
}
