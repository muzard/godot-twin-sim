using Twin.Sim.Combat;
using Twin.Sim.Squads;

namespace Twin.Testing;

/// <summary>
/// The starting values and the reference squad of backlog 0004 (squad), the counterpart of <see cref="SpecShooting"/>.
/// Tests pass these explicitly, so calibrating the simulation's defaults later does not change what a test checks.
/// </summary>
public static class SpecSquad
{
    /// <summary>
    /// An experienced member (kokenut): a <see cref="Experience.Regular"/> fighter. Provisional mapping onto backlog
    /// 0003's experience levels; see backlog 0004, "Experience unified with 0003".
    /// </summary>
    public const Experience Experienced = Experience.Regular;

    /// <summary>An inexperienced member (kokematon): a <see cref="Experience.Conscript"/> fighter. Provisional, as above.</summary>
    public const Experience Inexperienced = Experience.Conscript;

    /// <summary>An experienced member acts on a command 1 s after it reaches it.</summary>
    public const double ExperiencedReactionSeconds = 1;

    /// <summary>An inexperienced member acts on a command 3 s after it reaches it.</summary>
    public const double InexperiencedReactionSeconds = 3;

    /// <summary>A team leader passes a command on to its team 2 s after acting on it.</summary>
    public const double RelaySeconds = 2;

    /// <summary>A successor takes over a squad or a team 5 s after its leader is out of action.</summary>
    public const double TakeoverSeconds = 5;

    /// <summary>
    /// Distance between neighbours in file and wedge (S2a). ⚠ Provisional: the spacing should depend on the terrain;
    /// see backlog 0004, "S2a spec decisions".
    /// </summary>
    public const double IntervalMeters = 10;

    /// <summary>A compressed formation keeps at least this lateral gap between neighbouring slots, or becomes a file.</summary>
    public const double MinimumGapMeters = 1;

    /// <summary>The leader moves at this fraction of its speed, so members have a reserve to keep up.</summary>
    public const double LeaderSpeedFactor = 0.8;

    /// <summary>The leader halts while an able member is farther than this from its slot.</summary>
    public const double StragglerMeters = 20;

    /// <summary>The leader moves on when every able member is within this of its slot.</summary>
    public const double RegroupMeters = 5;

    /// <summary>Side of the square window around a member in which it searches a way to its slot.</summary>
    public const double LocalSearchMeters = 64;

    /// <summary>
    /// At the forest edge (S2c) the open line stands this far inside the edge. ⚠ Provisional ([PROPOSAL] in backlog 0004).
    /// </summary>
    public const double EdgeInsetMeters = 5;

    /// <summary>
    /// The RJ asks to leave the forest at the latest this long after commanding the deployment at the edge (S2c), even if
    /// the line has not formed. ⚠ Provisional ([PROPOSAL] in backlog 0004).
    /// </summary>
    public const double EdgeDeploymentSeconds = 60;

    /// <summary>
    /// Members that do not fit the open line at the edge form a further line this much deeper in the forest (S2c).
    /// ⚠ Provisional: compressing the line may replace this; see backlog 0004, "S2c spec decisions".
    /// </summary>
    public const double SecondLineDepthMeters = 10;

    /// <summary>Width of every observation sector (S2c): ±45° about its centre. ⚠ Provisional ([PROPOSAL]).</summary>
    public const double SectorWidthDegrees = 90;

    /// <summary>
    /// The text of the RJ's squad command to deploy at the forest edge (S2c), as logged in
    /// <see cref="Twin.Sim.Events.CommandIssued.Command"/>.
    /// </summary>
    public const string DeployAtForestEdgeCommand = "DeployAtForestEdge";

    /// <summary>
    /// The squad's front (S5b) reaches this far beyond the bearings to the outermost enemies the leader knows of, on each
    /// side. ⚠ Provisional ([PROPOSAL]).
    /// </summary>
    public const double FrontMarginDegrees = 10;

    /// <summary>
    /// Each team's share of the front (S5b) is widened by this fraction of its width (half on each side), so neighbouring
    /// sectors overlap. ⚠ First version, to be reworked (backlog 0004, "S5b spec decisions").
    /// </summary>
    public const double TeamShareWidening = 0.5;

