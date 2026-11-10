namespace Twin.Sim.Command;

/// <summary>When a permission answer that arrives while the agent is pinned or suppressed is handled.</summary>
public enum PermissionAnswerTiming
{
    /// <summary>When the agent is free again; the answer is logged then.</summary>
    WhenFree,

    /// <summary>On time; the agent plans at once but still does not move until it is free.</summary>
    Immediately,
}

/// <summary>
/// Rules decided by people rather than by physics, kept as data so they can be changed per scenario without changing
/// code (<c>doctrine</c> in scenario.json; backlog 0002). The defaults are the decisions of 2026-11-09.
/// </summary>
/// <param name="ReturnFireOverridesFireAt">
/// True: an agent under <c>ReturnFire</c> or <c>FreeFire</c> whose <c>FireAt</c> target cannot be engaged shoots back at a
/// visible attacker (the duty to return fire wins). False: it waits for its ordered target.
/// </param>
/// <param name="PermissionAnswers">When a permission answer that arrives while the agent is pinned or suppressed is handled.</param>
public sealed record Doctrine(
    bool ReturnFireOverridesFireAt = true, PermissionAnswerTiming PermissionAnswers = PermissionAnswerTiming.WhenFree)
{
    /// <summary>The decisions of 2026-11-09: the duty to return fire wins; answers are handled when the agent is free.</summary>
    public static Doctrine Default { get; } = new();
}
