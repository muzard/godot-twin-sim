namespace Twin.Sim.Command;

/// <summary>The scripted leader's answer to a permission request.</summary>
public enum LeaderAnswer
{
    /// <summary>Permission granted.</summary>
    Grant,

    /// <summary>Permission denied.</summary>
    Deny,

    /// <summary>The leader never answers; the agent waits until its timeout.</summary>
    NoAnswer,
}

/// <summary>A leader who answers every permission request the same way after a fixed delay (spec v0.1).</summary>
/// <param name="Answer">The answer.</param>
/// <param name="DelaySeconds">Simulated seconds from the request to the answer.</param>
/// <param name="PermitRadiusMeters">A granted permit is valid within this distance of the requested point.</param>
public sealed record ScriptedLeader(LeaderAnswer Answer, double DelaySeconds, double PermitRadiusMeters);

/// <summary>When an agent asks for permission and how long it waits.</summary>
/// <param name="Threshold">Ask when the boundary-respecting route is missing or costs more than this times the reference route.</param>
/// <param name="TimeoutSeconds">Give up waiting for an answer after this many simulated seconds.</param>
public sealed record PermissionRules(double Threshold, double TimeoutSeconds)
{
    /// <summary>Spec v0.1 starting values: threshold 1.5, timeout 120 s.</summary>
    public static PermissionRules Default { get; } = new(1.5, 120);
}
