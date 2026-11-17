using Twin.Sim.Combat;
using Twin.Sim.Command;

namespace Twin.Testing;

/// <summary>
/// The starting values and reference formulas of backlog 0003 (suppression as fire pressure, experience and the immediate
/// action drill), the counterpart of <see cref="SpecShooting"/>. Tests compute their expectations from these, so
/// calibrating the simulation's defaults later does not change what a test checks.
/// </summary>
public static class SpecSuppression
{
    /// <summary>Conscript or reservist: accuracy 0.8, dispersion 1.3, reaction 2 s, threshold 0.8, half-life 6 s.</summary>
    public static ExperienceValues Conscript { get; } = new(
        Accuracy: 0.8, Dispersion: 1.3, ReactionSeconds: 2.0, SuppressionThreshold: 0.8, PressureHalfLifeSeconds: 6);

    /// <summary>Regular soldier (the default): accuracy 1, dispersion 1, reaction 1.5 s, threshold 1, half-life 4 s.</summary>
    public static ExperienceValues Regular { get; } = new(
        Accuracy: 1.0, Dispersion: 1.0, ReactionSeconds: 1.5, SuppressionThreshold: 1.0, PressureHalfLifeSeconds: 4);

    /// <summary>Special forces: accuracy 1.3, dispersion 0.6, reaction 0.5 s, threshold 1.5, half-life 2.5 s.</summary>
    public static ExperienceValues SpecialForces { get; } = new(
        Accuracy: 1.3, Dispersion: 0.6, ReactionSeconds: 0.5, SuppressionThreshold: 1.5, PressureHalfLifeSeconds: 2.5);

    /// <summary>Aimed fire: full hit probability, 3 mil dispersion.</summary>
    public static FireModeValues Aimed { get; } = new(HitFactor: 1, DispersionMils: 3);

    /// <summary>Snap shots (heittolaukaus): a quarter of the hit probability, 6 mil dispersion.</summary>
    public static FireModeValues Snap { get; } = new(HitFactor: 0.25, DispersionMils: 6);

    /// <summary>
    /// The suppression rules of backlog 0003: crack radius r = 2 m, a muzzle-flash-only target doubles the miss distance,
    /// cover 1 / 0.8 / 0.6 standing / crouching / prone, suppression ends below 0.25 τ, light below 2 τ, heavy from 5 τ,
    /// and a round passing within 10 m puts an agent under fire.
    /// </summary>
    public static SuppressionRules Rules { get; } = new(
        Conscript: Conscript,
        Regular: Regular,
        SpecialForces: SpecialForces,
        Aimed: Aimed,
        Snap: Snap,
        CrackRadiusMeters: 2,
        FlashObservationFactor: 2,
        StandingCover: 1.0,
        CrouchingCover: 0.8,
        ProneCover: 0.6,
        ReleaseFraction: 0.25,
        LightFactor: 2,
        HeavyFactor: 5,
        UnderFireMeters: 10);

    /// <summary>
    /// The drill's values (kept in <see cref="Doctrine"/>): 3 snap shots, positions searched within 20 m on a 2 m grid,
    /// no fire with an own fighter within 30° of the muzzle direction, 20 % of the rounds kept in reserve.
    /// </summary>
    public static ContactDrillRules Drill { get; } = new(
        SnapShots: 3, CoverSearchMeters: 20, SearchGridMeters: 2, FireSectorHalfAngleDegrees: 30, ReserveFraction: 0.2);

    /// <summary>Machine gun 7.62 (values open, to be calibrated): S = 1.5, bursts of 5, 800 m, 500 rounds.</summary>
    public static WeaponProfile MachineGun { get; } = new(
        HitProbabilityAtZero: 0.6,
        HitFalloffMeters: 200,
        MaxRangeMeters: 800,
        SecondsPerShot: 2,
        Rounds: 500,
        SoundRangeMeters: 2000,
        FlashSeconds: 2,
        SuppressionPerRound: 1.5,
        RoundsPerBurst: 5,
        SnapSecondsPerShot: 1);

