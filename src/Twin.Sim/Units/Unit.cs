using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;

namespace Twin.Sim.Units;

/// <summary>What a unit is doing right now.</summary>
public enum UnitActivity
{
    /// <summary>Not executing any order.</summary>
    Idle,

    /// <summary>Moving toward a destination.</summary>
    Moving,

    /// <summary>Standing still, waiting for the leader's answer to a permission request.</summary>
    WaitingForPermission,

    /// <summary>Stopped: the order cannot be carried out without crossing a boundary it may not cross.</summary>
    Blocked,

    /// <summary>Under fire: prone, not moving on, shooting less accurately (backlog 0002).</summary>
    Suppressed,

    /// <summary>Just hit: completely pinned, prone, for the pinned time.</summary>
    Pinned,

    /// <summary>Hit twice: does nothing more.</summary>
    OutOfAction,

    /// <summary>Changing posture: neither moving nor shooting.</summary>
    ChangingPosture,

    /// <summary>
    /// Reserved for casualty care: treating or carrying a wounded team-mate. Nothing sets it yet (backlog 0002).
    /// </summary>
    GivingAid,

    /// <summary>
    /// A squad leader holding its movement order until its squad is with it: a member is far from its slot, pinned or
    /// out of action (backlog 0004, S2a).
    /// </summary>
    WaitingForSquad,
}

/// <summary>
/// A simulated agent on the map. State is changed only by the simulation's systems;
/// everything outside the simulation sees it read-only.
/// </summary>
public sealed class Unit
{
    internal Unit(string id, Vec3 position, MovementProfile profile)
        : this(id, position, profile.MaxBaseSpeed)
    {
        Profile = profile;
    }

    internal Unit(string id, Vec3 position, MovementProfile profile, Combatant combatant, CombatRules rules)
        : this(id, position, profile)
    {
        Combatant = combatant;
        CombatRules = rules;
        RoundsLeft = combatant.Weapon.Rounds;
    }

