using Twin.Sim.Command;

namespace Twin.Sim.Combat;

/// <summary>Which side an agent fights on. Agents never shoot at their own side.</summary>
public enum Side
{
    /// <summary>Own forces.</summary>
    Blue,

    /// <summary>Opposing forces.</summary>
    Red,
}

/// <summary>Body posture: sets eye height, aim point, speed and how hard the agent is to hit.</summary>
public enum Posture
{
    /// <summary>Standing.</summary>
    Standing,

    /// <summary>Crouching.</summary>
    Crouching,

    /// <summary>Lying prone; moving means crawling.</summary>
    Prone,
}

/// <summary>An agent's health (backlog 0002): unhurt, pinned for 60 s after the first hit, then wounded; a second hit puts it out of action.</summary>
public enum Health
{
    /// <summary>Not hit.</summary>
    Unhurt,

    /// <summary>Just hit: completely pinned, prone, for the pinned time.</summary>
    Pinned,

    /// <summary>Hit once and past the pinned time: slower and less accurate.</summary>
    Wounded,

    /// <summary>Hit twice: does nothing more.</summary>
    OutOfAction,
}

/// <summary>When an agent may open fire without a <c>FireAt</c> order.</summary>
public enum RulesOfEngagement
{
    /// <summary>Only on a <c>FireAt</c> order, even when fired at.</summary>
    HoldFire,

    /// <summary>Fire opened on the agent gives it the permission and the duty to shoot back at the shooter once it sees it.</summary>
    ReturnFire,

    /// <summary>As <see cref="ReturnFire"/>, and also engages the nearest visible enemy in range.</summary>
    FreeFire,
}

/// <summary>What a posture means for sight, movement and being hit.</summary>
/// <param name="EyeHeight">Eye height above the ground, meters.</param>
/// <param name="AimHeight">Height of the point others aim at (centre of mass), meters.</param>
/// <param name="SpeedFactor">Multiplier on walking speed.</param>
/// <param name="SizeFactor">Multiplier on the hit probability against the agent.</param>
public sealed record PostureValues(double EyeHeight, double AimHeight, double SpeedFactor, double SizeFactor);

/// <summary>The rules of line of sight, hits, posture and suppression (backlog 0002).</summary>
/// <param name="Standing">Standing posture values.</param>
/// <param name="Crouching">Crouching posture values.</param>
/// <param name="Prone">Prone posture values.</param>
/// <param name="PostureChangeSeconds">How long a voluntary posture change takes; the agent neither moves nor shoots meanwhile.</param>
/// <param name="SightSampleMeters">Sampling step along a sight line.</param>
/// <param name="CanopyHeightMeters">Forest attenuates a sight line only below this height above the ground.</param>
/// <param name="ForestAttenuationMeters">λ in the forest transmission T = exp(−L_forest / λ).</param>
/// <param name="VisibleTransmission">A target is visible when T is at least this.</param>
/// <param name="FlashTransmission">While its muzzle flash lasts, a shooter is visible when T is at least this.</param>
/// <param name="SuppressedHitFactor">Multiplier on the hit probability of a suppressed shooter.</param>
/// <param name="WoundedHitFactor">Multiplier on the hit probability of a wounded shooter.</param>
/// <param name="WoundedSpeedFactor">Multiplier on the speed of a wounded agent.</param>
/// <param name="PinnedSeconds">How long the first hit pins an agent.</param>
/// <param name="SuppressionSeconds">How long a shot at an agent suppresses it.</param>
public sealed record CombatRules(
    PostureValues Standing,
    PostureValues Crouching,
    PostureValues Prone,
    double PostureChangeSeconds,
    double SightSampleMeters,
    double CanopyHeightMeters,
    double ForestAttenuationMeters,
    double VisibleTransmission,
    double FlashTransmission,
    double SuppressedHitFactor,
    double WoundedHitFactor,
    double WoundedSpeedFactor,
    double PinnedSeconds,
    double SuppressionSeconds)
{
    /// <summary>Backlog 0002 starting values.</summary>
    public static CombatRules Default { get; } = new(
        new PostureValues(1.6, 1.0, 1.0, 1.0),
        new PostureValues(1.1, 0.7, 0.5, 0.7),
        new PostureValues(0.3, 0.2, 0.1, 0.4),
        PostureChangeSeconds: 1,
        SightSampleMeters: 0.5,
        CanopyHeightMeters: 15,
        ForestAttenuationMeters: 25,
        VisibleTransmission: 0.05,
        FlashTransmission: 0.01,
        SuppressedHitFactor: 0.3,
        WoundedHitFactor: 0.5,
        WoundedSpeedFactor: 0.5,
        PinnedSeconds: 60,
        SuppressionSeconds: 10);

    /// <summary>Doctrine: rules decided by people, such as whether return fire beats a <c>FireAt</c> order.</summary>
    public Doctrine Doctrine { get; init; } = Doctrine.Default;

    /// <summary>The values of a posture.</summary>
    public PostureValues For(Posture posture) => posture switch
    {
        Posture.Crouching => Crouching,
        Posture.Prone => Prone,
        _ => Standing,
    };
}

/// <summary>A weapon: hit probability over range, rate of fire, ammunition and its signature.</summary>
/// <param name="HitProbabilityAtZero">Hit probability at zero range in the open against a standing target.</param>
/// <param name="HitFalloffMeters">P(hit) falls as exp(−d / this).</param>
/// <param name="MaxRangeMeters">Beyond this, P(hit) is zero and the weapon is not fired.</param>
/// <param name="SecondsPerShot">Time between aimed shots.</param>
/// <param name="Rounds">Rounds carried.</param>
/// <param name="SoundRangeMeters">Shots are heard this far.</param>
/// <param name="FlashSeconds">How long the muzzle flash makes the shooter easier to see; 0 for none.</param>
public sealed record WeaponProfile(
    double HitProbabilityAtZero, double HitFalloffMeters, double MaxRangeMeters, double SecondsPerShot, int Rounds,
    double SoundRangeMeters, double FlashSeconds)
{
    /// <summary>Backlog 0002 infantry rifle.</summary>
    public static WeaponProfile Rifle { get; } = new(0.6, 150, 600, 2, 150, 1500, 2);
}

/// <summary>What makes an agent a fighter.</summary>
/// <param name="Side">Its side.</param>
/// <param name="Rules">Its rules of engagement.</param>
/// <param name="Weapon">Its weapon.</param>
/// <param name="UnitId">Its fire team or squad, for the later casualty-care system; null if none.</param>
public sealed record Combatant(Side Side, RulesOfEngagement Rules, WeaponProfile Weapon, string? UnitId = null);
