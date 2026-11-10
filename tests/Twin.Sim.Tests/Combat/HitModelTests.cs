using Twin.Sim.Combat;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// Hit probability (backlog 0002, F and PO5):
/// P(hit) = 0.6 · exp(−d / 150 m) · T · Size(target posture) · S(shooter suppressed) · W(shooter wounded), zero beyond 600 m.
/// </summary>
public class HitModelTests
{
    private static readonly WeaponProfile Rifle = SpecShooting.Rifle;
    private static readonly CombatRules Rules = SpecShooting.Rules;

    private static double P(
        double distance, double transmission = 1, Posture target = Posture.Standing, bool suppressed = false, bool wounded = false) =>
        HitModel.Probability(Rifle, Rules, distance, transmission, target, suppressed, wounded);

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(300)]
    public void F1_hit_probability_falls_with_distance_as_specified(double distance)
    {
        Assert.Equal(0.6 * Math.Exp(-distance / 150), P(distance), 12);
    }

    [Fact]
    public void F2_beyond_maximum_range_the_hit_probability_is_zero()
    {
        Assert.True(P(600) > 0);
        Assert.Equal(0, P(601));
    }

    [Fact]
    public void F3_forest_transmission_multiplies_the_hit_probability()
    {
        Assert.Equal(P(100) / 2, P(100, transmission: 0.5), 12);
    }

    [Fact]
    public void F4_a_suppressed_or_wounded_shooter_hits_less_often()
    {
        var open = P(100);

        Assert.Equal(open * 0.3, P(100, suppressed: true), 12);
        Assert.Equal(open * 0.5, P(100, wounded: true), 12);
        Assert.Equal(open * 0.15, P(100, suppressed: true, wounded: true), 12);
    }

    [Fact]
    public void PO5_a_smaller_posture_is_harder_to_hit()
    {
        var standing = P(100);

        Assert.Equal(standing * 0.7, P(100, target: Posture.Crouching), 12);
        Assert.Equal(standing * 0.4, P(100, target: Posture.Prone), 12);
    }

    [Fact]
    public void F8_sampled_hit_rate_matches_the_hit_probability()
    {
        // Fixed seed, so the result is the same every run; the ±4σ tolerance keeps the test from fitting one seed.
        const int Shots = 10_000;
        const double Probability = 0.3;
        var random = new DeterministicRandom(seed: 1);

        var hits = Enumerable.Range(0, Shots).Count(_ => HitModel.Roll(random, Probability));

        var sigma = Math.Sqrt(Shots * Probability * (1 - Probability));
        Assert.InRange(hits, (Shots * Probability) - (4 * sigma), (Shots * Probability) + (4 * sigma));
    }
}
