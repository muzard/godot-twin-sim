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

    /// <summary>Where the unit's eyes are: its position raised by the profile's eye height (0 without a profile).</summary>
    public Vec3 EyePosition => Position + new Vec3(0, 0, Profile?.EyeHeightMeters ?? 0);

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

    /// <summary>Convenience: true when <see cref="Activity"/> is <see cref="UnitActivity.Idle"/>.</summary>
    public bool IsIdle => Activity == UnitActivity.Idle;
}
