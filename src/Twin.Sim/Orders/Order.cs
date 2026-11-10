using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;

namespace Twin.Sim.Orders;

/// <summary>
/// Base type for everything a unit can be told to do. Orders are immutable data.
/// They are validated when issued and take effect at the start of the next tick.
/// </summary>
public abstract record Order;

/// <summary>Move along the ground to a map position. Height follows the terrain.</summary>
/// <param name="Destination">Target map position in meters.</param>
public sealed record MoveTo(Vec2 Destination) : Order;

/// <summary>
/// Move through a corridor to its last centerline point (spec v0.1): plan a route that respects the corridor and its
/// hard boundaries, ask the leader for permission when the boundaries make the route too long, then follow it.
/// </summary>
/// <param name="Corridor">The corridor; the destination is its last centerline point.</param>
public sealed record MoveThroughCorridor(Corridor Corridor) : Order
{
    /// <summary>The agent's destination B.</summary>
    public Vec2 Destination => Corridor.Centerline[^1];
}

/// <summary>
/// Shoot at an enemy while it is visible and in range, until it is out of action or the order is replaced
/// (backlog 0002). Rejected for an unknown target, oneself or an agent on one's own side.
/// </summary>
/// <param name="TargetId">The enemy.</param>
public sealed record FireAt(string TargetId) : Order;

/// <summary>Ends a <see cref="FireAt"/> order. Any other current order is kept.</summary>
public sealed record CeaseFire : Order;

/// <summary>
/// Take a posture. Takes the rules' posture-change time, during which the agent neither moves nor shoots.
/// The agent keeps its current order.
/// </summary>
/// <param name="Posture">The posture.</param>
public sealed record SetPosture(Posture Posture) : Order;
