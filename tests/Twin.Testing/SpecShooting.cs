using Twin.Sim.Combat;
using Twin.Sim.Command;

namespace Twin.Testing;

/// <summary>
/// The starting values and reference formulas of backlog 0002 (shooting), the counterpart of <see cref="SpecV01"/>.
/// Tests pass these explicitly, so calibrating the simulation's defaults later does not change what a test checks.
/// Values marked [PROPOSAL] in the backlog item live only here.
/// </summary>
public static class SpecShooting
{
    /// <summary>Standing: eye 1.6 m, aim point 1.0 m, full speed, full size.</summary>
    public static PostureValues Standing { get; } = new(EyeHeight: 1.6, AimHeight: 1.0, SpeedFactor: 1.0, SizeFactor: 1.0);

    /// <summary>Crouching: eye 1.1 m, aim point 0.7 m, half speed, 0.7 size.</summary>
    public static PostureValues Crouching { get; } = new(EyeHeight: 1.1, AimHeight: 0.7, SpeedFactor: 0.5, SizeFactor: 0.7);

    /// <summary>Prone: eye 0.3 m, aim point 0.2 m, crawling at 0.1 speed, 0.4 size.</summary>
    public static PostureValues Prone { get; } = new(EyeHeight: 0.3, AimHeight: 0.2, SpeedFactor: 0.1, SizeFactor: 0.4);

    /// <summary>
    /// Line of sight, hit and posture rules of backlog 0002, with the suppression rules and the drill of backlog 0003
    /// (<see cref="SpecSuppression"/>). The 0002 rule "a shot suppresses for 10 s" is gone.
    /// <para>
    /// Spotting (backlog 0004 S5a1) is off: these tests are about firing, so a fighter knows every enemy it can see at
    /// once, all round, as in 0002 and 0003, and nothing is spotted or reported. Spotting tests use
    /// <see cref="SpecSpotting.Rules"/>.
    /// </para>
    /// </summary>
    public static CombatRules Rules { get; } = new(
        Standing: Standing,
        Crouching: Crouching,
        Prone: Prone,
        PostureChangeSeconds: 1,
        SightSampleMeters: 0.5,
        CanopyHeightMeters: 15,
        ForestAttenuationMeters: 25,
        VisibleTransmission: 0.05,
        FlashTransmission: 0.01,
        SuppressedHitFactor: 0.3,
        WoundedHitFactor: 0.5,
        WoundedSpeedFactor: 0.5,
        PinnedSeconds: 60)
    {
        Suppression = SpecSuppression.Rules,
        Doctrine = Doctrine.Default with { Drill = SpecSuppression.Drill },
        Observation = ObservationRules.Off,
    };

    /// <summary>
    /// The infantry rifle: P(hit) = 0.6 · exp(−d / 150 m) up to 600 m, one aimed shot every 2 s, 150 rounds,
    /// heard up to 1500 m, muzzle flash visible for 2 s. Backlog 0003: a round passing close suppresses with S = 1.2,
    /// one round per shot, a snap shot every 0.7 s.
    /// </summary>
    public static WeaponProfile Rifle { get; } = new(
        HitProbabilityAtZero: 0.6,
        HitFalloffMeters: 150,
        MaxRangeMeters: 600,
        SecondsPerShot: 2,
        Rounds: 150,
        SoundRangeMeters: 1500,
        FlashSeconds: 2,
        SuppressionPerRound: 1.2,
        RoundsPerBurst: 1,
        SnapSecondsPerShot: 0.7);

    /// <summary>Hit probability from range alone: 0.6 · exp(−d / 150), zero beyond 600 m.</summary>
    public static double RangeProbability(double distance) => distance > 600 ? 0 : 0.6 * Math.Exp(-distance / 150);

    /// <summary>Forest transmission for <paramref name="forestMeters"/> of sight line inside forest: exp(−L / 25 m).</summary>
    public static double Transmission(double forestMeters) => Math.Exp(-forestMeters / 25);

    /// <summary>A combatant with the rifle.</summary>
    public static Combatant Fighter(
        Side side, RulesOfEngagement rules, string? unitId = null, Experience experience = Experience.Regular) =>
        new(side, rules, Rifle, unitId, experience);
}
