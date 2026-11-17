using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// The immediate action drill under fire (backlog 0003, DR). An agent on <c>ReturnFire</c> or <c>FreeFire</c> without a
/// <see cref="FireAt"/> order that comes under fire:
/// <list type="number">
/// <item>drops prone and, after its reaction time, fires 3 snap shots (<c>SnapShots</c>);</item>
/// <item>if in danger of being hit, moves to the nearest cover, even cover it cannot fire from (<c>MovingToCover</c>);</item>
/// <item>moves on to a firing position, forward by default (<c>MovingToFiringPosition</c>);</item>
/// <item>fires aimed shots (<c>ReturningFire</c>).</item>
/// </list>
/// It moves rushing (crouching) when not suppressed, crawling when lightly suppressed (pressure below 2 τ), not at all
/// above that. From the end of stage 1 it reports contact and waits for orders; any new order ends the drill.
/// </summary>
public class ContactDrillTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(3);
    private static readonly Vec2 Blue = new(41, 21);
    private static readonly Vec2 RedAt500 = new(541, 21);
    private static readonly Vec2 RedAt100 = new(141, 21);

    // A building south-east of blue (cells 46–52 × 12–18): cover from red to the east, but nothing to fire from.
    // A low rise north-east of blue (0.5 m, cells 52–54 × 26–36): a firing position behind it. Neither shields blue's
    // starting point from red, which is due east.
    private static readonly (double X0, double Y0, double X1, double Y1) CoverArea = (30, 12, 46, 18);
    private static readonly (double X0, double Y0, double X1, double Y1) FiringArea = (30, 25, 52, 37);

    private static RasterWorld World(double width, bool building, bool rise)
    {
        var map = TestMap.Create(widthMeters: width, heightMeters: 60);
        if (building)
        {
            map.Fill(TerrainClass.Building, fromX: 47, fromY: 13, toX: 51, toY: 17);
        }

        if (rise)
        {
            map.WithMound(fromX: 53, fromY: 27, toX: 53, toY: 35, height: 0.5);
        }

        return map.Build();
    }

    // Red fires one round at blue (it never hits); blue runs the drill.
    private static ScenarioRun FiredOnce(
        RasterWorld world, Vec2 red, WeaponProfile? redWeapon = null, RulesOfEngagement blueRules = RulesOfEngagement.ReturnFire) =>
        Scenario.ForCombat()
            .WithTerrain(world)
            .WithFighter("blue", Blue, Side.Blue, blueRules)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire,
                redWeapon ?? SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("red", new FireAt("blue"));

    private static bool In((double X0, double Y0, double X1, double Y1) area, Vec2 p) =>
        p.X >= area.X0 && p.X <= area.X1 && p.Y >= area.Y0 && p.Y <= area.Y1;

    private static List<ContactDrillStage> Stages(ScenarioRun sim, string id) =>
        sim.Events.OfType<ContactDrillStageChanged>().Where(e => e.AgentId == id).Select(e => e.Stage).ToList();

    private static ContactDrillStageChanged Entered(ScenarioRun sim, string id, ContactDrillStage stage) =>
        sim.Events.OfType<ContactDrillStageChanged>().First(e => e.AgentId == id && e.Stage == stage);

    private static double AngleDegrees(Vec2 from, Vec2 a, Vec2 b) =>
        Math.Acos(Math.Clamp((a - from).Normalized().Dot((b - from).Normalized()), -1, 1)) * 180 / Math.PI;

    [Fact]
    public void DR1_under_fire_the_agent_drops_prone_and_fires_three_snap_shots_after_its_reaction_time()
    {
        // From 500 m one round does not suppress (≈ 0.57 < τ): what blue does is the drill, not suppression.
        var sim = FiredOnce(World(600, building: false, rise: false), RedAt500);

        var redShot = sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").ShotsBy("red")[0];
        sim.Step();
        sim.Unit("blue").Should().BeIn(Posture.Prone).NotBeSuppressed().BeInDrillStage(ContactDrillStage.SnapShots);

        sim.RunUntil(s => s.ShotsBy("blue").Count >= 3, TimeSpan.FromSeconds(20), "three snap shots");

        var snaps = sim.ShotsBy("blue").Take(3).ToList();
        Assert.All(snaps, s => Assert.Equal(FireMode.Snap, s.Mode));
        Assert.All(snaps, s => Assert.Equal("red", s.TargetId));
        var reaction = SpecSuppression.Regular.ReactionSeconds;
        Assert.InRange(snaps[0].Time - redShot.Time, reaction, reaction + SpecV01.DtSeconds);
        Assert.Equal(ContactDrillStage.SnapShots, Stages(sim, "blue")[0]);
    }

    [Fact]
    public void DR2_in_danger_of_being_hit_it_first_moves_into_cover()
    {
        // Red sees blue in the open. The nearest cover is behind the building, from where blue cannot fire.
        var sim = FiredOnce(World(600, building: true, rise: false), RedAt500);

        sim.RunUntil(s => In(CoverArea, s.Unit("blue").Position.Xy) && s.Unit("blue").Velocity == Vec3.Zero, Plenty, "blue in cover");

        var moving = Entered(sim, "blue", ContactDrillStage.MovingToCover);
        Assert.False(sim.Simulation.CanSee("red", "blue"), "behind the building red cannot see blue");
        Assert.DoesNotContain(sim.ShotsBy("blue"), s => s.Time >= moving.Time);
        Assert.Contains(sim.Events.OfType<PostureChanged>(), e => e.AgentId == "blue" && e.Posture == Posture.Crouching && e.Time >= moving.Time);
    }

    [Fact]
    public void DR3_from_cover_it_moves_on_to_a_firing_position()
    {
        var sim = FiredOnce(World(600, building: true, rise: true), RedAt500);

        sim.RunUntil(s => s.Unit("blue").DrillStage == ContactDrillStage.ReturningFire, Plenty, "blue returns fire");

        Assert.Equal(
            [ContactDrillStage.SnapShots, ContactDrillStage.MovingToCover, ContactDrillStage.MovingToFiringPosition, ContactDrillStage.ReturningFire],
            Stages(sim, "blue"));
        var trail = sim.Trail("blue");
        var firstInCover = trail.ToList().FindIndex(p => In(CoverArea, p));
        var firstAtFiringPosition = trail.ToList().FindIndex(p => In(FiringArea, p));
        Assert.True(firstInCover >= 0, "blue went into cover first");
        Assert.True(firstAtFiringPosition > firstInCover, "then on to the firing position");
        Assert.True(In(FiringArea, sim.Unit("blue").Position.Xy), $"blue ends behind the rise, at {sim.Unit("blue").Position}");
        Assert.True(sim.Unit("blue").Position.Xy.DistanceTo(RedAt500) < Blue.DistanceTo(RedAt500), "the firing position is forward");
    }

    [Fact]
    public void DR4_in_cover_without_a_firing_position_it_holds_fire()
    {
        var sim = FiredOnce(World(600, building: true, rise: false), RedAt500);

        sim.RunFor(TimeSpan.FromSeconds(90));

        Assert.True(In(CoverArea, sim.Unit("blue").Position.Xy), $"blue stays in cover, at {sim.Unit("blue").Position}");
        Assert.DoesNotContain(sim.ShotsBy("blue"), s => s.Mode == FireMode.Aimed);
        Assert.DoesNotContain(ContactDrillStage.MovingToFiringPosition, Stages(sim, "blue"));
        sim.Unit("blue").Should().BeAwaitingOrders();
    }

    [Fact]
    public void DR5_without_cover_nearby_it_stays_and_fires_aimed_shots()
    {
        var sim = FiredOnce(World(600, building: false, rise: false), RedAt500);

        sim.RunUntil(s => s.ShotsBy("blue").Any(e => e.Mode == FireMode.Aimed), Plenty, "blue fires aimed shots");

        Assert.Equal([ContactDrillStage.SnapShots, ContactDrillStage.ReturningFire], Stages(sim, "blue"));
        sim.Unit("blue").Should().BeAt(Blue, toleranceMeters: 0.5).BeIn(Posture.Prone);
    }

    [Fact]
    public void DR6_lightly_suppressed_it_crawls_to_cover()
    {
        // One round from 100 m: pressure ≈ 1.03, between τ and 2 τ, so blue crawls (prone, 0.1 × walking speed).
        var sim = FiredOnce(World(200, building: true, rise: false), RedAt100);

        sim.RunUntil(s => s.Unit("blue").DrillStage == ContactDrillStage.MovingToCover, TimeSpan.FromSeconds(20), "blue heads for cover");
        sim.Step();

        Assert.True(sim.Unit("blue").IsSuppressed, "precondition: blue is still suppressed");
        Assert.True(sim.Unit("blue").SuppressionPressure < 2 * SpecSuppression.Regular.SuppressionThreshold, "precondition: lightly");
        sim.Unit("blue").Should().BeIn(Posture.Prone)
            .HaveGroundSpeed(SpecV01.Infantry.OpenMps * SpecShooting.Prone.SpeedFactor, toleranceMps: 0.005);
    }

    [Fact]
    public void DR7_more_heavily_suppressed_it_stays_where_it_is()
    {
        // One MG burst from 100 m: ≈ 6.45. Blue holds still until the pressure falls below 2 τ, then crawls.
        var burst = SpecSuppression.NeverHits(SpecSuppression.MachineGun) with { Rounds = 5 };
        var sim = FiredOnce(World(200, building: true, rise: false), RedAt100, burst);
        var shot = sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires").ShotsBy("red")[0];
        sim.Step();
        var held = sim.Unit("blue").Position.Xy;
        var crawlFrom = SpecSuppression.SecondsUntilBelow(
            SpecSuppression.ShotPressure(SpecSuppression.MachineGun, 100), 2 * SpecSuppression.Regular.SuppressionThreshold,
            Experience.Regular);

        sim.RunUntil(s => s.Unit("blue").Position.Xy.DistanceTo(held) > 0.01, TimeSpan.FromSeconds(60), "blue moves");

        Assert.InRange(sim.TimeSeconds - shot.Time, crawlFrom - SpecV01.DtSeconds, crawlFrom + (2 * SpecV01.DtSeconds));
        Assert.True(sim.Unit("blue").IsSuppressed, "blue is still (lightly) suppressed, so it crawls");
        sim.Unit("blue").Should().BeIn(Posture.Prone);
    }

    [Fact]
    public void DR8_at_the_firing_position_it_fires_aimed_shots_crouching()
    {
        var sim = FiredOnce(World(600, building: true, rise: true), RedAt500);

        sim.RunUntil(
            s => s.Unit("blue").DrillStage == ContactDrillStage.ReturningFire && s.ShotsBy("blue").Any(e => e.Mode == FireMode.Aimed),
            Plenty, "blue fires from the firing position");

        var aimed = sim.ShotsBy("blue").First(e => e.Mode == FireMode.Aimed);
        Assert.True(aimed.Time >= Entered(sim, "blue", ContactDrillStage.ReturningFire).Time);
        Assert.True(In(FiringArea, sim.Unit("blue").Position.Xy), $"blue fires from behind the rise, at {sim.Unit("blue").Position}");
        sim.Unit("blue").Should().BeIn(Posture.Crouching);
    }

    [Fact]
    public void DR9_an_attacker_seen_only_by_its_muzzle_flash_gets_snap_shots_not_aimed_ones()
    {
        // Red fires at bravo, who it sees through 34 m of a forest strip. The rounds go on to pass charlie about 8 m away,
        // so charlie is under fire. Charlie looks at red along the strip, through 90 m of forest: visible only while the
        // muzzle flash lasts. Red fires every 2 s, so the flash shows it most of the time.
        var world = TestMap.Create(widthMeters: 200, heightMeters: 40)
            .Fill(TerrainClass.Forest, fromX: 20, fromY: 18, toX: 110, toY: 24)
            .Build();
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("red", new Vec2(11, 21), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .WithFighter("bravo", new Vec2(125, 29), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("charlie", new Vec2(131, 21), Side.Blue, RulesOfEngagement.ReturnFire)
            .Build()
            .Order("red", new FireAt("bravo"));
        var rules = SpecShooting.Rules;
        var sight = LineOfSight.Between(world, sim.Unit("charlie").EyePosition, sim.Unit("red").AimPoint, rules);
        Assert.False(sight.IsVisible(rules), "precondition: charlie does not see red without the flash");
        Assert.True(sight.IsVisible(rules, muzzleFlash: true), "precondition: charlie sees red's muzzle flash");

        sim.RunFor(TimeSpan.FromSeconds(30));

        var shots = sim.ShotsBy("charlie");
        Assert.True(shots.Count > 3, $"charlie returns fire past its snap shots ({shots.Count} shots)");
        Assert.All(shots, s => Assert.Equal("red", s.TargetId));
        Assert.All(shots, s => Assert.Equal(FireMode.Snap, s.Mode));
    }

    [Fact]
    public void DR10_at_the_rear_of_a_wedge_it_moves_forward_to_fire_past_its_team_mates()
    {
        // Red fires at the point man; the round flies on along the same line past the rear man 20 m behind him, who is
        // under fire too. His team-mate is dead ahead, inside the 60° sector, so he may not fire from where he is. With
        // no cover nearby, he moves forward to a spot with a clear sector, and only then fires.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 80).Build())
            .WithFighter("point", new Vec2(101, 41), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("rear", new Vec2(81, 41), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(541, 41), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("red", new FireAt("point"));
        var start = sim.Unit("rear").Position.Xy;
        var red = sim.Unit("red").Position.Xy;

        sim.RunUntil(s => s.ShotsBy("rear").Any(e => e.Mode == FireMode.Aimed), Plenty, "rear fires");

        Assert.Contains(sim.Events.OfType<ContactDrillStageChanged>(), e => e.AgentId == "rear");
        foreach (var shot in sim.ShotsBy("rear"))
        {
            var from = sim.PositionAt("rear", shot.Time);
            var angle = AngleDegrees(from, red, sim.PositionAt("point", shot.Time));
            Assert.True(angle >= 30, $"rear fired at t={shot.Time} from {from} with its team-mate {angle:0.#}° off the muzzle");
        }

        Assert.True(sim.Unit("rear").Position.Xy.DistanceTo(red) <= start.DistanceTo(red), "the new firing position is forward");
    }

    [Fact]
    public void DR11_it_reports_contact_and_waits_instead_of_resuming_its_corridor()
    {
        var corridor = new Corridor([new Vec2(21, 21), new Vec2(281, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 240).Build())
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(21, 221), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor))
            .Order("red", new FireAt("blue"));

        sim.RunUntil(s => s.Events.OfType<ContactReported>().Any(), TimeSpan.FromSeconds(30), "blue reports contact");
        var contactAt = sim.Unit("blue").Position.Xy;
        sim.RunFor(TimeSpan.FromMinutes(10));

        var report = Assert.Single(sim.Events.OfType<ContactReported>());
        Assert.Equal("blue", report.AgentId);
        Assert.Equal("red", report.AttackerId);
        Assert.Equal(new Vec2(21, 221), report.AttackerPosition);
        var stageEvents = sim.Events.OfType<ContactDrillStageChanged>().Where(e => e.AgentId == "blue").ToList();
        Assert.Equal(ContactDrillStage.SnapShots, stageEvents[0].Stage);
        Assert.Equal(stageEvents[1].Time, report.Time, 9); // when stage 1 ends
        sim.Unit("blue").Should().BeAwaitingOrders().BeAt(contactAt, toleranceMeters: 0.5);
        Assert.DoesNotContain(sim.Events.OfType<Arrived>(), e => e.AgentId == "blue");
    }

    [Fact]
    public void DR12_a_new_order_ends_the_wait()
    {
        var corridor = new Corridor([new Vec2(21, 21), new Vec2(281, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 240).Build())
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(21, 221), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor))
            .Order("red", new FireAt("blue"));
        sim.RunUntil(s => s.Events.OfType<ContactReported>().Any(), TimeSpan.FromSeconds(30), "blue reports contact");
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.Order("blue", new MoveThroughCorridor(corridor)).Step();

        Assert.False(sim.Unit("blue").IsAwaitingOrders);
        sim.Unit("blue").Should().BeInDrillStage(ContactDrillStage.None);
        sim.RunUntil(s => s.Events.OfType<Arrived>().Any(e => e.AgentId == "blue"), TimeSpan.FromMinutes(10), "blue arrives");
    }

    [Fact]
    public void DR13_a_hold_fire_agent_does_not_run_the_drill()
    {
        var corridor = new Corridor([Blue, new Vec2(581, 41)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 540).Build())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Blue + new Vec2(0, 500), Side.Red, RulesOfEngagement.HoldFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 })
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor))
            .Order("red", new FireAt("blue"));

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Single(sim.ShotsBy("red"));
        Assert.Empty(sim.ShotsBy("blue"));
        Assert.Empty(sim.Events.OfType<ContactReported>());
        Assert.Empty(Stages(sim, "blue"));
        Assert.Equal(UnitActivity.Moving, sim.Unit("blue").Activity);
    }

    [Fact]
    public void DR14_an_agent_with_a_fire_at_order_keeps_its_ordered_target()
    {
        // Blue is ordered to fire at red-2, 100 m east and in plain sight; red-1 fires at blue from 500 m north.
        // Blue's rounds never hit, so its order lasts.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 540).Build())
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .WithFighter("red-1", new Vec2(21, 521), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .WithFighter("red-2", new Vec2(121, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new FireAt("red-2"))
            .Order("red-1", new FireAt("blue"));

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.NotEmpty(sim.ShotsBy("red-1"));
        Assert.NotEmpty(sim.ShotsBy("blue"));
        Assert.All(sim.ShotsBy("blue"), s => Assert.Equal("red-2", s.TargetId));
        Assert.All(sim.ShotsBy("blue"), s => Assert.Equal(FireMode.Aimed, s.Mode));
        Assert.Empty(Stages(sim, "blue"));
    }

    [Fact]
    public void DR15_fire_without_an_order_keeps_a_reserve_of_rounds()
    {
        // 10 rounds: the drill fires 8 (3 snap, 5 aimed) and keeps 20 % in reserve. A FireAt order may use the reserve.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 540).Build())
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire,
                SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 10 })
            .WithFighter("red", new Vec2(21, 521), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
            .Build()
            .Order("red", new FireAt("blue"));

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(8, sim.ShotsBy("blue").Count);
        Assert.Equal(2, sim.Unit("blue").RoundsLeft);

        sim.Order("blue", new FireAt("red")).RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal(10, sim.ShotsBy("blue").Count);
        Assert.Equal(0, sim.Unit("blue").RoundsLeft);
    }
}
