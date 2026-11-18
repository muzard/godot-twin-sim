using Twin.Sim.Combat;

namespace Twin.Sim.Squads;

/// <summary>A squad member's role (backlog 0004).</summary>
public enum SquadRole
{
    /// <summary>Ryhmänjohtaja (RJ): takes the squad's orders and commands it; leads the command team.</summary>
    SquadLeader,

    /// <summary>Varajohtaja (VJ): leads its team and takes command when the squad leader is out of action.</summary>
    SecondInCommand,

    /// <summary>Partionjohtaja (PJ): leads its team and passes the leader's commands on to it.</summary>
    TeamLeader,

    /// <summary>Konekivääriampuja: a rifleman with the machine gun (S4).</summary>
    MachineGunner,

    /// <summary>Taistelija: follows its slot and the commands it gets.</summary>
    Rifleman,
}

/// <summary>
/// One agent's place in a squad. How a command reaches the member depends on the fighter's own
/// <see cref="Combatant.Experience"/> (see <see cref="SquadDoctrine.ActsDirectly"/>).
/// </summary>
/// <param name="UnitId">The agent, already on the map.</param>
/// <param name="Role">Its role.</param>
/// <param name="Team">The team (partio) it belongs to; one of the squad's <see cref="SquadDefinition.Teams"/>.</param>
public sealed record SquadMember(string UnitId, SquadRole Role, string Team);

/// <summary>
/// A squad to form from agents already on the map (<see cref="Simulation.AddSquad"/>). It needs exactly one
/// <see cref="SquadRole.SquadLeader"/>, at most one <see cref="SquadRole.SecondInCommand"/>, and in every team at least one
/// member and exactly one leader (the squad leader, the second-in-command or a team leader). Members are fighters on one
/// side, each in one squad only.
/// </summary>
/// <param name="Id">The squad's id, unique in the simulation.</param>
/// <param name="Teams">The team names, in formation order.</param>
/// <param name="Members">The members, in squad order: a team's next member in this order takes it over when its leader is
/// out of action.</param>
public sealed record SquadDefinition(string Id, IReadOnlyList<string> Teams, IReadOnlyList<SquadMember> Members);

/// <summary>
/// How commands travel in a squad: the doctrine's delays (backlog 0004). An experienced member acts on the leader's
/// command after <paramref name="ExperiencedReactionSeconds"/>; an inexperienced member after its team leader's reaction,
/// <paramref name="RelaySeconds"/> and its own <paramref name="InexperiencedReactionSeconds"/>. A successor takes over a
/// squad or team <paramref name="TakeoverSeconds"/> after its leader is out of action.
/// <para>
/// "Experienced" is the fighter's <see cref="Combatant.Experience"/> (backlog 0003): <see cref="Experience.Regular"/> and
/// <see cref="Experience.SpecialForces"/> are experienced, <see cref="Experience.Conscript"/> (conscript or reservist) is
/// not. This mapping is provisional: the squad design counts reservists as experienced, so it will change when experience
/// is reworked (see backlog 0004, "Experience unified with 0003").
/// </para>
/// </summary>
/// <param name="ExperiencedReactionSeconds">t_react of an experienced member.</param>
/// <param name="InexperiencedReactionSeconds">t_react of an inexperienced member.</param>
/// <param name="RelaySeconds">t_relay: a team leader passes a command on this long after acting on it.</param>
/// <param name="TakeoverSeconds">t_takeover.</param>
public sealed record SquadDoctrine(
    double ExperiencedReactionSeconds = 1,
    double InexperiencedReactionSeconds = 3,
    double RelaySeconds = 2,
    double TakeoverSeconds = 5)
{
    /// <summary>The accepted starting values: reaction 1 s / 3 s, relay 2 s, takeover 5 s.</summary>
    public static SquadDoctrine Default { get; } = new();

    /// <summary>
    /// True if a fighter with this experience acts on the squad leader's command directly; otherwise it waits for its
    /// team leader to pass the command on.
    /// </summary>
    public static bool ActsDirectly(Experience experience) => experience != Experience.Conscript;

    /// <summary>The reaction time to a command of a member with the given experience.</summary>
    public double ReactionSeconds(Experience experience) =>
        ActsDirectly(experience) ? ExperiencedReactionSeconds : InexperiencedReactionSeconds;
}
