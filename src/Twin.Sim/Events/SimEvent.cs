using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Units;

namespace Twin.Sim.Events;

/// <summary>
/// Something that happened in the simulation, in the event log (spec v0.1, "Tapahtumaloki"). The log is enough to
/// rebuild every agent's state at any step (<see cref="EventReplay"/>), so viewers and metrics never need the core's
/// internal data structures.
/// </summary>
/// <param name="Time">Simulated time in seconds at the end of the step that produced the event.</param>
/// <param name="AgentId">The agent the event is about.</param>
public abstract record SimEvent(double Time, string AgentId);

/// <summary>An agent was placed on the map, idle.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Position">Where it stands, on the ground.</param>
public sealed record AgentPlaced(double Time, string AgentId, Vec3 Position) : SimEvent(Time, AgentId);

/// <summary>An agent's position changed during a step.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Position">Its position at the end of the step.</param>
public sealed record AgentMoved(double Time, string AgentId, Vec3 Position) : SimEvent(Time, AgentId);

/// <summary>An agent's activity changed during a step.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Activity">Its activity at the end of the step.</param>
public sealed record ActivityChanged(double Time, string AgentId, UnitActivity Activity) : SimEvent(Time, AgentId);

/// <summary>An agent planned the route it is going to follow.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Waypoints">Cell centers of the route.</param>
/// <param name="CostSeconds">Planned travel time including corridor penalties.</param>
public sealed record PathPlanned(double Time, string AgentId, IReadOnlyList<Vec2> Waypoints, double CostSeconds) : SimEvent(Time, AgentId);

/// <summary>An agent asked its leader for permission to cross a hard boundary, and waits in place.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="BoundaryId">The boundary.</param>
/// <param name="At">The crossing point on the reference route.</param>
public sealed record PermissionRequested(double Time, string AgentId, string BoundaryId, Vec2 At) : SimEvent(Time, AgentId);

/// <summary>The leader answered a permission request.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent who asked.</param>
/// <param name="BoundaryId">The boundary.</param>
/// <param name="Answer">Grant or deny.</param>
public sealed record PermissionAnswered(double Time, string AgentId, string BoundaryId, LeaderAnswer Answer) : SimEvent(Time, AgentId);

/// <summary>The agent stopped waiting for an answer.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="BoundaryId">The boundary it asked about.</param>
public sealed record PermissionTimedOut(double Time, string AgentId, string BoundaryId) : SimEvent(Time, AgentId);

/// <summary>The agent reached its destination.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Position">Where it stopped.</param>
public sealed record Arrived(double Time, string AgentId, Vec2 Position) : SimEvent(Time, AgentId);

/// <summary>The agent cannot go on and stays where it is.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Reason">Why, in words.</param>
public sealed record Blocked(double Time, string AgentId, string Reason) : SimEvent(Time, AgentId);

/// <summary>An agent fired a shot.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The shooter.</param>
/// <param name="TargetId">The agent it shot at.</param>
/// <param name="Hit">True if the shot hit.</param>
public sealed record ShotFired(double Time, string AgentId, string TargetId, bool Hit) : SimEvent(Time, AgentId);

/// <summary>
/// An agent heard a shot. The event records where the shooter was; v0.1 agents do not yet act on it (only on what
/// they see).
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The listener.</param>
/// <param name="ShooterId">The shooter.</param>
/// <param name="ShooterPosition">Where the shooter was.</param>
public sealed record ShotHeard(double Time, string AgentId, string ShooterId, Vec2 ShooterPosition) : SimEvent(Time, AgentId);

/// <summary>An agent's health changed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Health">The new health.</param>
public sealed record HealthChanged(double Time, string AgentId, Health Health) : SimEvent(Time, AgentId);

/// <summary>
/// A hit wounded an agent or put it out of action: the hook for casualty care, which is not implemented yet.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The casualty.</param>
/// <param name="Position">Where it lies.</param>
/// <param name="Health">Its health after the hit.</param>
public sealed record CasualtyOccurred(double Time, string AgentId, Vec2 Position, Health Health) : SimEvent(Time, AgentId);

/// <summary>A shot at an agent suppressed it, or extended its suppression.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Until">When the suppression ends unless extended.</param>
public sealed record SuppressionStarted(double Time, string AgentId, double Until) : SimEvent(Time, AgentId);

/// <summary>An agent is no longer suppressed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
public sealed record SuppressionEnded(double Time, string AgentId) : SimEvent(Time, AgentId);

/// <summary>An agent's posture changed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Posture">The new posture.</param>
public sealed record PostureChanged(double Time, string AgentId, Posture Posture) : SimEvent(Time, AgentId);

/// <summary>One agent in a <see cref="WorldSnapshot"/>.</summary>
/// <param name="Id">The agent.</param>
/// <param name="Position">Its position.</param>
/// <param name="Activity">What it is doing.</param>
/// <param name="Health">Its health.</param>
/// <param name="Posture">Its posture.</param>
/// <param name="Suppressed">True while it is suppressed.</param>
public readonly record struct AgentSnapshot(
    string Id, Vec3 Position, UnitActivity Activity, Health Health = Health.Unhurt, Posture Posture = Posture.Standing,
    bool Suppressed = false);

/// <summary>The state viewers show: every agent at one moment, ordered by id.</summary>
/// <param name="TimeSeconds">Simulated time.</param>
/// <param name="Agents">The agents, ordered by id.</param>
public sealed record WorldSnapshot(double TimeSeconds, IReadOnlyList<AgentSnapshot> Agents);
