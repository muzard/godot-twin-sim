using Twin.Sim.Combat;

namespace Twin.Testing;

/// <summary>
/// Values of backlog 0005 (firing positions in contact) as the tests use them. All are **[PROPOSAL]** starting values,
/// gathered here so that calibrating them later does not change what a test checks.
/// </summary>
public static class SpecFiringPositions
{
    /// <summary>A member looks for a firing position of its own within this distance, in metres.</summary>
    public const double FighterSearchMeters = 30;

    /// <summary>A team leader looks for a team position within this distance of itself, in metres.</summary>
    public const double TeamLeaderSearchMeters = 60;

    /// <summary>The squad leader looks for a team position within this distance of the team, in metres.</summary>
    public const double SquadLeaderSearchMeters = 100;

    /// <summary>Score for cover at a position: an enemy cannot see it there lying prone (0003's cover).</summary>
    public const double CoverWeight = 20;

    /// <summary>Score for concealment at a position: forest there.</summary>
    public const double ConcealmentWeight = 10;

    /// <summary>Score per metre the position's ground lies above the enemy's (none for lower ground).</summary>
    public const double HeightWeightPerMeter = 4;

    /// <summary>Score lost per metre of the way there.</summary>
    public const double DistanceWeightPerMeter = 1;

    /// <summary>Further score lost per metre of the way that runs over open ground (not forest).</summary>
    public const double OpenGroundWeightPerMeter = 1;

    /// <summary>A team leader asks the squad leader first when at least this many of its team can fire.</summary>
    public const int TeamStillFiring = 2;

    /// <summary>A team leader denied a move asks again no sooner than this, in seconds.</summary>
    public const double RetrySeconds = 30;

    /// <summary>Fire superiority is judged over this window, in seconds.</summary>
    public const double SuperiorityWindowSeconds = 10;

    /// <summary>The squad has fire superiority when its rounds in the window are at least this many times the enemy's heard.</summary>
    public const double SuperiorityFactor = 2;

    /// <summary>
    /// The name of a team leader's (or the squad leader's) command moving a team to a firing position, as it appears in
    /// <see cref="Twin.Sim.Events.CommandIssued.Command"/>.
    /// </summary>
    public const string TeamMoveCommand = "MoveTeamToFiringPosition";

    /// <summary>
    /// <paramref name="weapon"/> reaching only <paramref name="maxRangeMeters"/>: the way the tests keep a fighter from
    /// engaging an enemy it sees, so that it needs a firing position nearer to the enemy.
    /// </summary>
    public static WeaponProfile ShortRange(WeaponProfile weapon, double maxRangeMeters) =>
        (weapon ?? throw new ArgumentNullException(nameof(weapon))) with { MaxRangeMeters = maxRangeMeters };
}
