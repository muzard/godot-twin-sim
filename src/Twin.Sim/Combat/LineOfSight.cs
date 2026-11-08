using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Sim.Combat;

/// <summary>A sight line between two points.</summary>
/// <param name="Clear">False if the ground or a building blocks it.</param>
/// <param name="Transmission">Forest transmission T = exp(−L_forest / λ); 1 without forest, 0 when blocked.</param>
public sealed record SightLine(bool Clear, double Transmission)
{
    /// <summary>True if a target at the end of the line is visible; a muzzle flash lowers the transmission needed.</summary>
    public bool IsVisible(CombatRules rules, bool muzzleFlash = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Clear && Transmission >= (muzzleFlash ? rules.FlashTransmission : rules.VisibleTransmission);
    }
}

/// <summary>
/// Line of sight (backlog 0002): the line is sampled every <see cref="CombatRules.SightSampleMeters"/>. A sample below
/// the ground or in a building cell blocks it. Samples in forest cells below the canopy add to the forest length.
/// </summary>
public static class LineOfSight
{
    /// <summary>The sight line from <paramref name="from"/> (an eye) to <paramref name="to"/> (an aim point).</summary>
    public static SightLine Between(IWorldQuery world, Vec3 from, Vec3 to, CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rules);
        var horizontal = from.Xy.DistanceTo(to.Xy);
        var samples = Math.Max(1, (int)Math.Ceiling(horizontal / rules.SightSampleMeters));
        var step = from.DistanceTo(to) / samples;
        var forest = 0.0;
        for (var i = 1; i < samples; i++)
        {
            var t = (double)i / samples;
            var p = from + ((to - from) * t);
            if (!world.IsInside(p.X, p.Y))
            {
                return new SightLine(false, 0);
            }

            var ground = world.GroundHeight(p.X, p.Y);
            var terrain = world.ClassAt(p.X, p.Y);
            if (p.Z < ground || terrain == TerrainClass.Building)
            {
                return new SightLine(false, 0);
            }

            if (terrain == TerrainClass.Forest && p.Z < ground + rules.CanopyHeightMeters)
            {
                forest += step;
            }
        }

        return new SightLine(true, Math.Exp(-forest / rules.ForestAttenuationMeters));
    }
}

/// <summary>Hit probability and the hit draw (backlog 0002).</summary>
public static class HitModel
{
    /// <summary>
    /// P(hit) = P0 · exp(−d / falloff) · T · Size(target posture) · S(shooter suppressed) · W(shooter wounded),
    /// zero beyond the weapon's maximum range.
    /// </summary>
    public static double Probability(
        WeaponProfile weapon, CombatRules rules, double distance, double transmission, Posture targetPosture,
        bool shooterSuppressed, bool shooterWounded)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(rules);
        if (distance > weapon.MaxRangeMeters)
        {
            return 0;
        }

        return weapon.HitProbabilityAtZero * Math.Exp(-distance / weapon.HitFalloffMeters) * transmission
            * rules.For(targetPosture).SizeFactor
            * (shooterSuppressed ? rules.SuppressedHitFactor : 1)
            * (shooterWounded ? rules.WoundedHitFactor : 1);
    }

    /// <summary>Draws one hit or miss with probability <paramref name="probability"/>. Always draws exactly one number.</summary>
    public static bool Roll(DeterministicRandom random, double probability)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.NextDouble() < probability;
    }
}
