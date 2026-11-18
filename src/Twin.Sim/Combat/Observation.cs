namespace Twin.Sim.Combat;

/// <summary>
/// How fighters spot enemies (backlog 0004 S5a1). An observer spots an enemy after watching it continuously for
/// <c>t_spot = t_base(experience) · (1 + d / ReferenceDistanceMeters) · A(angle) / SizeFactor(target posture) / T</c>, where
/// <c>d</c> runs from the observer's eye to the enemy's aim point, <c>T</c> is the forest transmission of the sight line
/// (<see cref="SightLine"/>), and <c>A</c> is 1 within the focus cone around the watch direction,
/// <see cref="FieldOfViewFactor"/> elsewhere in the field of view, and nothing is spotted outside it (exactly on an edge
/// counts as outside the field of view and inside the focus cone). A muzzle flash in the field of view spots its shooter at
/// once. The values are data so night-vision devices and the like can change them.
/// </summary>
/// <param name="ConscriptSeconds">A conscript's base spotting time.</param>
/// <param name="RegularSeconds">A regular soldier's base spotting time.</param>
/// <param name="SpecialForcesSeconds">A special forces soldier's base spotting time.</param>
/// <param name="ReferenceDistanceMeters">At this distance the spotting time is twice the base time.</param>
/// <param name="FocusHalfAngleDegrees">Half the focus cone, in degrees from the watch direction.</param>
/// <param name="FieldOfViewHalfAngleDegrees">Half the field of view, in degrees from the watch direction.</param>
/// <param name="FieldOfViewFactor">The angle factor in the field of view outside the focus cone.</param>
public sealed record ObservationRules(
    double ConscriptSeconds,
    double RegularSeconds,
    double SpecialForcesSeconds,
    double ReferenceDistanceMeters,
    double FocusHalfAngleDegrees,
    double FieldOfViewHalfAngleDegrees,
    double FieldOfViewFactor)
{
    /// <summary>
    /// The starting values (backlog 0004, decision 19): base 1.5 / 1.25 / 1 s, doubled at 100 m, focus ±15°, field of
    /// view ±90°, three times as long outside the focus.
    /// </summary>
    public static ObservationRules Default { get; } = new(1.5, 1.25, 1.0, 100, 15, 90, 3);

    /// <summary>
    /// No spotting model, as in backlogs 0002 and 0003: a fighter knows every enemy it can see, at once and all round,
    /// and nothing is spotted, reported or halted for.
    /// </summary>
    public static ObservationRules Off { get; } = Default with { IsOff = true };

    /// <summary>True for <see cref="Off"/>: spotting is switched off.</summary>
    public bool IsOff { get; private init; }

    /// <summary>The base spotting time of an observer with the given experience.</summary>
    public double BaseSeconds(Experience experience) => experience switch
    {
        Experience.Conscript => ConscriptSeconds,
        Experience.SpecialForces => SpecialForcesSeconds,
        _ => RegularSeconds,
    };
}
