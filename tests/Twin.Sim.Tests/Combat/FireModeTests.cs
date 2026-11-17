using Twin.Sim.Combat;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// Fire modes (backlog 0003, M): aimed fire keeps the 0002 hit probability; a snap shot (heittolaukaus) hits with a quarter
/// of it. P(hit) per round = 0002 formula · A(experience) · K(mode). A burst of n rounds is one shot event with one draw:
/// P(burst hits) = 1 − (1 − p)^n.
/// </summary>
public class FireModeTests
{
    private static readonly WeaponProfile Rifle = SpecShooting.Rifle;
    private static readonly CombatRules Rules = SpecShooting.Rules;

    private static double P(double distance, FireMode mode, Experience experience = Experience.Regular) =>
        HitModel.Probability(Rifle, Rules, distance, transmission: 1, Posture.Standing, shooterSuppressed: false,
            shooterWounded: false, experience, mode);

    [Fact]
    public void M1_a_snap_shot_hits_with_a_quarter_of_the_aimed_probability()
    {
        Assert.Equal(0.25, SpecSuppression.Snap.HitFactor, 12);
        Assert.Equal(P(100, FireMode.Aimed) * 0.25, P(100, FireMode.Snap), 12);
    }

    [Fact]
    public void M2_special_forces_at_close_range_land_hits_with_snap_shots()
    {
        // 0.6 · e^(−25/150) · 1.3 · 0.25 ≈ 0.17 per round: return fire aims to suppress, but special forces at close
        // range are expected to land effective hits (decision of 2026-11-17).
        var expected = SpecShooting.RangeProbability(25) * 1.3 * 0.25;
        var probability = P(25, FireMode.Snap, Experience.SpecialForces);
        Assert.Equal(expected, probability, 12);
        Assert.InRange(probability, 0.16, 0.18);

        // Fixed seed, ±4σ: the sampled rate follows the probability.
        const int Shots = 10_000;
        var random = new DeterministicRandom(seed: 1);
        var hits = Enumerable.Range(0, Shots).Count(_ => HitModel.Roll(random, probability));
        var sigma = Math.Sqrt(Shots * probability * (1 - probability));
        Assert.InRange(hits, (Shots * probability) - (4 * sigma), (Shots * probability) + (4 * sigma));
    }

    [Theory]
    [InlineData(0.1, 1)]
    [InlineData(0.1, 5)]
    [InlineData(0.3, 5)]
    public void M5_a_burst_hits_when_any_of_its_rounds_would(double perRound, int rounds)
    {
        Assert.Equal(1 - Math.Pow(1 - perRound, rounds), HitModel.BurstProbability(perRound, rounds), 12);
    }
}
