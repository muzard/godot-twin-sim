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

    /// <summary>Command delays of backlog 0004: reaction 1 s / 3 s, relay 2 s, takeover 5 s.</summary>
    public static SquadDoctrine Doctrine { get; } = new(
        ExperiencedReactionSeconds: ExperiencedReactionSeconds,
        InexperiencedReactionSeconds: InexperiencedReactionSeconds,
        RelaySeconds: RelaySeconds,
        TakeoverSeconds: TakeoverSeconds);

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
