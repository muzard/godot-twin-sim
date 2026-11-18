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

/// <summary>
/// A squad order (backlog 0004, S2a): the squad leader commands a formation. Accepted only by the leader of a squad. The
/// squad's formation changes at once; each member takes its new slot when the command reaches it. The leader keeps its
/// current order.
/// </summary>
/// <param name="Formation">The formation.</param>
public sealed record SetSquadFormation(Squads.SquadFormation Formation) : Order;

/// <summary>
/// Back to the squad (backlog 0004, S2a): a member detached from its squad's movement by an order given straight to it
/// (any order but this one) ends that order and follows the squad again. Rejected for a squad leader and for an agent
/// outside a squad; accepted for a member already following, where it changes nothing.
/// </summary>
public sealed record Rejoin : Order;

/// <summary>
/// Watch toward a direction (tähystä suuntaan; backlog 0004 S5a1): the agent stops, as with <see cref="Stop"/>, and
/// watches <paramref name="Direction"/> (any length) until something else turns it. Rejected for a zero direction.
/// </summary>
/// <param name="Direction">Where to watch, in map coordinates.</param>
public sealed record Watch(Vec2 Direction) : Order;

/// <summary>
/// A squad order (backlog 0004 S5a1): a squad halted on a spotted enemy moves on. The leader takes up its suspended
/// movement order; every member learns of the enemies the leader knows and takes back the posture it had before the
/// halt. Accepted only by the leader of a squad; changes nothing for a squad that is not halted.
/// </summary>
public sealed record ResumeMovement : Order;

/// <summary>
/// A squad order (backlog 0004 S5b): the squad leader distributes the squad's fire over the enemies it knows of. Its front
/// spans the bearings to them, widened on each side (<see cref="Squads.SquadDoctrine.FrontMarginDegrees"/>); each team
/// gets an equal share left to right, widened so neighbours overlap, and the machine gunner the whole front. Each member
/// takes its fire area when the command reaches it. The leader gives it on its own when it learns of a new enemy; given
/// from outside it distributes fire the same way. Accepted only by the leader of a squad; the leader keeps its order.
/// </summary>
public sealed record DirectSquadFire : Order;

/// <summary>
/// A squad order (backlog 0004 S5b): the squad leader ends the fire distribution. Members drop their fire areas when the
/// command reaches them and fire only back at their own attackers until a new distribution. Accepted only by the leader
/// of a squad; the leader keeps its order.
/// </summary>
public sealed record SquadCeaseFire : Order;
