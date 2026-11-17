using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;

namespace Twin.Sim.Systems;

/// <summary>
/// The chain of command (backlog 0004, S1). A squad leader's command reaches each member after a delay
/// (<see cref="SquadDoctrine"/>):
/// <list type="bullet">
/// <item>an experienced member, a team leader, and any member of the leader's own team act after their own reaction
/// time;</item>
/// <item>any other inexperienced member acts when its team leader passes the command on: the team leader's reaction,
/// the relay time, then its own reaction. If the team has no leader when the command is given, the command lapses for
/// it.</item>
/// </list>
/// When a leader is out of action its place is empty at once; the successor (for the squad: the second-in-command, then
/// the members whose role is <see cref="SquadRole.TeamLeader"/>, then anyone, in squad order; for a team: its next member
/// in squad order) takes over after the takeover time. A team leader passes on a command it has acted on even if it is
/// out of action before the relay time has passed. Runs after firing, so a leader shot out of action leaves its place on the same step.
/// </summary>
public sealed class SquadSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var squad in simulation.Squads)
        {
            Deliver(simulation, squad);
            FillTeamLeaders(simulation, squad);
            FillLeader(simulation, squad);
        }
    }

    /// <summary>
    /// The leader commands its squad: logs <see cref="CommandIssued"/>, carries the command out itself at once, and sends
    /// it on its way to every other member.
    /// </summary>
    internal static void Command(Simulation simulation, Squad squad, Unit leader, Order command)
    {
        var now = simulation.TimeSeconds;
        var doctrine = simulation.Config.SquadDoctrine;
        var commandId = simulation.NextCommandId();
        simulation.Log(new CommandIssued(now, leader.Id, squad.Id, commandId, command.ToString()));
        CarryOut(leader, command);

        foreach (var member in squad.Members)
        {
            if (member.UnitId == leader.Id || IsOut(simulation, member.UnitId))
            {
                continue;
            }

            var teamLeader = squad.TeamLeaderId(member.Team);
            var experience = ExperienceOf(simulation, member.UnitId);
            if (SquadDoctrine.ActsDirectly(experience) || teamLeader == member.UnitId || teamLeader == leader.Id)
            {
                var relayTo = teamLeader == member.UnitId ? WaitingForTheirTeamLeader(simulation, squad, member) : [];
                squad.Deliveries.Add(new Squad.Delivery(
                    member.UnitId, commandId, command, now + doctrine.ReactionSeconds(experience), relayTo));
            }

            // Otherwise the member waits for its team leader to pass the command on, or, with no team leader, the
            // command lapses for it.
        }
    }

    private static List<string> WaitingForTheirTeamLeader(Simulation simulation, Squad squad, SquadMember teamLeader) =>
        squad.Members
            .Where(m => m.Team == teamLeader.Team && m.UnitId != teamLeader.UnitId &&
                !SquadDoctrine.ActsDirectly(ExperienceOf(simulation, m.UnitId)))
            .Select(m => m.UnitId)
            .ToList();

    /// <summary>A member's experience: its own as a fighter (every squad member is one).</summary>
    private static Experience ExperienceOf(Simulation simulation, string unitId) =>
        simulation.GetUnit(unitId).Combatant?.Experience ?? Experience.Regular;

    private static void Deliver(Simulation simulation, Squad squad)
    {
        var now = simulation.TimeSeconds;
        var doctrine = simulation.Config.SquadDoctrine;
        bool IsDue(Squad.Delivery d) => now >= d.Due - CombatQueries.TimeEpsilon;
        var due = squad.Deliveries.Where(IsDue).ToList();
        squad.Deliveries.RemoveAll(IsDue);
        foreach (var delivery in due)
        {
            if (IsOut(simulation, delivery.UnitId))
            {
                continue; // it never acts, and passes nothing on
            }

            var unit = simulation.GetUnit(delivery.UnitId);
            simulation.Log(new CommandReceived(now, unit.Id, delivery.CommandId));
            CarryOut(unit, delivery.Command);
            foreach (var recipient in delivery.RelayTo)
            {
                // Counted from when the team leader was due to act, so ticks never add up to extra delay.
                var relayed = delivery.Due + doctrine.RelaySeconds + doctrine.ReactionSeconds(ExperienceOf(simulation, recipient));
                squad.Deliveries.Add(new Squad.Delivery(recipient, delivery.CommandId, delivery.Command, relayed, []));
            }
        }
    }

    /// <summary>
    /// The unit acts on a command. A member's posture change starts on the next step (this system runs after
    /// <see cref="CombatStatusSystem"/>); the leader's, carried out when its order takes effect, starts on the same step.
    /// </summary>
    private static void CarryOut(Unit unit, Order command)
    {
        if (command is SetSquadPosture posture)
        {
            CombatQueries.WantPosture(unit, posture.Posture);
        }
    }

    private static void FillTeamLeaders(Simulation simulation, Squad squad)
    {
        var now = simulation.TimeSeconds;
        var takeoverSeconds = simulation.Config.SquadDoctrine.TakeoverSeconds;
        for (var team = 0; team < squad.Teams.Count; team++)
        {
            if (squad.TeamLeaderIds[team] is { } current)
            {
                if (!IsOut(simulation, current))
                {
                    continue;
                }

                squad.SetTeamLeader(team, null);
            }

            var name = squad.Teams[team];
            var successor = squad.Members.FirstOrDefault(m => m.Team == name && !IsOut(simulation, m.UnitId))?.UnitId;
            squad.TeamTakeovers[team] = Advance(squad.TeamTakeovers[team], successor, now, takeoverSeconds, out var takesOver);
            if (takesOver is not null)
            {
                squad.SetTeamLeader(team, takesOver);
                simulation.Log(new TeamLeaderChanged(now, takesOver, squad.Id, name));
            }
        }
    }

    private static void FillLeader(Simulation simulation, Squad squad)
    {
        var now = simulation.TimeSeconds;
        if (squad.LeaderId is { } current)
        {
            if (!IsOut(simulation, current))
            {
                return;
            }

            squad.LeaderId = null;
        }

        var successor = SuccessionOrder(squad).FirstOrDefault(id => !IsOut(simulation, id));
        squad.LeaderTakeover = Advance(
            squad.LeaderTakeover, successor, now, simulation.Config.SquadDoctrine.TakeoverSeconds, out var takesOver);
        if (takesOver is not null)
        {
            squad.LeaderId = takesOver;
            simulation.Log(new LeaderChanged(now, takesOver, squad.Id));
        }
    }

    /// <summary>
    /// The second-in-command, then the members with the team leader role, then everyone else, each in squad order. (A
    /// member who took over a team does not move up: the role decides, not who leads the team now.)
    /// </summary>
    private static IEnumerable<string> SuccessionOrder(Squad squad) =>
        squad.Members.Where(m => m.Role == SquadRole.SecondInCommand)
            .Concat(squad.Members.Where(m => m.Role == SquadRole.TeamLeader))
            .Concat(squad.Members.Where(m => m.Role is not (SquadRole.SecondInCommand or SquadRole.TeamLeader)))
            .Select(m => m.UnitId);

    /// <summary>
    /// Moves a takeover on by one step: a new successor starts its takeover time now; one whose time has come takes over
    /// (<paramref name="takesOver"/>) and the takeover ends.
    /// </summary>
    private static Squad.Takeover? Advance(
        Squad.Takeover? pending, string? successor, double now, double takeoverSeconds, out string? takesOver)
    {
        takesOver = null;
        if (successor is null)
        {
            return null;
        }

        if (pending is null || pending.UnitId != successor)
        {
            pending = new Squad.Takeover(successor, now + takeoverSeconds);
        }

        if (now >= pending.Due - CombatQueries.TimeEpsilon)
        {
            takesOver = successor;
            return null;
        }

        return pending;
    }

    private static bool IsOut(Simulation simulation, string unitId) => simulation.GetUnit(unitId).Health == Health.OutOfAction;
}
