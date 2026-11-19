namespace Twin.Sim.Squads;

/// <summary>Where a team stands in deciding on a firing position (backlog 0005).</summary>
internal sealed class TeamFiringState
{
    /// <summary>When the team leader decides (a report has reached it, or a retry is due); null if nothing is pending.</summary>
    public double? DecideAt { get; set; }

    /// <summary>When the squad leader answers the team leader's request; null if none is on its way.</summary>
    public double? AnswerAt { get; set; }

    /// <summary>When the team leader, granted, moves the team; null if no grant is on its way.</summary>
    public double? MoveAt { get; set; }

    /// <summary>When the squad leader searches for the team, told its team leader found nothing; null if not.</summary>
    public double? SquadLeaderSearchAt { get; set; }

    /// <summary>The team's last move command, while its members may still be acting on it.</summary>
    public MoveTeamToFiringPosition? Move { get; set; }

    /// <summary>True once no one found a position for the team: nobody looks again until the picture changes.</summary>
    public bool Exhausted { get; set; }
}