    /// <summary>
    /// The text of the leader's fire distribution command (S5b), as logged in
    /// <see cref="Twin.Sim.Events.CommandIssued.Command"/>.
    /// </summary>
    public const string DirectSquadFireCommand = "DirectSquadFire";

    /// <summary>
    /// The doctrine of backlog 0004: command delays (reaction 1 s / 3 s, relay 2 s, takeover 5 s), squad movement
    /// (10 m interval, 1 m minimum gap, leader at 0.8 ×, halts beyond 20 m, moves on within 5 m, 64 m search window), the
    /// forest edge and observation (5 m inside the edge, 60 s limit, second line 10 m deeper, 90° sectors) and fire control
    /// (front margin 10°, team shares widened by half).
    /// </summary>
    public static SquadDoctrine Doctrine { get; } = new(
        ExperiencedReactionSeconds: ExperiencedReactionSeconds,
        InexperiencedReactionSeconds: InexperiencedReactionSeconds,
        RelaySeconds: RelaySeconds,
        TakeoverSeconds: TakeoverSeconds,
        IntervalMeters: IntervalMeters,
        MinimumGapMeters: MinimumGapMeters,
        LeaderSpeedFactor: LeaderSpeedFactor,
        StragglerMeters: StragglerMeters,
        RegroupMeters: RegroupMeters,
        LocalSearchMeters: LocalSearchMeters,
        EdgeInsetMeters: EdgeInsetMeters,
        EdgeDeploymentSeconds: EdgeDeploymentSeconds,
        SecondLineDepthMeters: SecondLineDepthMeters,
        SectorWidthDegrees: SectorWidthDegrees,
        FrontMarginDegrees: FrontMarginDegrees,
        TeamShareWidening: TeamShareWidening);

    /// <summary>The wedge's step back and out between neighbours on an arm: the interval at 45° (7.07 m).</summary>
    public static double WedgeStepMeters => IntervalMeters / Math.Sqrt(2);

    /// <summary>The command team (johtopartio): the squad leader leads it.</summary>
    public const string CommandTeam = "command";

    /// <summary>The point team (kärkipartio).</summary>
    public const string PointTeam = "point";

    /// <summary>The support team (tukipartio): the second-in-command leads it.</summary>
    public const string SupportTeam = "support";

    /// <summary>The reference squad's teams, in formation order.</summary>
    public static IReadOnlyList<string> ReferenceTeams { get; } = [CommandTeam, PointTeam, SupportTeam];

    /// <summary>
    /// The reference squad: the FDF's traditional squad of three teams of three. Experience is mixed so that every
    /// command path shows up in one squad:
    /// <list type="table">
    /// <item><term>command</term><description>rj (squad leader, experienced), j1 (inexperienced), j2 (experienced)</description></item>
    /// <item><term>point</term><description>pj (team leader, inexperienced), k1 (inexperienced), k2 (experienced)</description></item>
    /// <item><term>support</term><description>vj (second-in-command, experienced), kk (machine gunner, inexperienced), t1 (inexperienced)</description></item>
    /// </list>
    /// </summary>
    public static SquadSpec Reference(string squadId = "r1") => new(squadId, ReferenceTeams,
    [
        Member("rj", SquadRole.SquadLeader, CommandTeam, Experienced),
        Member("j1", SquadRole.Rifleman, CommandTeam, Inexperienced),
        Member("j2", SquadRole.Rifleman, CommandTeam, Experienced),
        Member("pj", SquadRole.TeamLeader, PointTeam, Inexperienced),
        Member("k1", SquadRole.Rifleman, PointTeam, Inexperienced),
        Member("k2", SquadRole.Rifleman, PointTeam, Experienced),
        Member("vj", SquadRole.SecondInCommand, SupportTeam, Experienced),
        Member("kk", SquadRole.MachineGunner, SupportTeam, Inexperienced),
        Member("t1", SquadRole.Rifleman, SupportTeam, Inexperienced),
    ]);

    /// <summary>A squad member placed with the given experience.</summary>
    public static SquadMemberSpec Member(string unitId, SquadRole role, string team, Experience experience) =>
        new(unitId, role, team, experience);
}
