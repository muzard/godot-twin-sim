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

    /// <summary>True while a shot at the unit keeps it suppressed.</summary>
    public bool IsSuppressed { get; internal set; }

    /// <summary>Rounds left for the weapon.</summary>
    public int RoundsLeft { get; internal set; }

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

    /// <summary>Progress through a corridor order, owned by the corridor movement system.</summary>
    internal Systems.CorridorProgress? CorridorProgress { get; set; }

    internal CombatRules? CombatRules { get; }

    /// <summary>The posture the unit wants; forced prone while pinned or suppressed, back to this afterwards.</summary>
    internal Posture DesiredPosture { get; set; } = Posture.Standing;

    /// <summary>When the posture change in progress completes; null when none is.</summary>
    internal double? PostureChangeUntil { get; set; }

    internal double SuppressedUntil { get; set; }

    internal double PinnedUntil { get; set; }

    internal double NextShotTime { get; set; }

    internal double FlashUntil { get; set; } = double.NegativeInfinity;

    /// <summary>Agents that have shot at this one, in the order they first did (the duty to return fire).</summary>
    internal List<string> Attackers { get; } = [];

    /// <summary>The activity to return to when a combat state (suppressed, pinned, changing posture) ends.</summary>
    internal UnitActivity ActivityBeforeCombat { get; set; }

    /// <summary>True while the unit cannot move: pinned, out of action, suppressed or changing posture.</summary>
    internal bool IsImmobilized =>
        Health is Health.Pinned or Health.OutOfAction || IsSuppressed || PostureChangeUntil is not null;

    /// <summary>Multiplier on walking speed from posture and wounds.</summary>
    internal double SpeedFactor => CombatRules is { } rules
        ? rules.For(Posture).SpeedFactor * (Health == Health.Wounded ? rules.WoundedSpeedFactor : 1)
        : 1;

    /// <summary>Convenience: true when <see cref="Activity"/> is <see cref="UnitActivity.Idle"/>.</summary>
    public bool IsIdle => Activity == UnitActivity.Idle;
}
