namespace Twin.Sim.Squads;

/// <summary>
/// How squad members in contact find firing positions (backlog 0005). All values are starting values to be calibrated.
/// </summary>
/// <param name="FighterSearchMeters">A member looks for a firing position of its own within this distance.</param>
/// <param name="TeamLeaderSearchMeters">A team leader looks for a team position within this distance of itself.</param>
/// <param name="SquadLeaderSearchMeters">The squad leader looks for a team position within this distance of the team.</param>
/// <param name="CoverWeight">Score for cover at a position: an enemy cannot see it there lying prone.</param>
/// <param name="ConcealmentWeight">Score for concealment at a position: forest there.</param>
/// <param name="HeightWeightPerMeter">Score per metre the position's ground lies above the enemy's.</param>
/// <param name="DistanceWeightPerMeter">Score lost per metre of the way there.</param>
/// <param name="OpenGroundWeightPerMeter">Further score lost per metre of the way over open ground (not forest).</param>
/// <param name="TeamStillFiring">A team leader asks the squad leader first when at least this many of its team can fire.</param>
/// <param name="RetrySeconds">A team leader denied a move asks again no sooner than this.</param>
/// <param name="SuperiorityWindowSeconds">Fire superiority is judged over this window.</param>
/// <param name="SuperiorityFactor">Fire superiority: the squad's shots in the window at least this many times the enemy's heard.</param>
public sealed record FiringPositionRules(
    double FighterSearchMeters = 30,
    double TeamLeaderSearchMeters = 60,
    double SquadLeaderSearchMeters = 100,
    double CoverWeight = 20,
    double ConcealmentWeight = 10,
    double HeightWeightPerMeter = 4,
    double DistanceWeightPerMeter = 1,
    double OpenGroundWeightPerMeter = 1,
    int TeamStillFiring = 2,
    double RetrySeconds = 30,
    double SuperiorityWindowSeconds = 10,
    double SuperiorityFactor = 2)
{
    /// <summary>The starting values of backlog 0005.</summary>
    public static FiringPositionRules Default { get; } = new();
}
