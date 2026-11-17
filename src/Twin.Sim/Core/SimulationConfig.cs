using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Squads;

namespace Twin.Sim;

/// <summary>Fixed parameters of a simulation run. See docs/adr/0005-determinism.md.</summary>
/// <param name="TickRateHz">Simulation updates per simulated second. Independent of frame rate.</param>
/// <param name="Seed">Seed for all randomness in the run.</param>
public sealed record SimulationConfig(int TickRateHz = 20, ulong Seed = 1)
{
    /// <summary>Duration of one tick in seconds.</summary>
    public double TickSeconds => 1.0 / TickRateHz;

    /// <summary>The scripted leader who answers permission requests; null means requests are never answered.</summary>
    public ScriptedLeader? Leader { get; init; }

    /// <summary>When agents ask for permission and how long they wait.</summary>
    public PermissionRules Permission { get; init; } = PermissionRules.Default;

    /// <summary>Rules of line of sight, hits, posture and suppression (backlog 0002).</summary>
    public CombatRules Combat { get; init; } = CombatRules.Default;

    /// <summary>How commands travel in a squad: reaction, relay and takeover times (backlog 0004).</summary>
    public SquadDoctrine SquadDoctrine { get; init; } = SquadDoctrine.Default;
}
