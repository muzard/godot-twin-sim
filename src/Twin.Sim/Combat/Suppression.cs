using Twin.Sim.Geometry;

namespace Twin.Sim.Combat;

/// <summary>A fighter's experience (backlog 0003): it sets accuracy, dispersion, reaction time and how fire affects it.</summary>
public enum Experience
{
    /// <summary>Conscript or reservist.</summary>
    Conscript,

    /// <summary>Regular soldier: the default.</summary>
    Regular,

    /// <summary>Special forces.</summary>
    SpecialForces,
}

/// <summary>How a shot is fired (backlog 0003).</summary>
public enum FireMode
{
    /// <summary>An aimed shot: the 0002 hit probability.</summary>
    Aimed,

    /// <summary>A snap shot (heittolaukaus): quick, less accurate, fired while taking cover.</summary>
    Snap,
}

/// <summary>How well a shooter sees its target when it fires (backlog 0003).</summary>
public enum ObservationQuality
{
    /// <summary>Seen: the sight line is clear and the forest transmission is at least the visibility threshold.</summary>
    Seen,

    /// <summary>Seen only by its muzzle flash: only snap shots, and the rounds pass farther away.</summary>
    MuzzleFlash,
}

/// <summary>Where an agent is in the immediate action drill under fire (backlog 0003).</summary>
public enum ContactDrillStage
{
    /// <summary>Not running the drill.</summary>
    None,

    /// <summary>Dropped prone; fires its snap shots after its reaction time.</summary>
    SnapShots,

    /// <summary>In danger of being hit: moving to the nearest cover.</summary>
    MovingToCover,

    /// <summary>Moving to a firing position, forward by default.</summary>
    MovingToFiringPosition,

    /// <summary>Returning fire with aimed shots (snap shots at a target seen only by its muzzle flash).</summary>
    ReturningFire,
}

/// <summary>What an experience level means in a fight (backlog 0003).</summary>
/// <param name="Accuracy">Accuracy factor A: multiplies the hit probability.</param>
/// <param name="Dispersion">Dispersion factor σ_X: multiplies the miss distance of its rounds.</param>
/// <param name="ReactionSeconds">Time from coming under fire to the first snap shot.</param>
/// <param name="SuppressionThreshold">τ: the fire pressure at which the agent is suppressed.</param>
/// <param name="PressureHalfLifeSeconds">H: the fire pressure on the agent halves in this time.</param>
public sealed record ExperienceValues(
    double Accuracy, double Dispersion, double ReactionSeconds, double SuppressionThreshold, double PressureHalfLifeSeconds);

/// <summary>What a fire mode means (backlog 0003).</summary>
/// <param name="HitFactor">K: multiplies the hit probability.</param>
/// <param name="DispersionMils">σ_M: the angular dispersion of the rounds, in mils (1 mil = 0.001 rad).</param>
public sealed record FireModeValues(double HitFactor, double DispersionMils);

