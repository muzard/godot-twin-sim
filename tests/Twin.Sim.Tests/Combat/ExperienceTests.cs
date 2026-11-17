using Twin.Sim.Combat;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// Experience (backlog 0003, X): conscript or reservist, regular (the default) and special forces. Experience scales the
/// hit probability by the accuracy factor A: 0.8, 1.0 and 1.3. Regular aimed fire keeps the 0002 hit probability.
/// </summary>
public class ExperienceTests
{
    private static readonly WeaponProfile Rifle = SpecShooting.Rifle;
    private static readonly CombatRules Rules = SpecShooting.Rules;

    private static double P(double distance, Experience experience) =>
        HitModel.Probability(Rifle, Rules, distance, transmission: 1, Posture.Standing, shooterSuppressed: false,
            shooterWounded: false, experience, FireMode.Aimed);

    [Fact]
    public void X1_the_default_experience_is_regular_and_keeps_the_0002_hit_probability()
    {
        var combatant = new Combatant(Side.Blue, RulesOfEngagement.ReturnFire, Rifle);

        Assert.Equal(Experience.Regular, combatant.Experience);
        Assert.Equal(SpecShooting.RangeProbability(100),
            HitModel.Probability(Rifle, Rules, 100, transmission: 1, Posture.Standing, shooterSuppressed: false, shooterWounded: false),
            12);
        Assert.Equal(SpecShooting.RangeProbability(100), P(100, Experience.Regular), 12);
    }

    [Theory]
    [InlineData(Experience.Conscript, 0.8)]
    [InlineData(Experience.Regular, 1.0)]
    [InlineData(Experience.SpecialForces, 1.3)]
    public void X2_experience_scales_the_hit_probability(Experience experience, double accuracy)
    {
        Assert.Equal(accuracy, SpecSuppression.For(experience).Accuracy, 12);
        Assert.Equal(SpecShooting.RangeProbability(100) * accuracy, P(100, experience), 12);
    }
}
