using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Units;

namespace Twin.Headless;

/// <summary>
/// When a squad scenario's run is over (backlog 0004 S2d): its leader has arrived (<see cref="Arrived"/>; a leader left
/// idle by a halt on contact has not) or is blocked, one side has nobody left in action, or shots have been fired and
/// nobody has fired for <see cref="QuietSeconds"/>.
/// </summary>
internal sealed class SquadRunEnd(Simulation simulation)
{
    /// <summary>Once shots have been fired, the run ends when nobody has fired for this long.</summary>
    private const double QuietSeconds = 120;

    private int _seen;
    private double? _lastShot;
    private bool _arrived;

    /// <summary>True when the run is over (see the class remarks).</summary>
    public bool IsOver()
    {
        var events = simulation.EventLog;
        for (; _seen < events.Count; _seen++)
        {
            switch (events[_seen])
            {
                case ShotFired shot:
                    _lastShot = shot.Time;
                    break;
                case Arrived arrived when simulation.Squads.Any(s => s.LeaderId == arrived.AgentId):
                    _arrived = true;
                    break;
                default:
                    break;
            }
        }

        var squad = simulation.Squads.First();
        if (_arrived || (squad.LeaderId is { } leaderId && simulation.GetUnit(leaderId).Activity == UnitActivity.Blocked))
        {
            return true;
        }

        var sides = simulation.Units.Where(u => u.Combatant is not null).Select(u => u.Combatant!.Side).Distinct().Count();
        var sidesInAction = simulation.Units
            .Where(u => u.Combatant is not null && u.Health != Health.OutOfAction)
            .Select(u => u.Combatant!.Side)
            .Distinct()
            .Count();
        if (sidesInAction < sides)
        {
            return true;
        }

        return _lastShot is { } time && simulation.TimeSeconds - time >= QuietSeconds;
    }

    /// <summary>A one-line summary of the run for the console.</summary>
    public string Outcome()
    {
        int OutOfAction(Side side) => simulation.Units.Count(u => u.Combatant?.Side == side && u.Health == Health.OutOfAction);
        int Total(Side side) => simulation.Units.Count(u => u.Combatant?.Side == side);
        var log = simulation.EventLog;
        var halted = log.OfType<SquadHalted>().Any() ? "squad halted" : "squad not halted";
        return $"{log.OfType<EnemySpotted>().Count()} spotted, {halted}, {log.OfType<ShotFired>().Count()} shots, " +
            $"out of action blue {OutOfAction(Side.Blue)}/{Total(Side.Blue)} red {OutOfAction(Side.Red)}/{Total(Side.Red)}";
    }
}