    internal Unit(string id, Vec3 position, double maxSpeedMps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeedMps);
        Id = id;
        Position = position;
        MaxSpeedMps = maxSpeedMps;
    }

    /// <summary>Unique, stable identifier.</summary>
    public string Id { get; }

    /// <summary>Position in meters; Z is the ground height for ground units.</summary>
    public Vec3 Position { get; internal set; }

    /// <summary>
    /// Where the unit's eyes are: its position raised by the posture's eye height for a fighter, otherwise by the
    /// profile's eye height (0 without a profile).
    /// </summary>
    public Vec3 EyePosition => Position + new Vec3(0, 0,
        CombatRules is { } rules ? rules.For(Posture).EyeHeight : Profile?.EyeHeightMeters ?? 0);

    /// <summary>The point others aim at: the position raised by the posture's aim height.</summary>
    public Vec3 AimPoint => Position + new Vec3(0, 0, CombatRules?.For(Posture).AimHeight ?? 0);

    /// <summary>Side, rules of engagement and weapon, or null for a unit that does not fight.</summary>
    public Combatant? Combatant { get; }

    /// <summary>The fire team or squad this agent belongs to (not its own <see cref="Id"/>), or null.</summary>
    public string? UnitId => Combatant?.UnitId;

    /// <summary>Health; always <see cref="Health.Unhurt"/> for a unit that does not fight.</summary>
    public Health Health { get; internal set; } = Health.Unhurt;

    /// <summary>Current posture.</summary>
    public Posture Posture { get; internal set; } = Posture.Standing;

    /// <summary>
    /// True while the unit is suppressed (backlog 0003): from the moment its fire pressure reaches its threshold τ until
    /// the pressure falls below the release fraction of τ (<see cref="SuppressionRules"/>).
    /// </summary>
    public bool IsSuppressed { get; internal set; }

    /// <summary>
    /// True while the unit is heavily suppressed: pressure at the heavy factor times its threshold
    /// (<see cref="SuppressionRules"/>). It does not fire at all.
    /// </summary>
    public bool IsHeavilySuppressed { get; internal set; }

    /// <summary>
    /// The fire pressure on the unit (backlog 0003): rounds passing close add to it, and it halves every half-life of
    /// the unit's experience.
    /// </summary>
    public double SuppressionPressure { get; internal set; }

    /// <summary>Where the unit is in the immediate action drill under fire (backlog 0003).</summary>
    public ContactDrillStage DrillStage { get; internal set; }

    /// <summary>True from the unit's contact report until a new order ends its drill.</summary>
    public bool IsAwaitingOrders { get; internal set; }

    /// <summary>Rounds left for the weapon.</summary>
    public int RoundsLeft { get; internal set; }

    /// <summary>
    /// Where the unit watches (backlog 0004 S5a1), unit length: toward the nearest attacker in the drill under fire,
    /// toward the last known position of an enemy it has just learned of while it stands still, the direction of its last
    /// <see cref="Watch"/> order, the centre of its first S2c observation sector while it follows a squad, otherwise its
    /// direction of movement. North at first.
    /// </summary>
    public Vec2 WatchDirection { get; internal set; } = InitialWatchDirection;

    /// <summary>Where every unit watches when it is placed: north.</summary>
    public static Vec2 InitialWatchDirection { get; } = new(0, 1);

    /// <summary>
    /// How the unit moves on terrain, or null for a unit that moves at <see cref="MaxSpeedMps"/> everywhere.
    /// A plain <see cref="MoveTo"/> ignores the profile and moves straight at <see cref="MaxSpeedMps"/>; use
    /// <see cref="MoveThroughCorridor"/> for terrain-aware movement.
    /// </summary>
    public MovementProfile? Profile { get; }

    /// <summary>Velocity in meters per second at the end of the last tick.</summary>
    public Vec3 Velocity { get; internal set; }

    /// <summary>Maximum ground speed in meters per second.</summary>
    public double MaxSpeedMps { get; }

    /// <summary>The order being executed, or null when idle.</summary>
    public Order? CurrentOrder { get; internal set; }

    /// <summary>What the unit is currently doing.</summary>
    public UnitActivity Activity { get; internal set; } = UnitActivity.Idle;

    /// <summary>
    /// The way the unit faces, a unit vector in map coordinates (backlog 0004, S2c). It starts facing north. A squad member
    /// following its squad faces the centre of its first observation sector; any other unit faces the way it last moved.
    /// </summary>
    public Vec2 Heading { get; internal set; } = new(0, 1);

    /// <summary>
    /// The directions the unit observes (backlog 0004, S2c): set for members of a moving or deployed squad, empty for
    /// anyone else. Data only for now: seeing does not depend on them until the observation model arrives.
    /// </summary>
    public IReadOnlyList<ObservationSector> ObservationSectors { get; internal set; } = [];

    /// <summary>True if the unit also watches the air (backlog 0004, S2c; data only, as the sectors).</summary>
    public bool WatchesAir { get; internal set; }

    /// <summary>True if one of <see cref="ObservationSectors"/> covers the map direction <paramref name="direction"/>.</summary>
    public bool Observes(Vec2 direction) => ObservationSectors.Any(s => s.Covers(direction));

    /// <summary>Progress through a corridor order, owned by the corridor movement system.</summary>
    internal Systems.CorridorProgress? CorridorProgress { get; set; }

    internal CombatRules? CombatRules { get; }

    /// <summary>The posture the unit wants; forced prone while pinned or suppressed, back to this afterwards.</summary>
    internal Posture DesiredPosture { get; set; } = Posture.Standing;

    /// <summary>When the posture change in progress completes; null when none is.</summary>
    internal double? PostureChangeUntil { get; set; }

    internal double PinnedUntil { get; set; }

    internal double NextShotTime { get; set; }

    internal double FlashUntil { get; set; } = double.NegativeInfinity;

    /// <summary>Agents that have shot at this one, in the order they first did (the duty to return fire).</summary>
    internal List<string> Attackers { get; } = [];

    /// <summary>
    /// Attackers already answered, by a drill under fire or by an order the leader gave while they were firing: their
    /// fire does not start the drill again (backlog 0003, decision of 2026-11-17).
    /// </summary>
    internal List<string> KnownAttackers { get; } = [];

    /// <summary>The drill under fire: when it started, its snap shots, its suspended order and where it is heading.</summary>
    internal ContactDrill? Drill { get; set; }

    /// <summary>Spotting progress toward each enemy in sight but not yet spotted, 0..1 (backlog 0004 S5a1).</summary>
    internal SortedDictionary<string, double> SpotProgress { get; } = new(StringComparer.Ordinal);

    /// <summary>Enemies the unit has spotted itself: only these does it fire at.</summary>
    internal SortedSet<string> Spotted { get; } = new(StringComparer.Ordinal);

    /// <summary>Enemies the unit knows of, spotted or reported, with their last known position.</summary>
    internal SortedDictionary<string, Vec2> KnownEnemies { get; } = new(StringComparer.Ordinal);

    /// <summary>Enemies the unit has reported or relayed: a member reports an enemy once.</summary>
    internal SortedSet<string> Reported { get; } = new(StringComparer.Ordinal);

    /// <summary>The last known position of the enemy the unit learned of last; it watches there until it moves on.</summary>
    internal Vec2? WatchTarget { get; set; }

    /// <summary>
    /// True while the unit watches its S2c observation sectors (a squad member following its squad): it spots by the best
    /// of their centres, not by <see cref="WatchDirection"/> alone.
    /// </summary>
    internal bool WatchesSectors { get; set; }

    /// <summary>True from a <see cref="Watch"/> order until the next order: the unit keeps that direction.</summary>
    internal bool HoldsWatch { get; set; }

    /// <summary>True while a squad member that knows of an enemy stands still, crouching, until the squad moves on.</summary>
    internal bool IsAlerted { get; set; }

    /// <summary>The posture the unit wanted before it was alerted; taken back when its squad moves on.</summary>
    internal Posture? PostureBeforeAlert { get; set; }

    /// <summary>The activity to return to when a combat state (suppressed, pinned, changing posture) ends.</summary>
    internal UnitActivity ActivityBeforeCombat { get; set; }

    /// <summary>True while the unit cannot move: pinned, out of action, suppressed or changing posture.</summary>
    internal bool IsImmobilized =>
        Health is Health.Pinned or Health.OutOfAction || IsSuppressed || PostureChangeUntil is not null;

    /// <summary>
    /// True while the unit leads a squad that is not with it: its movement order is kept but it does not move
    /// (backlog 0004). Set by <see cref="Systems.SquadCohesionSystem"/>.
    /// </summary>
    internal bool HeldBySquad { get; set; }

    /// <summary>Multiplier on walking speed for a squad leader, so its members can keep up (1 for everyone else).</summary>
    internal double SquadSpeedFactor { get; set; } = 1;

    /// <summary>Multiplier on walking speed from posture, wounds and leading a squad.</summary>
    internal double SpeedFactor => SquadSpeedFactor * (CombatRules is { } rules
        ? rules.For(Posture).SpeedFactor * (Health == Health.Wounded ? rules.WoundedSpeedFactor : 1)
        : 1);

    /// <summary>Convenience: true when <see cref="Activity"/> is <see cref="UnitActivity.Idle"/>.</summary>
    public bool IsIdle => Activity == UnitActivity.Idle;
}
