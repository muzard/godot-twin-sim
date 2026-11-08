using Twin.Sim;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Terrain;

namespace Twin.Testing;

/// <summary>
/// Entry point for scenario tests. Describe the world, then <see cref="Build"/> it:
/// <code>
/// var sim = Scenario.Create()
///     .WithTerrain(TestTerrain.Flat(1000))
///     .WithUnit("alpha", at: new(100, 100))
///     .Build();
/// </code>
/// </summary>
public sealed class Scenario
{
    /// <summary>Default ground speed for test units: brisk infantry pace.</summary>
    public const double DefaultSpeedMps = 5.0;

    private readonly List<(string Id, Vec2 At, double SpeedMps, MovementProfile? Profile)> _units = [];
    private ITerrain _terrain = TestTerrain.Flat(1000);
    private SimulationConfig _config = new();

    private Scenario()
    {
    }

    /// <summary>Starts describing a scenario. Defaults: 1 km flat map, 20 Hz, seed 1.</summary>
    public static Scenario Create() => new();

    /// <summary>Sets the terrain.</summary>
    public Scenario WithTerrain(ITerrain terrain)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        return this;
    }

    /// <summary>Adds a ground unit.</summary>
    public Scenario WithUnit(string id, Vec2 at, double speedMps = DefaultSpeedMps)
    {
        _units.Add((id, at, speedMps, null));
        return this;
    }

    /// <summary>
    /// Adds an agent that moves by a movement profile: terrain-dependent speeds, slope limit and eye height.
    /// Use <see cref="SpecV01.Infantry"/> for spec v0.1 infantry.
    /// </summary>
    public Scenario WithUnit(string id, Vec2 at, MovementProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _units.Add((id, at, 0, profile));
        return this;
    }

    /// <summary>
    /// Adds the scripted leader who answers permission requests: after <paramref name="after"/> of simulated time it
    /// grants, denies or never answers. A permit is valid within <paramref name="permitRadiusMeters"/> of the requested point.
    /// </summary>
    public Scenario WithLeader(LeaderAnswer answer, TimeSpan after, double permitRadiusMeters)
    {
        _config = _config with { Leader = new ScriptedLeader(answer, after.TotalSeconds, permitRadiusMeters) };
        return this;
    }

    /// <summary>
    /// Sets when an agent asks for permission (the respecting route costs more than <paramref name="threshold"/> times
    /// the reference route) and how long it waits for an answer.
    /// </summary>
    public Scenario WithPermissionRules(double threshold, TimeSpan timeout)
    {
        _config = _config with { Permission = new PermissionRules(threshold, timeout.TotalSeconds) };
        return this;
    }

    /// <summary>Sets the random seed.</summary>
    public Scenario WithSeed(ulong seed)
    {
        _config = _config with { Seed = seed };
        return this;
    }

    /// <summary>Sets the tick rate.</summary>
    public Scenario WithTickRate(int hz)
    {
        _config = _config with { TickRateHz = hz };
        return this;
    }

    /// <summary>Creates the simulation, ready to receive orders. No ticks have run yet.</summary>
    public ScenarioRun Build()
    {
        var simulation = new Simulation(_terrain, _config);
        foreach (var (id, at, speed, profile) in _units)
        {
            if (profile is null)
            {
                simulation.AddUnit(id, at, speed);
            }
            else
            {
                simulation.AddUnit(id, at, profile);
            }
        }

        return new ScenarioRun(simulation);
    }
}
