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
/// <param name="Teams">The team names. (The formation order of teams follows the roles: see <see cref="FormationSlots"/>.)</param>
/// <param name="Members">The members, in squad order: a team's next member in this order takes it over when its leader is
/// out of action.</param>
/// <param name="Formation">The formation the squad starts in.</param>
public sealed record SquadDefinition(
    string Id, IReadOnlyList<string> Teams, IReadOnlyList<SquadMember> Members, SquadFormation Formation = SquadFormation.File);

/// <summary>How a squad moves: the shape its members' slots make around the leader (backlog 0004, S2a).</summary>
public enum SquadFormation
{
    /// <summary>Jono: everyone on the leader's path, one interval apart.</summary>
    File,

    /// <summary>Kiila: one squad wedge with the first member at its apex and the leader on its axis.</summary>
    Wedge,
}

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
/// <param name="IntervalMeters">Distance between neighbours in file and wedge (S2a). Provisional: it should depend on the
/// terrain.</param>
/// <param name="MinimumGapMeters">A formation compressed to fit a corridor keeps at least this lateral gap between
/// neighbouring slots, or becomes a file.</param>
/// <param name="LeaderSpeedFactor">The squad leader moves at this fraction of its speed, so members can keep up.</param>
/// <param name="StragglerMeters">The leader halts while a member is farther than this from its slot.</param>
/// <param name="RegroupMeters">The leader moves on when every member is within this of its slot.</param>
/// <param name="LocalSearchMeters">Side of the square window around a member in which it searches a way to its slot.</param>
public sealed record SquadDoctrine(
    double ExperiencedReactionSeconds = 1,
    double InexperiencedReactionSeconds = 3,
    double RelaySeconds = 2,
    double TakeoverSeconds = 5,
    double IntervalMeters = 10,
    double MinimumGapMeters = 1,
    double LeaderSpeedFactor = 0.8,
    double StragglerMeters = 20,
    double RegroupMeters = 5,
    double LocalSearchMeters = 64)
{
    /// <summary>
    /// The accepted starting values: reaction 1 s / 3 s, relay 2 s, takeover 5 s; 10 m interval, 1 m minimum gap, leader
    /// at 0.8 ×, halt beyond 20 m, move on within 5 m, 64 m search window.
    /// </summary>
    public static SquadDoctrine Default { get; } = new();

    /// <summary>Where each formation place watches while the squad moves or rests (backlog 0004 S5a1).</summary>
    public WatchSectors WatchSectors { get; init; } = WatchSectors.Default;

    /// <summary>
    /// A spoken spot report reaches squad mates within this distance (backlog 0004 S5a1). ⚠ A placeholder: how far a
    /// voice carries must be modelled better later (terrain, background noise, wind, loudness).
    /// </summary>
    public double VoiceRangeMeters { get; init; } = 30;

    /// <summary>
    /// True if a fighter with this experience acts on the squad leader's command directly; otherwise it waits for its
    /// team leader to pass the command on.
    /// </summary>
    public static bool ActsDirectly(Experience experience) => experience != Experience.Conscript;

    /// <summary>The reaction time to a command of a member with the given experience.</summary>
    public double ReactionSeconds(Experience experience) =>
        ActsDirectly(experience) ? ExperiencedReactionSeconds : InexperiencedReactionSeconds;
}

/// <summary>
/// Each formation place's watch direction (backlog 0004 S5a1), in degrees to the right of the squad's direction of
/// travel, in formation order (front to back, see <see cref="FormationSlots"/>). A place beyond a table watches ahead.
/// </summary>
/// <param name="FileDegrees">The file's places.</param>
/// <param name="WedgeDegrees">The wedge's places.</param>
public sealed record WatchSectors(IReadOnlyList<double> FileDegrees, IReadOnlyList<double> WedgeDegrees)
{
    /// <summary>
    /// The starting tables for the nine-member reference squad: in the file the first member and the leader ahead, the
    /// rest alternately right and left, the last one back; in the wedge the apex and the leader ahead, the arms out to
    /// their side, the rear corners back left and back right.
    /// </summary>
    public static WatchSectors Default { get; } = new(
        FileDegrees: [0, 90, -90, 0, 90, -90, 90, -90, 180],
        WedgeDegrees: [0, -45, 45, 0, 90, -90, 90, -135, 135]);

    /// <summary>The watch angle of the place <paramref name="place"/> (0 = front) in <paramref name="formation"/>.</summary>
    public double DegreesAt(SquadFormation formation, int place)
    {
        var table = formation == SquadFormation.Wedge ? WedgeDegrees : FileDegrees;
        return place >= 0 && place < table.Count ? table[place] : 0;
    }
}

/// <summary>How a spot report is passed on (backlog 0004 S5a1). Hand signals come in a later ticket.</summary>
public enum ReportMethod
{
    /// <summary>Spoken: heard by squad mates within the voice range.</summary>
    Voice,
}
