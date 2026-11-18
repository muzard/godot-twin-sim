using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Terrain;
using Twin.Sim.Units;

namespace Twin.Testing;

/// <summary>
/// A running scenario. Time advances only through <see cref="Step"/>, <see cref="RunFor"/>
/// and <see cref="RunUntil"/>, so every test is reproducible.
/// </summary>
public sealed class ScenarioRun
{
    private readonly SortedDictionary<string, List<Vec2>> _trails = new(StringComparer.Ordinal);

    internal ScenarioRun(Simulation simulation)
    {
        Simulation = simulation;
        RecordTrails();
    }

    /// <summary>The underlying simulation, for checks the kit does not cover yet.</summary>
    public Simulation Simulation { get; }

    /// <summary>The scenario's terrain.</summary>
    public ITerrain Terrain => Simulation.Terrain;

    /// <summary>Simulated seconds elapsed.</summary>
    public double TimeSeconds => Simulation.TimeSeconds;

    /// <summary>Every event the simulation has logged so far, in order (the event log).</summary>
    public IReadOnlyList<SimEvent> Events => Simulation.EventLog;

    /// <summary>
    /// A unit's horizontal position when the run was built and after every tick since: the route it actually took.
    /// </summary>
    public IReadOnlyList<Vec2> Trail(string unitId) =>
        _trails.TryGetValue(unitId, out var trail) ? trail : throw new KeyNotFoundException($"No unit with id '{unitId}'.");

    /// <summary>Looks up a unit.</summary>
    public Unit Unit(string id) => Simulation.GetUnit(id);

    /// <summary>Every shot <paramref name="shooterId"/> has fired so far, in order.</summary>
    public IReadOnlyList<ShotFired> ShotsBy(string shooterId) =>
        Events.OfType<ShotFired>().Where(e => e.AgentId == shooterId).ToList();

    /// <summary>
    /// Where a unit was (horizontally) after the tick at simulated time <paramref name="timeSeconds"/>, from its trail.
    /// Time 0 is the position when the run was built.
    /// </summary>
    public Vec2 PositionAt(string unitId, double timeSeconds)
    {
        var trail = Trail(unitId);
        var index = (int)Math.Round(timeSeconds * Simulation.Config.TickRateHz);
        if (index < 0 || index >= trail.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds), $"No position of '{unitId}' recorded at t={timeSeconds} s.");
        }

        return trail[index];
    }

    /// <summary>Issues an order; it takes effect on the next tick.</summary>
    public ScenarioRun Order(string unitId, Order order)
    {
        Simulation.Issue(unitId, order);
        return this;
    }

    /// <summary>Runs exactly <paramref name="ticks"/> ticks.</summary>
    public ScenarioRun Step(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
        {
            Simulation.Step();
            RecordTrails();
        }

        return this;
    }

    /// <summary>Runs for a span of simulated time (rounded up to whole ticks).</summary>
    public ScenarioRun RunFor(TimeSpan duration) =>
        Step((int)Math.Ceiling(duration.TotalSeconds * Simulation.Config.TickRateHz));

    /// <summary>
    /// Runs until <paramref name="condition"/> holds, checking after every tick.
    /// Fails with a diagnostic message if simulated <paramref name="timeout"/> passes first,
    /// so a broken behaviour never hangs the test suite.
    /// </summary>
    public ScenarioRun RunUntil(Func<ScenarioRun, bool> condition, TimeSpan timeout, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var maxTicks = (long)Math.Ceiling(timeout.TotalSeconds * Simulation.Config.TickRateHz);
        for (long i = 0; i < maxTicks; i++)
        {
            Simulation.Step();
            RecordTrails();
            if (condition(this))
            {
                return this;
            }
        }

        throw new ScenarioAssertionException(
            $"Condition {(description is null ? string.Empty : $"'{description}' ")}was not met within {timeout.TotalSeconds:0.##} s " +
            $"of simulated time (tick {Simulation.Tick}).{Environment.NewLine}{Describe()}");
    }

    /// <summary>
    /// Runs until a shot hits <paramref name="targetId"/> (a <see cref="ShotFired"/> with <c>Hit</c> true), checking after
    /// every tick, and returns that shot. Fails with a diagnostic if no hit comes within simulated <paramref name="timeout"/>.
    /// </summary>
    public ShotFired RunUntilHit(string targetId, TimeSpan timeout)
    {
        RunUntil(s => s.Events.OfType<ShotFired>().Any(e => e.TargetId == targetId && e.Hit), timeout, $"{targetId} hit");
        return Events.OfType<ShotFired>().First(e => e.TargetId == targetId && e.Hit);
    }

    /// <summary>Looks up a squad (backlog 0004).</summary>
    public Squad Squad(string id) => Simulation.GetSquad(id);

    /// <summary>
    /// Gives a squad order to <paramref name="leaderId"/> and runs the one tick on which it takes effect, so the leader
    /// commands the squad. Returns the <see cref="CommandIssued"/> event that tick logged; fails if there is none.
    /// </summary>
    public CommandIssued Command(string leaderId, Order order)
    {
        var before = Events.Count;
        Order(leaderId, order).Step();
        return Events.Skip(before).OfType<CommandIssued>().SingleOrDefault(e => e.AgentId == leaderId)
            ?? throw new ScenarioAssertionException(
                $"Expected '{leaderId}' to command its squad on the tick after {order}, but it logged no CommandIssued." +
                $"{Environment.NewLine}{Describe()}");
    }

    /// <summary>
    /// Orders <paramref name="shooterId"/> to fire at <paramref name="targetId"/> until the target is out of action (two
    /// real hits, as in backlog 0002), then orders it to cease fire. Returns the simulated time at which the target went
    /// out of action. Fails with a diagnostic if that does not happen within <paramref name="timeout"/>.
    /// </summary>
    public double RunUntilOutOfAction(string targetId, string shooterId, TimeSpan timeout)
    {
        Order(shooterId, new FireAt(targetId));
        RunUntil(s => s.Unit(targetId).Health == Health.OutOfAction, timeout, $"{targetId} out of action");
        Order(shooterId, new CeaseFire());
        return Events.OfType<HealthChanged>().Last(e => e.AgentId == targetId && e.Health == Health.OutOfAction).Time;
    }

    /// <summary>The moment <paramref name="agentId"/> acted on <paramref name="command"/>, or null if it has not.</summary>
    public CommandReceived? ReceivedBy(string agentId, CommandIssued command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Events.OfType<CommandReceived>().FirstOrDefault(e => e.AgentId == agentId && e.CommandId == command.CommandId);
    }

    private void RecordTrails()
    {
        foreach (var unit in Simulation.Units)
        {
            if (!_trails.TryGetValue(unit.Id, out var trail))
            {
                trail = [];
                _trails.Add(unit.Id, trail);
            }

            trail.Add(unit.Position.Xy);
        }
    }

    /// <summary>Every unit's state as text. Included in failure messages.</summary>
    public string Describe() =>
        string.Join(Environment.NewLine, Simulation.Units.Select(UnitAssertions.DescribeUnit));
}
