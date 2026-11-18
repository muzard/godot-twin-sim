namespace Twin.Sim.Squads;

/// <summary>
/// A member's slot relative to its squad leader (backlog 0004, S2a): <paramref name="Along"/> metres along the leader's
/// path (positive ahead) and <paramref name="Across"/> metres across it (positive to the left).
/// </summary>
/// <param name="UnitId">The member.</param>
/// <param name="Along">Metres along the leader's path, positive ahead of the leader.</param>
/// <param name="Across">Metres across the path, positive to the left.</param>
public sealed record FormationSlot(string UnitId, double Along, double Across);

/// <summary>
/// The slots of a formation, before any compression to fit a corridor (backlog 0004, S2a).
/// <para>
/// Formation order, front to back: the teams with neither the squad leader nor the second-in-command, in team order (the
/// point team), then the squad leader's team, then the second-in-command's team; within a team, squad order.
/// </para>
/// <list type="bullet">
/// <item><b>File:</b> everyone on the leader's path, one interval apart in formation order.</item>
/// <item><b>Wedge:</b> the first member at the apex; the others alternate left and right arm, one interval apart along
/// each arm at 45°. The leader walks the axis at the depth of its place, so its own arm slot stays empty.</item>
/// </list>
/// </summary>
public static class FormationSlots
{
    /// <summary>Each member's slot relative to the leader, in formation order.</summary>
    /// <param name="formation">The formation.</param>
    /// <param name="squad">The squad's definition (members, roles and teams).</param>
    /// <param name="intervalMeters">Distance between neighbours.</param>
    public static IReadOnlyList<FormationSlot> For(SquadFormation formation, SquadDefinition squad, double intervalMeters)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (!squad.Members.Any(m => m.Role == SquadRole.SquadLeader))
        {
            throw new ArgumentException("A squad needs a squad leader.", nameof(squad));
        }

        var order = FormationOrder(squad);
        var leaderIndex = order.FindIndex(m => m.Role == SquadRole.SquadLeader);

        var slots = new List<FormationSlot>(order.Count);
        if (formation == SquadFormation.File)
        {
            for (var i = 0; i < order.Count; i++)
            {
                slots.Add(new FormationSlot(order[i].UnitId, (leaderIndex - i) * intervalMeters, 0));
            }

            return slots;
        }

        var step = intervalMeters / Math.Sqrt(2);
        var leaderRank = Rank(leaderIndex);
        for (var i = 0; i < order.Count; i++)
        {
            var rank = Rank(i);
            var side = i == leaderIndex || i == 0 ? 0 : (i % 2 == 1 ? 1 : -1);
            slots.Add(new FormationSlot(order[i].UnitId, (leaderRank - rank) * step, side * rank * step));
        }

        return slots;
    }

    /// <summary>The members front to back (see the class remarks).</summary>
    internal static List<SquadMember> FormationOrder(SquadDefinition squad)
    {
        var leaderTeam = squad.Members.First(m => m.Role == SquadRole.SquadLeader).Team;
        var secondTeam = squad.Members.FirstOrDefault(m => m.Role == SquadRole.SecondInCommand)?.Team;
        var teams = squad.Teams.Where(t => t != leaderTeam && t != secondTeam).ToList();
        teams.Add(leaderTeam);
        if (secondTeam is not null && secondTeam != leaderTeam)
        {
            teams.Add(secondTeam);
        }

        return teams.SelectMany(t => squad.Members.Where(m => m.Team == t)).ToList();
    }

    /// <summary>Steps back from the apex of the i-th member in formation order.</summary>
    private static int Rank(int i) => (i + 1) / 2;
}
