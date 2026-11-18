using Twin.Sim.Orders;

namespace Twin.Sim.Squads;

/// <summary>
/// A squad in the simulation (backlog 0004): its members and who leads it and each of its teams now. Formed with
/// <see cref="Simulation.AddSquad"/>; <see cref="Systems.SquadSystem"/> carries commands through it and fills a leader's
/// place when the leader is out of action.
/// </summary>
public sealed class Squad
{
    private readonly SortedDictionary<string, SquadMember> _members = new(StringComparer.Ordinal);
    private readonly string?[] _teamLeaders;

    internal Squad(SquadDefinition definition, string leaderId, IReadOnlyList<string> teamLeaderIds)
    {
        Id = definition.Id;
        Teams = [.. definition.Teams];
        Members = [.. definition.Members];
        foreach (var member in Members)
        {
            _members.Add(member.UnitId, member);
        }

        LeaderId = leaderId;
        _teamLeaders = [.. teamLeaderIds];
        TeamTakeovers = new Takeover?[Teams.Count];
    }

    /// <summary>The squad's id.</summary>
    public string Id { get; }

    /// <summary>The agent leading the squad now, or null while its place is empty (until a successor takes over).</summary>
    public string? LeaderId { get; internal set; }

    /// <summary>The team names, in formation order.</summary>
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
}
