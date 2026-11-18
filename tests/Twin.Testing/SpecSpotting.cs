using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;

namespace Twin.Testing;

/// <summary>
/// The values and reference formula of backlog 0004 S5a1 (spotting), the counterpart of <see cref="SpecShooting"/>.
/// Tests pass these explicitly, so calibrating the simulation's defaults later does not change what a test checks.
/// <para>
/// An observer spots an enemy after watching it continuously for
/// <c>t_spot = t_base(experience) · (1 + d / 100 m) · A(angle) / SizeFactor(target posture) / T</c>, where <c>d</c> is
/// the distance from the observer's eye to the enemy's aim point, <c>A</c> is 1 within the focus cone (±15° of the
/// watch direction), 3 elsewhere in the field of view (±90°) and never outside it, and <c>T</c> is the 0002 forest
/// transmission.
/// </para>
/// </summary>
public static class SpecSpotting
{
    /// <summary>A conscript's base spotting time (product owner, 2026-11-18).</summary>
    public const double ConscriptSeconds = 1.5;

    /// <summary>A regular soldier's base spotting time (product owner, 2026-11-18).</summary>
    public const double RegularSeconds = 1.25;

    /// <summary>A special forces soldier's base spotting time (product owner, 2026-11-18).</summary>
    public const double SpecialForcesSeconds = 1.0;

    /// <summary>At this distance the spotting time is twice the base time. [PROPOSAL]</summary>
    public const double ReferenceDistanceMeters = 100;

    /// <summary>Half the focus cone: within it the angle factor is 1. [PROPOSAL]</summary>
    public const double FocusHalfAngleDegrees = 15;

    /// <summary>Half the field of view: outside it nothing is spotted. Exactly on the edge counts as outside. [PROPOSAL]</summary>
    public const double FieldOfViewHalfAngleDegrees = 90;

    /// <summary>The angle factor in the field of view outside the focus cone (product owner).</summary>
    public const double FieldOfViewFactor = 3;

    /// <summary>
    /// A spoken spot report reaches squad mates within this distance. ⚠ A placeholder: voice range must be modelled
    /// better later (terrain, noise, wind, loudness); see backlog 0004, "S5a1 spec decisions". [PROPOSAL]
    /// </summary>
    public const double VoiceRangeMeters = 30;

    /// <summary>The observation rules above.</summary>
    public static ObservationRules Observation { get; } = new(
        ConscriptSeconds: ConscriptSeconds,
        RegularSeconds: RegularSeconds,
        SpecialForcesSeconds: SpecialForcesSeconds,
        ReferenceDistanceMeters: ReferenceDistanceMeters,
        FocusHalfAngleDegrees: FocusHalfAngleDegrees,
        FieldOfViewHalfAngleDegrees: FieldOfViewHalfAngleDegrees,
        FieldOfViewFactor: FieldOfViewFactor);

    /// <summary>The combat rules of <see cref="SpecShooting.Rules"/> with spotting switched on.</summary>
    public static CombatRules Rules { get; } = SpecShooting.Rules with { Observation = Observation };

    /// <summary>
    /// The squad doctrine of <see cref="SpecSquad.Doctrine"/> with the voice range. Where squad members watch comes from
    /// their S2c observation sectors (<see cref="Twin.Sim.Units.Unit.ObservationSectors"/>): each sector's centre is a
    /// watch direction, and the best angle factor of them counts (merge with S2c, product owner, 2026-11-18).
    /// </summary>
    public static SquadDoctrine Doctrine { get; } = SpecSquad.Doctrine with { VoiceRangeMeters = VoiceRangeMeters };

    /// <summary>The base spotting time of an observer with the given experience.</summary>
    public static double BaseSeconds(Experience experience) => experience switch
    {
        Experience.Conscript => ConscriptSeconds,
        Experience.SpecialForces => SpecialForcesSeconds,
        _ => RegularSeconds,
    };

    /// <summary>The 0002 size factor of a target in the given posture (1, 0.7 or 0.4).</summary>
    public static double SizeFactor(Posture posture) => posture switch
    {
        Posture.Crouching => SpecShooting.Crouching.SizeFactor,
        Posture.Prone => SpecShooting.Prone.SizeFactor,
        _ => SpecShooting.Standing.SizeFactor,
    };

    /// <summary>
    /// The spotting time <c>t_base · (1 + d / 100 m) · angleFactor / SizeFactor(target) / transmission</c>, with
    /// <paramref name="distanceMeters"/> from the observer's eye to the target's aim point.
    /// </summary>
    public static double SpotSeconds(
        Experience observer, double distanceMeters, double angleFactor = 1, Posture target = Posture.Standing,
        double transmission = 1) =>
        BaseSeconds(observer) * (1 + (distanceMeters / ReferenceDistanceMeters)) * angleFactor / SizeFactor(target) /
        transmission;

    /// <summary><paramref name="direction"/> turned <paramref name="degreesRight"/> clockwise (to the right), unit length.</summary>
    public static Vec2 Turned(Vec2 direction, double degreesRight)
    {
        var d = direction.Normalized();
        var a = degreesRight * Math.PI / 180;
        return new Vec2((d.X * Math.Cos(a)) + (d.Y * Math.Sin(a)), (-d.X * Math.Sin(a)) + (d.Y * Math.Cos(a)));
    }
}
