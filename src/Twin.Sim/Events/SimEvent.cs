using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
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

/// <summary>
/// An agent asked its leader for permission to cross a hard boundary, and waits in place. A squad leader asks its platoon
/// leader (the scripted leader); a squad member asks its squad leader, who passes the request on (backlog 0004, S2b).
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="BoundaryId">The boundary.</param>
/// <param name="At">The crossing point: on the agent's reference route, or for a squad member on its way to its slot.</param>
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
/// <param name="Mode">Aimed or snap (backlog 0003).</param>
/// <param name="Rounds">Rounds in the shot: 1 for a rifle, the burst for a machine gun.</param>
public sealed record ShotFired(
    double Time, string AgentId, string TargetId, bool Hit, FireMode Mode = FireMode.Aimed, int Rounds = 1) : SimEvent(Time, AgentId);

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

/// <summary>
/// An agent's fire pressure reached its suppression threshold, or its suppression changed between plain and heavy
/// (backlog 0003). It lasts until <see cref="SuppressionEnded"/>; when is not known in advance.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Heavy">True if it is heavily suppressed: it does not fire at all.</param>
public sealed record SuppressionStarted(double Time, string AgentId, bool Heavy) : SimEvent(Time, AgentId);

/// <summary>An agent is no longer suppressed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
public sealed record SuppressionEnded(double Time, string AgentId) : SimEvent(Time, AgentId);

/// <summary>An agent's posture changed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Posture">The new posture.</param>
public sealed record PostureChanged(double Time, string AgentId, Posture Posture) : SimEvent(Time, AgentId);

/// <summary>
/// An agent entered a stage of the immediate action drill under fire (backlog 0003); <see cref="ContactDrillStage.None"/>
/// when the drill ends.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="Stage">The new stage.</param>
public sealed record ContactDrillStageChanged(double Time, string AgentId, ContactDrillStage Stage) : SimEvent(Time, AgentId);

/// <summary>
/// An agent under fire reported contact to its leader (backlog 0003). From now on it waits for orders, until a new order
/// ends its drill.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The agent.</param>
/// <param name="AttackerId">The enemy it reports.</param>
/// <param name="AttackerPosition">Where that enemy is.</param>
public sealed record ContactReported(double Time, string AgentId, string AttackerId, Vec2 AttackerPosition) : SimEvent(Time, AgentId);

/// <summary>A squad was formed (backlog 0004).</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The squad's leader.</param>
/// <param name="SquadId">The squad.</param>
/// <param name="Teams">Its team names, in formation order.</param>
/// <param name="TeamLeaderIds">Each team's leader, in team order.</param>
/// <param name="Formation">The formation it starts in (S2a).</param>
public sealed record SquadFormed(
    double Time, string AgentId, string SquadId, IReadOnlyList<string> Teams, IReadOnlyList<string> TeamLeaderIds,
    SquadFormation Formation = SquadFormation.File)
    : SimEvent(Time, AgentId);

/// <summary>A squad's leader commanded a formation (backlog 0004, S2a); members take it as the command reaches them.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The leader.</param>
/// <param name="SquadId">The squad.</param>
/// <param name="Formation">The new formation.</param>
public sealed record FormationChanged(double Time, string AgentId, string SquadId, SquadFormation Formation) : SimEvent(Time, AgentId);

/// <summary>A squad's leader commanded its squad (backlog 0004). Members act on it later, each logging <see cref="CommandReceived"/>.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The leader.</param>
/// <param name="SquadId">The squad.</param>
/// <param name="CommandId">Identifies the command in <see cref="CommandReceived"/>; unique in the run.</param>
/// <param name="Command">The command, in words.</param>
public sealed record CommandIssued(double Time, string AgentId, string SquadId, int CommandId, string Command) : SimEvent(Time, AgentId);

/// <summary>A squad member acted on a command: it reached the member and the member's reaction time passed.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The member.</param>
/// <param name="CommandId">The command (<see cref="CommandIssued.CommandId"/>).</param>
public sealed record CommandReceived(double Time, string AgentId, int CommandId) : SimEvent(Time, AgentId);

/// <summary>
/// A successor took command of a squad whose leader was out of action. (The leader's place is empty from the moment the
/// leader is out of action, which <see cref="HealthChanged"/> logs.)
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The new leader.</param>
/// <param name="SquadId">The squad.</param>
public sealed record LeaderChanged(double Time, string AgentId, string SquadId) : SimEvent(Time, AgentId);

/// <summary>A successor took over a team whose leader was out of action.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The new team leader.</param>
/// <param name="SquadId">The squad.</param>
/// <param name="Team">The team.</param>
public sealed record TeamLeaderChanged(double Time, string AgentId, string SquadId, string Team) : SimEvent(Time, AgentId);

/// <summary>An agent spotted an enemy (backlog 0004 S5a1): from now on it knows of it and may fire at it.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The observer.</param>
/// <param name="EnemyId">The enemy.</param>
/// <param name="EnemyPosition">Where the enemy is.</param>
public sealed record EnemySpotted(double Time, string AgentId, string EnemyId, Vec2 EnemyPosition) : SimEvent(Time, AgentId);

