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
    /// The reference squad in formation order (front to back, backlog 0004 S2a): the order of the
    /// <see cref="WatchSectors"/> tables.
    /// </summary>
    public static IReadOnlyList<string> FormationOrder { get; } = ["pj", "k1", "k2", "rj", "j1", "j2", "vj", "kk", "t1"];

    /// <summary>
    /// Each formation place's watch direction, in degrees to the right of the squad's direction of travel, in formation
    /// order. File: ahead, then alternately right and left, the RJ ahead, the last one back. Wedge: the apex and the RJ
    /// ahead, the arms out to their side, the rear corners back left and back right. [PROPOSAL]
    /// </summary>
    public static WatchSectors WatchSectors { get; } = new(
        FileDegrees: [0, 90, -90, 0, 90, -90, 90, -90, 180],
        WedgeDegrees: [0, -45, 45, 0, 90, -90, 90, -135, 135]);

    /// <summary>The squad doctrine of <see cref="SpecSquad.Doctrine"/> with the watch sectors and the voice range.</summary>
    public static SquadDoctrine Doctrine { get; } = SpecSquad.Doctrine with
    {
        WatchSectors = WatchSectors,
        VoiceRangeMeters = VoiceRangeMeters,
    };

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

    /// <summary>The watch angle of the reference squad member <paramref name="unitId"/> in <paramref name="formation"/>.</summary>
    public static double WatchDegrees(SquadFormation formation, string unitId)
    {
        var place = FormationOrder.ToList().IndexOf(unitId);
        if (place < 0)
        {
            throw new ArgumentException($"'{unitId}' is not in the reference squad.", nameof(unitId));
        }

        return (formation == SquadFormation.Wedge ? WatchSectors.WedgeDegrees : WatchSectors.FileDegrees)[place];
    }

    /// <summary><paramref name="direction"/> turned <paramref name="degreesRight"/> clockwise (to the right), unit length.</summary>
    public static Vec2 Turned(Vec2 direction, double degreesRight)
    {
        var d = direction.Normalized();
        var a = degreesRight * Math.PI / 180;
        return new Vec2((d.X * Math.Cos(a)) + (d.Y * Math.Sin(a)), (-d.X * Math.Sin(a)) + (d.Y * Math.Cos(a)));
    }
}
