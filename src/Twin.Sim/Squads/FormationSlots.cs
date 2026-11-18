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
/// The slots of a formation, before any compression to fit a corridor (backlog 0004, S2a; the wedge redefined in S2c).
/// <para>
/// Formation order (the file), front to back: the teams with neither the squad leader nor the second-in-command, in team
/// order (the point team), then the squad leader's team, then the second-in-command's team; within a team, squad order.
/// </para>
/// <para>
/// The open line (<see cref="LineOrder"/>), left to right: the second-in-command's team (its riflemen outermost, then the
/// second-in-command, then its machine gunners next to the squad leader), the squad leader and its team in squad order,
/// then the other teams in team order, each in squad order.
/// </para>
/// <list type="bullet">
/// <item><b>File:</b> everyone on the leader's path, one interval apart in formation order.</item>
/// <item><b>Wedge:</b> the open line folded back at its middle member (the apex), one interval apart along each arm at
/// 45°. The leader walks its own place on the fold, so the wedge is not symmetric about the leader's path.</item>
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
        var slots = new List<FormationSlot>(order.Count);
        if (formation == SquadFormation.File)
        {
            var leaderIndex = order.FindIndex(m => m.Role == SquadRole.SquadLeader);
            for (var i = 0; i < order.Count; i++)
            {
                slots.Add(new FormationSlot(order[i].UnitId, (leaderIndex - i) * intervalMeters, 0));
            }

            return slots;
        }

        // The wedge: the open line folded back at its apex. Coordinates are first taken from the apex, then shifted so
        // the leader is at (0, 0).
        var line = LineOrder(squad).ToList();
        var apex = ApexIndex(line.Count);
        var step = intervalMeters / Math.Sqrt(2);
        (double Along, double Across) FromApex(int i)
        {
            var rank = Math.Abs(i - apex);
            var side = Math.Sign(apex - i); // left of the apex in the line: the left arm
            return (-rank * step, side * rank * step);
        }

        var leader = FromApex(line.FindIndex(id => squad.Members.First(m => m.UnitId == id).Role == SquadRole.SquadLeader));
        foreach (var member in order)
        {
            var (along, across) = FromApex(line.FindIndex(id => id == member.UnitId));
            slots.Add(new FormationSlot(member.UnitId, along - leader.Along, across - leader.Across));
        }

        return slots;
    }

    /// <summary>The open line's members, left to right facing the way the squad moves (or out of the forest).</summary>
    /// <param name="squad">The squad's definition (members, roles and teams).</param>
    public static IReadOnlyList<string> LineOrder(SquadDefinition squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        var leader = squad.Members.FirstOrDefault(m => m.Role == SquadRole.SquadLeader)
            ?? throw new ArgumentException("A squad needs a squad leader.", nameof(squad));
        var secondTeam = squad.Members.FirstOrDefault(m => m.Role == SquadRole.SecondInCommand)?.Team;
        var line = new List<string>(squad.Members.Count);
        if (secondTeam is not null && secondTeam != leader.Team)
        {
            var team = squad.Members.Where(m => m.Team == secondTeam).ToList();
            line.AddRange(team.Where(m => m.Role is not (SquadRole.SecondInCommand or SquadRole.MachineGunner))
                .Reverse().Select(m => m.UnitId));                                                   // riflemen outermost
            line.AddRange(team.Where(m => m.Role == SquadRole.SecondInCommand).Select(m => m.UnitId));
            line.AddRange(team.Where(m => m.Role == SquadRole.MachineGunner).Select(m => m.UnitId)); // next to the leader
        }

        line.Add(leader.UnitId);
        line.AddRange(squad.Members.Where(m => m.Team == leader.Team && m.UnitId != leader.UnitId).Select(m => m.UnitId));
        foreach (var team in squad.Teams.Where(t => t != leader.Team && t != secondTeam))
        {
            line.AddRange(squad.Members.Where(m => m.Team == team).Select(m => m.UnitId));
        }

        return line;
    }

    /// <summary>The wedge's apex in the open line: its middle member (for an even count, the one left of the middle).</summary>
    internal static int ApexIndex(int count) => (count - 1) / 2;

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
}