/// <summary>
/// The rules of suppression as fire pressure (backlog 0003). A round passing an agent at miss distance m adds
/// S · exp(−m / r) · T · Cover(posture) to the agent's pressure, which halves every H. The agent is suppressed from τ
/// until the pressure falls below <see cref="ReleaseFraction"/> τ, and heavily suppressed from <see cref="HeavyFactor"/> τ.
/// </summary>
/// <param name="Conscript">Values of a conscript or reservist.</param>
/// <param name="Regular">Values of a regular soldier.</param>
/// <param name="SpecialForces">Values of special forces.</param>
/// <param name="Aimed">Values of aimed fire.</param>
/// <param name="Snap">Values of snap shots.</param>
/// <param name="CrackRadiusMeters">r: the pressure of a round falls as exp(−m / r) with its miss distance m.</param>
/// <param name="FlashObservationFactor">Q: multiplies the miss distance when the target is seen only by its muzzle flash.</param>
/// <param name="StandingCover">Cover factor of a standing agent.</param>
/// <param name="CrouchingCover">Cover factor of a crouching agent.</param>
/// <param name="ProneCover">Cover factor of a prone agent.</param>
/// <param name="ReleaseFraction">Suppression ends when the pressure falls below this times τ.</param>
/// <param name="LightFactor">Below this times τ a suppressed agent is lightly suppressed: in the drill it may still crawl.</param>
/// <param name="HeavyFactor">From this times τ the agent is heavily suppressed: it does not fire at all.</param>
/// <param name="UnderFireMeters">A round passing an agent within this distance puts it under fire.</param>
public sealed record SuppressionRules(
    ExperienceValues Conscript,
    ExperienceValues Regular,
    ExperienceValues SpecialForces,
    FireModeValues Aimed,
    FireModeValues Snap,
    double CrackRadiusMeters,
    double FlashObservationFactor,
    double StandingCover,
    double CrouchingCover,
    double ProneCover,
    double ReleaseFraction,
    double LightFactor,
    double HeavyFactor,
    double UnderFireMeters)
{
    /// <summary>Backlog 0003 starting values.</summary>
    public static SuppressionRules Default { get; } = new(
        Conscript: new ExperienceValues(0.8, 1.3, 2.0, 0.8, 6),
        Regular: new ExperienceValues(1.0, 1.0, 1.5, 1.0, 4),
        SpecialForces: new ExperienceValues(1.3, 0.6, 0.5, 1.5, 2.5),
        Aimed: new FireModeValues(1, 3),
        Snap: new FireModeValues(0.25, 6),
        CrackRadiusMeters: 2,
        FlashObservationFactor: 2,
        StandingCover: 1.0,
        CrouchingCover: 0.8,
        ProneCover: 0.6,
        ReleaseFraction: 0.25,
        LightFactor: 2,
        HeavyFactor: 5,
        UnderFireMeters: 10);

    /// <summary>The values of an experience level.</summary>
    public ExperienceValues For(Experience experience) => experience switch
    {
        Experience.Conscript => Conscript,
        Experience.SpecialForces => SpecialForces,
        _ => Regular,
    };

    /// <summary>The values of a fire mode.</summary>
    public FireModeValues For(FireMode mode) => mode == FireMode.Snap ? Snap : Aimed;

    /// <summary>The cover factor of a posture.</summary>
    public double Cover(Posture posture) => posture switch
    {
        Posture.Crouching => CrouchingCover,
        Posture.Prone => ProneCover,
        _ => StandingCover,
    };
}

/// <summary>Suppression as fire pressure (backlog 0003): miss distances and the pressure a round adds. No random numbers.</summary>
public static class SuppressionModel
{
    /// <summary>
    /// Expected miss distance at the target: m = d · σ_M · σ_X · Q, with σ_M in radians and Q the flash factor when the
    /// target is seen only by its muzzle flash.
    /// </summary>
    public static double MissDistance(
        CombatRules rules, double distance, FireMode mode, Experience shooter, ObservationQuality observation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var suppression = rules.Suppression;
        return distance * (suppression.For(mode).DispersionMils / 1000) * suppression.For(shooter).Dispersion *
            (observation == ObservationQuality.MuzzleFlash ? suppression.FlashObservationFactor : 1);
    }

    /// <summary>
    /// The distance at which a round passes another agent: √(m² + b²), with b the agent's distance from the round's path,
    /// the ray from the shooter through the target out to the weapon's maximum range. Null for an agent behind the
    /// shooter or beyond the range: the round does not pass it.
    /// </summary>
    public static double? MissDistanceBeside(Vec2 shooter, Vec2 target, Vec2 other, double missAtTarget, double maxRangeMeters)
    {
        var direction = (target - shooter).Normalized();
        var along = (other - shooter).Dot(direction);
        if (along <= 0 || along > maxRangeMeters)
        {
            return null;
        }

        var offset = (other - (shooter + (direction * along))).Length;
        return Math.Sqrt((missAtTarget * missAtTarget) + (offset * offset));
    }

    /// <summary>
    /// Pressure from one round passing an agent at <paramref name="missDistance"/>: S · exp(−m / r) · T · Cover(posture),
    /// with T the forest transmission of the shooter's line to the agent (0 when the line is blocked).
    /// </summary>
    public static double RoundPressure(
        WeaponProfile weapon, CombatRules rules, double missDistance, double transmission, Posture posture)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(rules);
        var suppression = rules.Suppression;
        return weapon.SuppressionPerRound * Math.Exp(-missDistance / suppression.CrackRadiusMeters) * transmission *
            suppression.Cover(posture);
    }
}
