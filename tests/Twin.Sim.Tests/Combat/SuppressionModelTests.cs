using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// Suppression as fire pressure (backlog 0003, SU), the pure part:
/// <list type="bullet">
/// <item>A round passes the target at the expected miss distance m = d · σ_M · σ_X · Q (σ_M 3 mil aimed, 6 mil snap;
/// σ_X by experience; Q = 2 for a target seen only by its muzzle flash). No random numbers.</item>
/// <item>It passes anyone else in front of the shooter at √(m² + b²), with b the distance from the round's path (the ray
/// from the shooter through the target, out to the weapon's maximum range).</item>
/// <item>Pressure from one round: p = S · exp(−m / 2 m) · T · Cover(posture), Cover 1 / 0.8 / 0.6.</item>
/// </list>
/// </summary>
public class SuppressionModelTests
{
    private static readonly CombatRules Rules = SpecShooting.Rules;
    private static readonly WeaponProfile Rifle = SpecShooting.Rifle;

    [Theory]
    [InlineData(100, FireMode.Aimed, Experience.Regular, ObservationQuality.Seen)]
    [InlineData(300, FireMode.Aimed, Experience.Regular, ObservationQuality.Seen)]
    [InlineData(300, FireMode.Snap, Experience.Regular, ObservationQuality.Seen)]
    [InlineData(300, FireMode.Aimed, Experience.SpecialForces, ObservationQuality.Seen)]
    [InlineData(300, FireMode.Aimed, Experience.Conscript, ObservationQuality.Seen)]
    [InlineData(300, FireMode.Snap, Experience.Regular, ObservationQuality.MuzzleFlash)]
    public void SU1_miss_distance_grows_with_range_and_shrinks_with_aim_and_skill(
        double distance, FireMode mode, Experience shooter, ObservationQuality observation)
    {
        var expected = SpecSuppression.MissDistance(distance, mode, shooter, observation == ObservationQuality.MuzzleFlash);

        Assert.Equal(expected, SuppressionModel.MissDistance(Rules, distance, mode, shooter, observation), 12);
    }

    [Fact]
    public void SU1_the_reference_values_are_three_and_six_mils()
    {
        // Regular, seen, at 100 m: 0.3 m aimed, 0.6 m snap; special forces aimed 0.18 m; flash only doubles it.
        Assert.Equal(0.3, SuppressionModel.MissDistance(Rules, 100, FireMode.Aimed, Experience.Regular, ObservationQuality.Seen), 12);
        Assert.Equal(0.6, SuppressionModel.MissDistance(Rules, 100, FireMode.Snap, Experience.Regular, ObservationQuality.Seen), 12);
        Assert.Equal(0.18, SuppressionModel.MissDistance(Rules, 100, FireMode.Aimed, Experience.SpecialForces, ObservationQuality.Seen), 12);
        Assert.Equal(0.6, SuppressionModel.MissDistance(Rules, 100, FireMode.Aimed, Experience.Regular, ObservationQuality.MuzzleFlash), 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.3)]
    [InlineData(1.5)]
    [InlineData(5)]
    public void SU2_a_round_suppresses_less_the_farther_it_passes(double missDistance)
    {
        Assert.Equal(1.2 * Math.Exp(-missDistance / 2),
            SuppressionModel.RoundPressure(Rifle, Rules, missDistance, transmission: 1, Posture.Standing), 12);
    }

    [Fact]
    public void SU2_a_heavier_weapon_suppresses_more_per_round()
    {
        var rifle = SuppressionModel.RoundPressure(Rifle, Rules, 1, transmission: 1, Posture.Standing);
        var heavy = SuppressionModel.RoundPressure(SpecSuppression.HeavyMachineGun, Rules, 1, transmission: 1, Posture.Standing);

        Assert.Equal(4.0 / 1.2, heavy / rifle, 12);
    }

    [Theory]
    [InlineData(Posture.Standing, 1.0)]
    [InlineData(Posture.Crouching, 0.8)]
    [InlineData(Posture.Prone, 0.6)]
    public void SU3_cover_reduces_pressure(Posture posture, double cover)
    {
        var open = SuppressionModel.RoundPressure(Rifle, Rules, 0.5, transmission: 1, Posture.Standing);

        Assert.Equal(open * cover, SuppressionModel.RoundPressure(Rifle, Rules, 0.5, transmission: 1, posture), 12);
        Assert.Equal(open * cover * 0.4, SuppressionModel.RoundPressure(Rifle, Rules, 0.5, transmission: 0.4, posture), 12);
    }

    [Fact]
    public void SU3_a_blocked_line_gives_no_pressure()
    {
        // A blocked sight line (ridge, building) has transmission 0.
        Assert.Equal(0, SuppressionModel.RoundPressure(Rifle, Rules, 0.5, transmission: 0, Posture.Standing));
    }

    [Fact]
    public void SU12_a_round_passes_others_at_their_distance_from_its_path()
    {
        var shooter = new Vec2(0, 0);
        var target = new Vec2(100, 0);
        const double Miss = 0.3;
        const double Range = 600;

        // Beside the target, 3 m off the path.
        Assert.Equal(SpecSuppression.MissDistanceBeside(Miss, 3),
            SuppressionModel.MissDistanceBeside(shooter, target, new Vec2(100, 3), Miss, Range)!.Value, 12);
        // Between shooter and target, and beyond the target: the path continues to the weapon's range.
        Assert.Equal(SpecSuppression.MissDistanceBeside(Miss, 4),
            SuppressionModel.MissDistanceBeside(shooter, target, new Vec2(50, -4), Miss, Range)!.Value, 12);
        Assert.Equal(SpecSuppression.MissDistanceBeside(Miss, 2),
            SuppressionModel.MissDistanceBeside(shooter, target, new Vec2(400, 2), Miss, Range)!.Value, 12);
        // Behind the shooter, or beyond the weapon's range: not passed at all.
        Assert.Null(SuppressionModel.MissDistanceBeside(shooter, target, new Vec2(-20, 1), Miss, Range));
        Assert.Null(SuppressionModel.MissDistanceBeside(shooter, target, new Vec2(650, 1), Miss, Range));
    }
}
