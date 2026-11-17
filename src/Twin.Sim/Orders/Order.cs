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
/// Halt where the unit is (backlog 0001). On the next tick the current order ends, including a
/// <see cref="FireAt"/>, so the unit stops shooting too. The unit keeps no current order and has zero velocity.
/// It becomes idle on that tick, or, if it is in a combat state (suppressed, pinned, changing posture), once that
/// state ends. Accepted for an idle unit, where it has no effect.
/// </summary>
public sealed record Stop : Order;

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

/// <summary>
/// A squad order (backlog 0004): the squad leader commands its squad to take a posture ("Maahan!"). Accepted only by the
/// leader of a squad. The leader takes the posture at once; every other member takes it when the command reaches it
/// (<see cref="Squads.SquadDoctrine"/>). Everyone keeps its current order.
/// </summary>
/// <param name="Posture">The posture.</param>
public sealed record SetSquadPosture(Posture Posture) : Order;
