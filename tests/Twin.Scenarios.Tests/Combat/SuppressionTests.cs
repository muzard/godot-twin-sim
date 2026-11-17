using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Suppression as fire pressure (backlog 0003, SU), replacing 0002's "a shot suppresses for 10 s". Every shot adds
/// n · S · exp(−m / 2 m) · T · Cover to its target (and less to anyone near the round's path); the pressure halves every
/// H (4 s for a regular soldier). An agent is suppressed from τ (1 for a regular) until the pressure falls below 0.25 τ,
/// and heavily suppressed (no fire at all) from 5 τ. Expectations come from <see cref="SpecSuppression"/>. Shooters use
/// weapons that never hit (<see cref="SpecSuppression.NeverHits"/>), so no casualty gets in the way.
/// </summary>
public class SuppressionTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(3);
    private static readonly Vec2 BlueStart = new(21, 21);

    // Blue walks east along y = 21 under a corridor order; red stands `distance` north of blue's start and fires at it.
    private static ScenarioRun RedFiresAtWalkingBlue(double distance, WeaponProfile redWeapon)
    {
        const double MapWidth = 300;
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: MapWidth, heightMeters: distance + 40).Build())
            .WithFighter("blue", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", BlueStart + new Vec2(0, distance), Side.Red, RulesOfEngagement.HoldFire, redWeapon)
            .Build();
        var corridor = new Corridor([BlueStart, new Vec2(MapWidth - 19, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        return sim.Order("blue", new MoveThroughCorridor(corridor)).Order("red", new FireAt("blue"));
    }

    // Blue stands still at (21, 21) holding fire; red fires at it from `distance` north.
    private static ScenarioRun RedFiresAtStandingBlue(
        double distance, WeaponProfile redWeapon, Experience blue = Experience.Regular,
        RulesOfEngagement blueRules = RulesOfEngagement.HoldFire) =>
        Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: distance + 40).Build())
            .WithFighter("blue", BlueStart, Side.Blue, blueRules, experience: blue)
            .WithFighter("red", BlueStart + new Vec2(0, distance), Side.Red, RulesOfEngagement.HoldFire, redWeapon)
            .Build()
            .Order("red", new FireAt("blue"));

    private static WeaponProfile OneRound(WeaponProfile weapon) => SpecSuppression.NeverHits(weapon) with { Rounds = weapon.RoundsPerBurst };

    private static IEnumerable<T> EventsOf<T>(ScenarioRun sim, string agentId)
        where T : SimEvent => sim.Events.OfType<T>().Where(e => e.AgentId == agentId);

    [Fact]
    public void SU4_a_close_rifle_shot_suppresses()
    {
        // One aimed round from 100 m passes 0.3 m away: 1.2 · e^(−0.15) ≈ 1.03 ≥ τ = 1.
        Assert.True(SpecSuppression.ShotPressure(SpecShooting.Rifle, 100) >= SpecSuppression.Regular.SuppressionThreshold);
        var sim = RedFiresAtWalkingBlue(distance: 100, OneRound(SpecShooting.Rifle));

        sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").Step();
        var suppressedAt = sim.Unit("blue").Position;

        sim.Unit("blue").Should().BeSuppressed().BeIn(Posture.Prone).NotBeHeavilySuppressed();
        sim.RunFor(TimeSpan.FromSeconds(5));
        sim.Unit("blue").Should().BeSuppressed();
        Assert.Equal(suppressedAt, sim.Unit("blue").Position);
    }

    [Fact]
    public void SU5_a_single_distant_rifle_shot_does_not_suppress()
    {
        // From 500 m the round passes 1.5 m away: 1.2 · e^(−0.75) ≈ 0.57 < τ. A stray round barely matters.
        Assert.True(SpecSuppression.ShotPressure(SpecShooting.Rifle, 500) < SpecSuppression.Regular.SuppressionThreshold);
        var sim = RedFiresAtWalkingBlue(distance: 500, OneRound(SpecShooting.Rifle));

        sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").Step();
        var at = sim.Unit("blue").Position;
        sim.RunFor(TimeSpan.FromSeconds(5));

        sim.Unit("blue").Should().NotBeSuppressed().BeIn(Posture.Standing);
        Assert.Equal(UnitActivity.Moving, sim.Unit("blue").Activity);
        Assert.True(sim.Unit("blue").Position.Xy.DistanceTo(at.Xy) > 1, "blue walks on");
        Assert.Empty(EventsOf<SuppressionStarted>(sim, "blue"));
    }

    [Fact]
    public void SU6_suppression_ends_when_pressure_falls_below_a_quarter_of_the_threshold()
    {
        var sim = RedFiresAtWalkingBlue(distance: 100, OneRound(SpecShooting.Rifle));
        var shot = sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").ShotsBy("red")[0];

        sim.RunUntil(s => EventsOf<SuppressionEnded>(s, "blue").Any(), Plenty, "blue's suppression ends");

        // L = 1.03 decays below 0.25 after H · log2(L / 0.25) ≈ 8.2 s.
        var release = SpecSuppression.ReleaseSeconds(SpecSuppression.ShotPressure(SpecShooting.Rifle, 100), Experience.Regular);
        var ended = EventsOf<SuppressionEnded>(sim, "blue").Single();
        Assert.InRange(ended.Time, shot.Time + release - SpecV01.DtSeconds, shot.Time + release + SpecV01.DtSeconds);

        // As in 0002, it then stands up (1 s) and resumes its order.
        sim.RunFor(TimeSpan.FromSeconds(2));
        sim.Unit("blue").Should().NotBeSuppressed().BeIn(Posture.Standing);
        Assert.Equal(UnitActivity.Moving, sim.Unit("blue").Activity);
        Assert.IsType<MoveThroughCorridor>(sim.Unit("blue").CurrentOrder);
    }

    [Fact]
    public void SU7_sustained_fire_keeps_the_target_suppressed()
    {
        // Aimed rifle fire from 500 m every 2 s: 0.57, 0.97, 1.25, … The third shot suppresses.
        var perShot = SpecSuppression.ShotPressure(SpecShooting.Rifle, 500);
        var second = SpecSuppression.Decayed(perShot, 2, Experience.Regular) + perShot;
        var third = SpecSuppression.Decayed(second, 2, Experience.Regular) + perShot;
        Assert.True(second < 1 && third >= 1, "precondition: the third shot is the first to reach τ");
        var sim = RedFiresAtStandingBlue(distance: 500, SpecSuppression.NeverHits(SpecShooting.Rifle));

        sim.RunFor(TimeSpan.FromSeconds(30));
        sim.Order("red", new CeaseFire()).Step();

        var shots = sim.ShotsBy("red");
        var started = EventsOf<SuppressionStarted>(sim, "blue").First();
        Assert.InRange(started.Time, shots[2].Time, shots[2].Time + SpecV01.DtSeconds);
        sim.Unit("blue").Should().BeSuppressed();
        Assert.Empty(EventsOf<SuppressionEnded>(sim, "blue"));

        sim.RunUntil(s => EventsOf<SuppressionEnded>(s, "blue").Any(), Plenty, "suppression ends after the fire stops");
        Assert.True(EventsOf<SuppressionEnded>(sim, "blue").Single().Time > shots[^1].Time);
    }

    [Fact]
    public void SU8_machine_gun_fire_suppresses_far_more_than_rifle_fire()
    {
        var mg = RedFiresAtStandingBlue(distance: 300, SpecSuppression.NeverHits(SpecSuppression.MachineGun));
        var rifle = RedFiresAtStandingBlue(distance: 300, SpecSuppression.NeverHits(SpecShooting.Rifle));

        foreach (var sim in new[] { mg, rifle })
        {
            sim.RunFor(TimeSpan.FromSeconds(10));
            sim.Order("red", new CeaseFire()).Step();
        }

        mg.Unit("blue").Should().BeHeavilySuppressed();
        rifle.Unit("blue").Should().NotBeHeavilySuppressed();
        Assert.True(mg.Unit("blue").SuppressionPressure > rifle.Unit("blue").SuppressionPressure);

        mg.RunUntil(s => EventsOf<SuppressionEnded>(s, "blue").Any(), Plenty, "MG target recovers");
        rifle.RunUntil(s => EventsOf<SuppressionEnded>(s, "blue").Any() || s.TimeSeconds > 60, Plenty, "rifle target recovers");
        var mgEnd = EventsOf<SuppressionEnded>(mg, "blue").Single().Time;
        var rifleEnd = EventsOf<SuppressionEnded>(rifle, "blue").Select(e => e.Time).DefaultIfEmpty(0).Single();
        Assert.True(mgEnd > rifleEnd + 5, $"MG suppression ended at t={mgEnd}, rifle suppression at t={rifleEnd}");
    }

    [Fact]
    public void SU9_a_heavily_suppressed_agent_does_not_fire()
    {
        // A 12.7 burst from 100 m (≈ 17) keeps blue far above 5 τ; blue is on return fire and sees the gunner.
        var sim = RedFiresAtStandingBlue(distance: 100, SpecSuppression.NeverHits(SpecSuppression.HeavyMachineGun),
            blueRules: RulesOfEngagement.ReturnFire);

        sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires");
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.Unit("blue").Should().BeHeavilySuppressed();
        Assert.True(sim.Simulation.CanSee("blue", "red"), "precondition: blue sees the gunner");
        Assert.Empty(sim.ShotsBy("blue"));
        Assert.Contains(EventsOf<SuppressionStarted>(sim, "blue"), e => e.Heavy);
    }

    [Fact]
    public void SU10_sustained_rifle_fire_alone_does_not_suppress_heavily()
    {
        // Heavy suppression stops all fire, so its threshold is high: one rifle levels off well below 5 τ.
        var sim = RedFiresAtStandingBlue(distance: 100, SpecSuppression.NeverHits(SpecShooting.Rifle));

        sim.RunFor(TimeSpan.FromSeconds(60));

        sim.Unit("blue").Should().BeSuppressed().NotBeHeavilySuppressed();
        Assert.DoesNotContain(EventsOf<SuppressionStarted>(sim, "blue"), e => e.Heavy);
    }

    [Fact]
    public void SU11_suppression_draws_no_random_numbers()
    {
        // Blue lies prone and holds fire; red fires real rounds at it until it is out of action. Only shots draw from
        // Simulation.Random, one number each, so a fresh generator with the same seed reproduces every hit.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 140).Build())
            .WithFighter("blue", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", BlueStart + new Vec2(0, 100), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new SetPosture(Posture.Prone));
        sim.RunFor(TimeSpan.FromSeconds(2));
        sim.Order("red", new FireAt("blue")).RunFor(TimeSpan.FromMinutes(2));

        var distance = sim.Unit("red").EyePosition.DistanceTo(sim.Unit("blue").AimPoint);
        var probability = SpecShooting.RangeProbability(distance) * SpecShooting.Prone.SizeFactor;
        var random = new Twin.Sim.DeterministicRandom(sim.Simulation.Config.Seed);
        var shots = sim.ShotsBy("red");
        Assert.True(shots.Count >= 2, "precondition: red fired");
        Assert.Equal(shots.Select(_ => random.NextDouble() < probability), shots.Select(s => s.Hit));
    }

    [Fact]
    public void SU13_a_fighter_beside_the_target_is_suppressed_less_than_the_target()
    {
        // Three aimed MG bursts at A from 100 m. B lies 3 m off the rounds' path: √(0.3² + 3²) ≈ 3 m, ≈ 0.22 of the
        // pressure A gets per round. A ends heavily suppressed; B only suppressed, and B recovers first.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 140).Build())
            .WithFighter("a", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("b", BlueStart + new Vec2(3, 0), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", BlueStart + new Vec2(0, 100), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecSuppression.MachineGun) with { Rounds = 15 })
            .Build()
            .Order("red", new FireAt("a"));

        sim.RunUntil(s => s.ShotsBy("red").Count == 3, TimeSpan.FromSeconds(30), "three bursts").Step();

        sim.Unit("a").Should().BeHeavilySuppressed();
        sim.Unit("b").Should().BeSuppressed().NotBeHeavilySuppressed();

        sim.RunUntil(s => EventsOf<SuppressionEnded>(s, "a").Any(), Plenty, "a recovers");
        Assert.True(EventsOf<SuppressionEnded>(sim, "b").Single().Time < EventsOf<SuppressionEnded>(sim, "a").Single().Time);
    }

    [Fact]
    public void SU13_aimed_rifle_fire_at_the_target_does_not_suppress_a_fighter_beside_it()
    {
        // 1.2 · e^(−√(0.3² + 3²) / 2) ≈ 0.27 per round levels off at ≈ 0.9 < τ.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 140).Build())
            .WithFighter("a", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("b", BlueStart + new Vec2(3, 0), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", BlueStart + new Vec2(0, 100), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle))
            .Build()
            .Order("red", new FireAt("a"));

        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.Unit("a").Should().BeSuppressed();
        sim.Unit("b").Should().NotBeSuppressed();
        Assert.Empty(EventsOf<SuppressionStarted>(sim, "b"));
        Assert.True(sim.Unit("b").SuppressionPressure > 0, "b feels the fire, a little");
    }

    [Fact]
    public void SU14_a_round_passing_within_ten_meters_starts_the_whole_drill()
    {
        // One round at A from 500 m. B, on return fire 3 m beside A, is under fire too: it runs the drill and its
        // first snap shot goes at red.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 540).Build())
            .WithFighter("a", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("b", BlueStart + new Vec2(3, 0), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", BlueStart + new Vec2(0, 500), Side.Red, RulesOfEngagement.HoldFire, OneRound(SpecShooting.Rifle))
            .Build()
            .Order("red", new FireAt("a"));

        sim.RunUntil(s => s.ShotsBy("b").Any(), TimeSpan.FromSeconds(20), "b returns fire");

        Assert.Contains(EventsOf<ContactDrillStageChanged>(sim, "b"), e => e.Stage == ContactDrillStage.SnapShots);
        Assert.Equal("red", sim.ShotsBy("b")[0].TargetId);
        Assert.Equal(FireMode.Snap, sim.ShotsBy("b")[0].Mode);
    }

    [Fact]
    public void SU15_point_patrol_contact_does_not_pin_down_the_rear_of_the_wedge()
    {
        // A fire team in a wedge heads north; its point patrol is 100 m ahead. Red fires at the patrol from 200 m to
        // the north-east, so the rounds' path runs about 70 m from every fighter of the wedge. The wedge hears the
        // fight but is not under fire: no suppression and no drill. The patrol is under fire and runs the drill.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 400, heightMeters: 500).Build())
            .WithWedge("w", apex: new Vec2(201, 201), facing: new Vec2(0, 1), spacingMeters: 10, Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("p-1", new Vec2(201, 301), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("p-2", new Vec2(207, 301), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(343, 443), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .Build()
            .Order("red", new FireAt("p-1"));

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Contains(EventsOf<ContactDrillStageChanged>(sim, "p-1"), e => e.Stage == ContactDrillStage.SnapShots);
        foreach (var id in new[] { "w-1", "w-2", "w-3", "w-4", "w-5" })
        {
            Assert.NotEmpty(EventsOf<ShotHeard>(sim, id));
            Assert.Empty(EventsOf<SuppressionStarted>(sim, id));
            Assert.Empty(EventsOf<ContactDrillStageChanged>(sim, id));
            sim.Unit(id).Should().BeInDrillStage(ContactDrillStage.None).BeIn(Posture.Standing);
        }
    }
}