    /// <summary>Heavy machine gun 12.7 (values open, to be calibrated): S = 4, bursts of 5, 1500 m, 200 rounds.</summary>
    public static WeaponProfile HeavyMachineGun { get; } = new(
        HitProbabilityAtZero: 0.6,
        HitFalloffMeters: 300,
        MaxRangeMeters: 1500,
        SecondsPerShot: 3,
        Rounds: 200,
        SoundRangeMeters: 3000,
        FlashSeconds: 2,
        SuppressionPerRound: 4.0,
        RoundsPerBurst: 5,
        SnapSecondsPerShot: 1.5);

    /// <summary>
    /// The weapon with a hit probability of zero: every round misses, but it still suppresses (pressure does not depend
    /// on the hit curve). For tests about suppression alone, so no casualty gets in the way.
    /// </summary>
    public static WeaponProfile NeverHits(WeaponProfile weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon with { HitProbabilityAtZero = 0 };
    }

    /// <summary>The values of an experience level.</summary>
    public static ExperienceValues For(Experience experience) => experience switch
    {
        Experience.Conscript => Conscript,
        Experience.SpecialForces => SpecialForces,
        _ => Regular,
    };

    /// <summary>Cover factor of a posture: 1 standing, 0.8 crouching, 0.6 prone.</summary>
    public static double Cover(Posture posture) => posture switch
    {
        Posture.Crouching => 0.8,
        Posture.Prone => 0.6,
        _ => 1.0,
    };

    /// <summary>Expected miss distance at the target: m = d · σ_M · σ_X · Q, with σ_M in radians and Q = 2 for flash only.</summary>
    public static double MissDistance(double distance, FireMode mode, Experience shooter, bool muzzleFlashOnly = false) =>
        distance * ((mode == FireMode.Snap ? Snap : Aimed).DispersionMils / 1000) * For(shooter).Dispersion *
        (muzzleFlashOnly ? 2 : 1);

    /// <summary>Miss distance for an agent at perpendicular distance <paramref name="offset"/> from the round's path: √(m² + b²).</summary>
    public static double MissDistanceBeside(double missAtTarget, double offset) =>
        Math.Sqrt((missAtTarget * missAtTarget) + (offset * offset));

    /// <summary>Pressure from one round passing at <paramref name="missDistance"/>: S · exp(−m / 2 m) · T · Cover(posture).</summary>
    public static double RoundPressure(
        WeaponProfile weapon, double missDistance, Posture posture = Posture.Standing, double transmission = 1)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.SuppressionPerRound * Math.Exp(-missDistance / 2) * transmission * Cover(posture);
    }

    /// <summary>
    /// Pressure one shot event (a single round or a burst of n) adds to its target in the open:
    /// n · S · exp(−m / 2 m) · Cover(target posture), with m from <see cref="MissDistance"/>.
    /// </summary>
    public static double ShotPressure(
        WeaponProfile weapon, double distance, FireMode mode = FireMode.Aimed, Experience shooter = Experience.Regular,
        Posture target = Posture.Standing)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.RoundsPerBurst * RoundPressure(weapon, MissDistance(distance, mode, shooter), target);
    }

    /// <summary>How long a pressure <paramref name="level"/> takes to decay below <paramref name="below"/>: H · log2(L / below).</summary>
    public static double SecondsUntilBelow(double level, double below, Experience agent) =>
        For(agent).PressureHalfLifeSeconds * Math.Log2(level / below);

    /// <summary>How long suppression lasts after the last shot that left pressure <paramref name="level"/>: until it is below 0.25 τ.</summary>
    public static double ReleaseSeconds(double level, Experience agent) =>
        SecondsUntilBelow(level, 0.25 * For(agent).SuppressionThreshold, agent);

    /// <summary>Pressure <paramref name="level"/> after <paramref name="seconds"/> of decay: L · 2^(−t / H).</summary>
    public static double Decayed(double level, double seconds, Experience agent) =>
        level * Math.Pow(2, -seconds / For(agent).PressureHalfLifeSeconds);
}
