using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Squads;
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

    /// <summary>Distance between squad members placed by <see cref="WithSquad"/>, in meters.</summary>
    public const double SquadSpacingMeters = 3.0;

    private readonly List<(string Id, Vec2 At, double SpeedMps, MovementProfile? Profile, Combatant? Combatant)> _units = [];
    private readonly List<SquadDefinition> _squads = [];
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
        _units.Add((id, at, speedMps, null, null));
        return this;
    }

    /// <summary>
    /// Adds an agent that moves by a movement profile: terrain-dependent speeds, slope limit and eye height.
    /// Use <see cref="SpecV01.Infantry"/> for spec v0.1 infantry.
    /// </summary>
    public Scenario WithUnit(string id, Vec2 at, MovementProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _units.Add((id, at, 0, profile, null));
        return this;
    }

    /// <summary>
    /// Adds a combatant: an agent with a movement profile, a side, rules of engagement and a weapon (backlog 0002).
    /// Use <see cref="SpecShooting.Rifle"/> for the rifle and set <see cref="WithCombatRules"/> to <see cref="SpecShooting.Rules"/>.
    /// </summary>
    public Scenario WithUnit(string id, Vec2 at, MovementProfile profile, Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(combatant);
        _units.Add((id, at, 0, profile, combatant));
        return this;
    }

    /// <summary>
    /// Starts a combat scenario (backlog 0002): spec v0.1 tick rate (dt = 0.5 s) and <see cref="SpecShooting.Rules"/>.
    /// The terrain still defaults to a 1 km flat map; set it with <see cref="WithTerrain"/>.
    /// </summary>
    public static Scenario ForCombat() => Create().WithTickRate(SpecV01.TickRateHz).WithCombatRules(SpecShooting.Rules);

    /// <summary>
    /// Adds a fighter: spec v0.1 infantry with the <see cref="SpecShooting.Rifle"/> (or <paramref name="weapon"/>),
    /// standing, on <paramref name="side"/> under <paramref name="rules"/>, a <see cref="Experience.Regular"/> soldier
    /// unless <paramref name="experience"/> says otherwise (backlog 0003).
    /// </summary>
    public Scenario WithFighter(
        string id, Vec2 at, Side side, RulesOfEngagement rules, WeaponProfile? weapon = null, string? unitId = null,
        Experience experience = Experience.Regular) =>
        WithUnit(id, at, SpecV01.Infantry, new Combatant(side, rules, weapon ?? SpecShooting.Rifle, unitId, experience));

    /// <summary>
    /// Adds a fire team of five in a wedge (kiila) with its point at <paramref name="apex"/>, heading
    /// <paramref name="facing"/> (any length). The arms trail back at 45° on both sides, <paramref name="spacingMeters"/>
    /// between neighbours: <c>{prefix}-1</c> at the point, <c>-2</c> and <c>-3</c> left and right behind it, <c>-4</c> and
    /// <c>-5</c> left and right at the rear. All are fighters with the rifle in the unit <paramref name="idPrefix"/>.
    /// </summary>
    public Scenario WithWedge(
        string idPrefix, Vec2 apex, Vec2 facing, double spacingMeters, Side side, RulesOfEngagement rules)
    {
        var forward = facing.Normalized();
        var left = new Vec2(-forward.Y, forward.X);
        var step = spacingMeters / Math.Sqrt(2);
        WithFighter($"{idPrefix}-1", apex, side, rules, unitId: idPrefix);
        WithFighter($"{idPrefix}-2", apex - (forward * step) + (left * step), side, rules, unitId: idPrefix);
        WithFighter($"{idPrefix}-3", apex - (forward * step) - (left * step), side, rules, unitId: idPrefix);
        WithFighter($"{idPrefix}-4", apex - (forward * 2 * step) + (left * 2 * step), side, rules, unitId: idPrefix);
        WithFighter($"{idPrefix}-5", apex - (forward * 2 * step) - (left * 2 * step), side, rules, unitId: idPrefix);
        return this;
    }

    /// <summary>
    /// Starts a squad scenario (backlog 0004): <see cref="ForCombat"/> plus the <see cref="SpecSquad.Doctrine"/> command
    /// delays. dt = 0.5 s, so every spec delay is a whole number of ticks.
    /// </summary>
    public static Scenario ForSquad() => ForCombat().WithSquadDoctrine(SpecSquad.Doctrine);

    /// <summary>
    /// Starts a spotting scenario (backlog 0004 S5a1): <see cref="ForCombat"/> with <see cref="SpecSpotting.Rules"/>, so
    /// seeing an enemy takes time and depends on where the observer watches. Turn an observer with the
    /// <see cref="Twin.Sim.Orders.Watch"/> order.
    /// </summary>
    public static Scenario ForSpotting() => ForCombat().WithCombatRules(SpecSpotting.Rules);

    /// <summary>
    /// Starts a squad spotting scenario (backlog 0004 S5a1): <see cref="ForSquad"/> with <see cref="SpecSpotting.Rules"/>
    /// and <see cref="SpecSpotting.Doctrine"/> (watch sectors, voice range).
    /// </summary>
    public static Scenario ForSquadSpotting() =>
        ForSquad().WithCombatRules(SpecSpotting.Rules).WithSquadDoctrine(SpecSpotting.Doctrine);

    /// <summary>
    /// Places every member of <paramref name="squad"/> as a fighter (as <see cref="WithFighter"/>) with its experience,
    /// and forms the squad from them when the scenario is built. Members stand in a row, <see cref="SquadSpacingMeters"/>
    /// apart, eastward from <paramref name="at"/> in member order. They hold fire unless <paramref name="rules"/> says
    /// otherwise, so a test's shooting stays under its own control.
    /// </summary>
    public Scenario WithSquad(
        SquadSpec squad, Vec2 at, Side side = Side.Blue, RulesOfEngagement rules = RulesOfEngagement.HoldFire)
    {
        ArgumentNullException.ThrowIfNull(squad);
        for (var i = 0; i < squad.Members.Count; i++)
        {
            var member = squad.Members[i];
            WithFighter(member.UnitId, at + new Vec2(i * SquadSpacingMeters, 0), side, rules, experience: member.Experience);
        }

        _squads.Add(squad.Definition);
        return this;
    }

    /// <summary>Sets the squad command delays. Tests pass <see cref="SpecSquad.Doctrine"/>.</summary>
    public Scenario WithSquadDoctrine(SquadDoctrine doctrine)
    {
        _config = _config with { SquadDoctrine = doctrine ?? throw new ArgumentNullException(nameof(doctrine)) };
        return this;
    }

    /// <summary>Sets the rules of line of sight, hits, posture and suppression. Tests pass <see cref="SpecShooting.Rules"/>.</summary>
    public Scenario WithCombatRules(CombatRules rules)
    {
        _config = _config with { Combat = rules ?? throw new ArgumentNullException(nameof(rules)) };
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
        foreach (var (id, at, speed, profile, combatant) in _units)
        {
            if (profile is null)
            {
                simulation.AddUnit(id, at, speed);
            }
            else if (combatant is null)
            {
                simulation.AddUnit(id, at, profile);
            }
            else
            {
                simulation.AddUnit(id, at, profile, combatant);
            }
        }

        foreach (var squad in _squads)
        {
            simulation.AddSquad(squad);
        }

        return new ScenarioRun(simulation);
    }
}
