using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Experience in a fight (backlog 0003, X): special forces react faster (0.5 s against 2 s for a conscript), are harder
/// to suppress (τ 1.5 against 0.8) and recover sooner (pressure half-life 2.5 s against 6 s).
/// Red stands north of blue and fires rounds that never hit.
/// </summary>
public class ExperienceTests
{
    private static readonly Vec2 Blue = new(21, 21);

    private static ScenarioRun RedFiresAt(Experience blue, double distance, WeaponProfile redWeapon, RulesOfEngagement blueRules) =>
        Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: distance + 40).Build())
            .WithFighter("blue", Blue, Side.Blue, blueRules, experience: blue)
            .WithFighter("red", Blue + new Vec2(0, distance), Side.Red, RulesOfEngagement.HoldFire, redWeapon)
            .Build()
            .Order("red", new FireAt("blue"));

    private static WeaponProfile OneRifleRound => SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 };

    private static WeaponProfile OneMgBurst => SpecSuppression.NeverHits(SpecSuppression.MachineGun) with { Rounds = 5 };

    [Theory]
    [InlineData(Experience.SpecialForces)]
    [InlineData(Experience.Regular)]
    [InlineData(Experience.Conscript)]
    public void X3_special_forces_fire_their_first_snap_shot_sooner_than_conscripts(Experience experience)
    {
        // From 500 m one round does not suppress anyone, so only the reaction time sets the first shot.
        var sim = RedFiresAt(experience, 500, OneRifleRound, RulesOfEngagement.ReturnFire);

        sim.RunUntil(s => s.ShotsBy("blue").Any(), TimeSpan.FromSeconds(20), "blue fires");

        var reaction = SpecSuppression.For(experience).ReactionSeconds;
        var delay = sim.ShotsBy("blue")[0].Time - sim.ShotsBy("red")[0].Time;
        Assert.InRange(delay, reaction, reaction + SpecV01.DtSeconds);
    }

    [Fact]
    public void X4_special_forces_are_harder_to_suppress()
    {
        // One aimed round from 100 m: ≈ 1.03. Above a conscript's τ = 0.8, below special forces' τ = 1.5.
        var conscript = RedFiresAt(Experience.Conscript, 100, OneRifleRound, RulesOfEngagement.HoldFire);
        var specialForces = RedFiresAt(Experience.SpecialForces, 100, OneRifleRound, RulesOfEngagement.HoldFire);

        conscript.RunFor(TimeSpan.FromSeconds(2));
        specialForces.RunFor(TimeSpan.FromSeconds(2));

        conscript.Unit("blue").Should().BeSuppressed();
        specialForces.Unit("blue").Should().NotBeSuppressed().BeIn(Posture.Standing);
    }

    [Theory]
    [InlineData(Experience.Conscript)]
    [InlineData(Experience.Regular)]
    [InlineData(Experience.SpecialForces)]
    public void X5_suppression_lasts_by_experience(Experience experience)
    {
        // One MG burst from 100 m (≈ 6.45) suppresses everyone; it lasts H · log2(L / 0.25 τ): ≈ 30 s for a
        // conscript, ≈ 19 s for a regular, ≈ 10 s for special forces.
        var sim = RedFiresAt(experience, 100, OneMgBurst, RulesOfEngagement.HoldFire);
        var shot = sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").ShotsBy("red")[0];

        sim.RunUntil(s => s.Events.OfType<SuppressionEnded>().Any(e => e.AgentId == "blue"), TimeSpan.FromMinutes(2), "blue recovers");

        var release = SpecSuppression.ReleaseSeconds(SpecSuppression.ShotPressure(SpecSuppression.MachineGun, 100), experience);
        var ended = sim.Events.OfType<SuppressionEnded>().Single(e => e.AgentId == "blue");
        Assert.InRange(ended.Time - shot.Time, release - SpecV01.DtSeconds, release + SpecV01.DtSeconds);
    }
}
