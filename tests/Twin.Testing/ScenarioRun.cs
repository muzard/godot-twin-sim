using Twin.Sim;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
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