/// <summary>
/// A squad member spoke a spot report (backlog 0004 S5a1): its own spotting, or a relay toward its leader. Squad mates in
/// range who did not know of the enemy log <see cref="SpotReceived"/>.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The member speaking.</param>
/// <param name="EnemyId">The enemy.</param>
/// <param name="EnemyPosition">Where the enemy was spotted.</param>
/// <param name="Method">How the report is passed on.</param>
public sealed record SpotReported(double Time, string AgentId, string EnemyId, Vec2 EnemyPosition, ReportMethod Method)
    : SimEvent(Time, AgentId);

/// <summary>A squad member learned of an enemy from a squad mate's report, or from its leader when the squad moved on.</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The member who learned of it.</param>
/// <param name="ReporterId">Who told it.</param>
/// <param name="EnemyId">The enemy.</param>
/// <param name="EnemyPosition">Where the enemy was reported.</param>
public sealed record SpotReceived(double Time, string AgentId, string ReporterId, string EnemyId, Vec2 EnemyPosition)
    : SimEvent(Time, AgentId);

/// <summary>A squad halted because its leader learned of an enemy (backlog 0004 S5a1).</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The leader.</param>
/// <param name="SquadId">The squad.</param>
/// <param name="EnemyId">The enemy that halted it.</param>
public sealed record SquadHalted(double Time, string AgentId, string SquadId, string EnemyId) : SimEvent(Time, AgentId);

/// <summary>A halted squad moved on on its leader's <see cref="Orders.ResumeMovement"/> order (backlog 0004 S5a1).</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The leader.</param>
/// <param name="SquadId">The squad.</param>
public sealed record SquadResumed(double Time, string AgentId, string SquadId) : SimEvent(Time, AgentId);

/// <summary>
/// A new squad leader that did not hear its predecessor's movement order asked the platoon leader for it (backlog 0004,
/// S2b).
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The new leader.</param>
/// <param name="SquadId">The squad.</param>
public sealed record OrderRequested(double Time, string AgentId, string SquadId) : SimEvent(Time, AgentId);

/// <summary>The platoon leader repeated the squad's movement order to its new leader (backlog 0004, S2b).</summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The new leader.</param>
/// <param name="SquadId">The squad.</param>
public sealed record OrderRepeated(double Time, string AgentId, string SquadId) : SimEvent(Time, AgentId);

/// <summary>
/// The squad has deployed in its open line at the forest edge (backlog 0004, S2c): every able member stands in its spot.
/// Its leader asks to leave the forest on the same step.
/// </summary>
/// <param name="Time">Simulated time.</param>
/// <param name="AgentId">The squad leader.</param>
/// <param name="SquadId">The squad.</param>
public sealed record EdgeDeployed(double Time, string AgentId, string SquadId) : SimEvent(Time, AgentId);

/// <summary>One squad in a <see cref="WorldSnapshot"/>.</summary>
/// <param name="Id">The squad.</param>
/// <param name="LeaderId">Its leader, or null while the place is empty.</param>
/// <param name="TeamLeaderIds">Each team's leader in team order, null while the place is empty.</param>
/// <param name="Formation">The formation its leader commanded last.</param>
/// <param name="Halted">True while it is halted on a spotted enemy (backlog 0004 S5a1).</param>
public sealed record SquadSnapshot(
    string Id, string? LeaderId, IReadOnlyList<string?> TeamLeaderIds, SquadFormation Formation = SquadFormation.File,
    bool Halted = false);

/// <summary>An enemy an agent knows of, in a <see cref="WorldSnapshot"/> (backlog 0004 S5a1).</summary>
/// <param name="AgentId">The agent.</param>
/// <param name="EnemyId">The enemy.</param>
/// <param name="Position">Its last known position: where it was spotted or reported.</param>
public sealed record KnownEnemySnapshot(string AgentId, string EnemyId, Vec2 Position);

/// <summary>One agent in a <see cref="WorldSnapshot"/>.</summary>
/// <param name="Id">The agent.</param>
/// <param name="Position">Its position.</param>
/// <param name="Activity">What it is doing.</param>
/// <param name="Health">Its health.</param>
/// <param name="Posture">Its posture.</param>
/// <param name="Suppressed">True while it is suppressed.</param>
/// <param name="HeavilySuppressed">True while it is heavily suppressed.</param>
/// <param name="DrillStage">Its stage of the drill under fire.</param>
/// <param name="AwaitingOrders">True from its contact report until a new order.</param>
public readonly record struct AgentSnapshot(
    string Id, Vec3 Position, UnitActivity Activity, Health Health = Health.Unhurt, Posture Posture = Posture.Standing,
    bool Suppressed = false, bool HeavilySuppressed = false, ContactDrillStage DrillStage = ContactDrillStage.None,
    bool AwaitingOrders = false);

/// <summary>The state viewers show: every agent at one moment, ordered by id.</summary>
/// <param name="TimeSeconds">Simulated time.</param>
/// <param name="Agents">The agents, ordered by id.</param>
public sealed record WorldSnapshot(double TimeSeconds, IReadOnlyList<AgentSnapshot> Agents)
{
    /// <summary>Every squad and who leads it and its teams, ordered by id (backlog 0004).</summary>
    public IReadOnlyList<SquadSnapshot> Squads { get; init; } = [];

    /// <summary>Every enemy each agent knows of, ordered by agent and then enemy (backlog 0004 S5a1).</summary>
    public IReadOnlyList<KnownEnemySnapshot> KnownEnemies { get; init; } = [];
}
