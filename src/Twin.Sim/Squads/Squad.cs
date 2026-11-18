using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim.Squads;

/// <summary>
/// A squad in the simulation (backlog 0004): its members, who leads it and each of its teams now, and how it moves.
/// Formed with <see cref="Simulation.AddSquad"/>; <see cref="Systems.SquadSystem"/> carries commands through it and fills a
/// leader's place when the leader is out of action; <see cref="Systems.SquadCohesionSystem"/> and
/// <see cref="Systems.SquadFollowSystem"/> move it behind its leader.
/// </summary>
public sealed class Squad
{
    private readonly SortedDictionary<string, SquadMember> _members = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, SquadFormation> _memberFormations = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _detached = new(StringComparer.Ordinal);
    private readonly string?[] _teamLeaders;

    internal Squad(SquadDefinition definition, string leaderId, IReadOnlyList<string> teamLeaderIds)
    {
        Definition = definition with { Teams = [.. definition.Teams], Members = [.. definition.Members] };
        Id = definition.Id;
        Teams = Definition.Teams;
        Members = Definition.Members;
        Formation = definition.Formation;
        foreach (var member in Members)
        {
            _members.Add(member.UnitId, member);
            _memberFormations.Add(member.UnitId, definition.Formation);
        }

        LeaderId = leaderId;
        _teamLeaders = [.. teamLeaderIds];
        TeamTakeovers = new Takeover?[Teams.Count];
    }

    /// <summary>The formation the squad's leader has commanded last (members change to it as the command reaches them).</summary>
    public SquadFormation Formation { get; internal set; }

    /// <summary>
    /// True from the moment the leader learns of an enemy it did not know of until it is ordered
    /// <see cref="ResumeMovement"/> (backlog 0004 S5a1): the squad stands still, crouching, its movement order suspended.
    /// </summary>
    public bool IsHalted { get; internal set; }

    /// <summary>The leader's movement order suspended by the halt; taken up again on <see cref="ResumeMovement"/>.</summary>
    internal Order? SuspendedOrder { get; set; }

    /// <summary>Enemies the squad has halted for, or moved on knowing of: they do not halt it again.</summary>
    internal SortedSet<string> HaltedFor { get; } = new(StringComparer.Ordinal);

    /// <summary>Spot reports and relays about to be spoken, in the order they were scheduled.</summary>
    internal List<SpotReport> PendingReports { get; } = [];

    /// <summary>The definition the squad was formed from.</summary>
    internal SquadDefinition Definition { get; }

    /// <summary>True once the leader has had a movement order: from then on the members follow it in their slots.</summary>
    internal bool MovementStarted { get; set; }

    /// <summary>True while the leader waits for its squad (hysteresis between the straggler and regroup distances).</summary>
    internal bool LeaderHeld { get; set; }

    /// <summary>The path the leader has walked since the squad started moving, oldest first.</summary>
    internal List<Vec2> LeaderTrail { get; } = [];

    /// <summary>The leader the trail belongs to; a new leader starts a new trail.</summary>
    internal string? TrailLeaderId { get; set; }

    /// <summary>The corridor of the leader's current or last corridor order; null after a plain move order.</summary>
    internal Corridors.Corridor? Corridor { get; set; }

    /// <summary>The formation <paramref name="unitId"/> moves in: the last formation command it has acted on.</summary>
    /// <exception cref="KeyNotFoundException">The agent is not in this squad.</exception>
    public SquadFormation FormationOf(string unitId) =>
        _memberFormations.TryGetValue(unitId, out var formation) ? formation : throw new KeyNotFoundException($"'{unitId}' is not in squad '{Id}'.");

    /// <summary>
    /// True if <paramref name="unitId"/> is a member that follows the squad's movement; false for a member detached by any
    /// order given straight to it (until it is ordered to rejoin) and for an agent outside the squad.
    /// </summary>
    public bool IsFollowing(string unitId) => _members.ContainsKey(unitId) && !_detached.Contains(unitId);

    internal void SetMemberFormation(string unitId, SquadFormation formation) => _memberFormations[unitId] = formation;

    internal void Detach(string unitId) => _detached.Add(unitId);

    internal void Rejoin(string unitId) => _detached.Remove(unitId);

    /// <summary>The squad's id.</summary>
    public string Id { get; }

    /// <summary>The agent leading the squad now, or null while its place is empty (until a successor takes over).</summary>
    public string? LeaderId { get; internal set; }

    /// <summary>The team names.</summary>
    public IReadOnlyList<string> Teams { get; }

    /// <summary>The members, in squad order.</summary>
    public IReadOnlyList<SquadMember> Members { get; }

    /// <summary>Who leads each team now, in team order; null for a team whose leader's place is empty.</summary>
    public IReadOnlyList<string?> TeamLeaderIds => _teamLeaders;

    /// <summary>The successor waiting to take command, if the leader's place is empty.</summary>
    internal Takeover? LeaderTakeover { get; set; }

    /// <summary>The successor waiting to take over each team, in team order.</summary>
    internal Takeover?[] TeamTakeovers { get; }

    /// <summary>Commands on their way to members, in the order they were scheduled.</summary>
    internal List<Delivery> Deliveries { get; } = [];

    /// <summary>A member's role and team. (Its experience is its own as a fighter: <see cref="Combat.Combatant.Experience"/>.)</summary>
    /// <exception cref="KeyNotFoundException">The agent is not in this squad.</exception>
    public SquadMember Member(string unitId) =>
        _members.TryGetValue(unitId, out var member) ? member : throw new KeyNotFoundException($"'{unitId}' is not in squad '{Id}'.");

    /// <summary>True if the agent is in this squad.</summary>
    public bool Contains(string unitId) => _members.ContainsKey(unitId);

    /// <summary>The agent leading <paramref name="team"/> now, or null while its place is empty.</summary>
    /// <exception cref="KeyNotFoundException">The squad has no such team.</exception>
    public string? TeamLeaderId(string team) => _teamLeaders[TeamIndex(team)];

    internal int TeamIndex(string team)
    {
        for (var i = 0; i < Teams.Count; i++)
        {
            if (string.Equals(Teams[i], team, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"Squad '{Id}' has no team '{team}'.");
    }

    internal void SetTeamLeader(int team, string? unitId) => _teamLeaders[team] = unitId;

    /// <summary>A successor and when it takes over.</summary>
    internal sealed record Takeover(string UnitId, double Due);

    /// <summary>
    /// A command that reaches <paramref name="UnitId"/> at <paramref name="Due"/>. A team leader's delivery carries the
    /// members it passes the command on to (<paramref name="RelayTo"/>).
    /// </summary>
    internal sealed record Delivery(string UnitId, int CommandId, Order Command, double Due, IReadOnlyList<string> RelayTo);

    /// <summary>
    /// <paramref name="ReporterId"/> speaks a report of <paramref name="EnemyId"/> at <paramref name="EnemyPosition"/> at
    /// <paramref name="Due"/>: its own spotting, or a relay toward the leader (<paramref name="IsRelay"/>).
    /// </summary>
    internal sealed record SpotReport(string ReporterId, string EnemyId, Vec2 EnemyPosition, double Due, bool IsRelay);
}
