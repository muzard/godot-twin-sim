using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Spotting (backlog 0004 S5a1: SP1–SP6). Seeing an enemy is no longer instant: an observer spots it after watching it
/// continuously for <c>t_spot = t_base(experience) · (1 + d / 100 m) · A(angle) / SizeFactor(posture) / T</c>
/// (<see cref="SpecSpotting"/>). The angle factor is 1 in the focus cone (±15° of the watch direction), 3 elsewhere in the
/// field of view (±90°), and nothing is spotted outside it. Breaking the line of sight or looking away starts the time
/// again. A muzzle flash in the field of view spots its shooter at once; a shot is heard all round but spots no one.
/// Blue watches; red stands still and holds fire. dt = 0.5 s.
/// </summary>
public class SpottingTests
{
    private static readonly Vec2 Blue = new(21, 31);
    private static readonly Vec2 East = new(1, 0);
    private static readonly Vec2 West = new(-1, 0);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private static RasterWorld Open(double width = 400) => TestMap.Create(widthMeters: width, heightMeters: 60).Build();

    /// <summary>Blue at <see cref="Blue"/> watching <paramref name="watching"/>, red standing at <paramref name="redAt"/>.</summary>
    private static ScenarioRun Watching(
        Vec2 watching, Vec2 redAt, Experience experience = Experience.Regular, RasterWorld? world = null,
        CombatRules? rules = null)
    {
        var scenario = Scenario.ForSpotting().WithTerrain(world ?? Open());
        if (rules is not null)
        {
            scenario.WithCombatRules(rules);
        }

        return scenario
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire, experience: experience)
            .WithFighter("red", redAt, Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new Watch(watching));
    }

    /// <summary>The distance the formula uses: from blue's eye to red's aim point.</summary>
    private static double EyeToAim(ScenarioRun sim) => sim.Unit("blue").EyePosition.DistanceTo(sim.Unit("red").AimPoint);

    /// <summary>Blue's watch direction with red, due east, <paramref name="degrees"/> to the right of it.</summary>
    private static Vec2 RedOffToTheRight(double degrees) => SpecSpotting.Turned(East, -degrees);

    [Fact]
    public void SP1_a_regular_fighter_spots_a_standing_enemy_straight_ahead_at_100_m_after_2_5_s()
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));
        var expected = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim));
        Assert.Equal(2.5, expected, precision: 2);

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected);
    }

    [Fact]
    public void SP1_an_enemy_in_sight_is_not_known_until_it_is_spotted()
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));

        sim.RunFor(TimeSpan.FromSeconds(2));
        Assert.True(sim.Simulation.CanSee("blue", "red"), "the line of sight is clear all along");
        Assert.False(sim.Simulation.KnowsOf("blue", "red"), "2 s is less than the 2.5 s spotting time");

        sim.RunUntilSpotted("blue", "red", OneMinute);
        Assert.True(sim.Simulation.KnowsOf("blue", "red"));
    }

    [Fact]
    public void SP1_spotting_is_logged_with_the_enemys_position()
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));

        var spotted = sim.RunUntilSpotted("blue", "red", OneMinute);

        Assert.Equal("blue", spotted.AgentId);
        Assert.Equal("red", spotted.EnemyId);
        Assert.Equal(sim.Unit("red").Position.Xy, spotted.EnemyPosition);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 1)]
    [InlineData(20, SpecSpotting.FieldOfViewFactor)]
    [InlineData(60, SpecSpotting.FieldOfViewFactor)]
    [InlineData(85, SpecSpotting.FieldOfViewFactor)]
    public void SP2_the_angle_factor_is_one_in_the_focus_cone_and_three_elsewhere_in_the_field_of_view(
        double degreesOff, double factor)
    {
        var sim = Watching(RedOffToTheRight(degreesOff), Blue + new Vec2(100, 0));
        var expected = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim), angleFactor: factor);

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected);
    }

    [Theory]
    [InlineData(95)]
    [InlineData(135)]
    [InlineData(180)]
    public void SP2_an_enemy_outside_the_field_of_view_is_never_spotted(double degreesOff)
    {
        var sim = Watching(RedOffToTheRight(degreesOff), Blue + new Vec2(100, 0));

        sim.RunFor(TimeSpan.FromMinutes(5));

        Assert.True(sim.Simulation.CanSee("blue", "red"), "the line of sight is clear; only the watch direction is wrong");
        sim.ShouldNotHaveSpotted("blue", "red");
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(300)]
    public void SP3_spotting_takes_longer_with_distance(double meters)
    {
        var sim = Watching(East, Blue + new Vec2(meters, 0));
        var expected = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim));

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected);
    }

    [Theory]
    [InlineData(Posture.Crouching)]
    [InlineData(Posture.Prone)]
    public void SP3_a_crouching_or_prone_enemy_takes_longer_by_its_size(Posture posture)
    {
        // Blue looks away while red takes its posture, then turns to it.
        var sim = Watching(West, Blue + new Vec2(100, 0));
        sim.Order("red", new SetPosture(posture)).RunFor(TimeSpan.FromSeconds(3));
        sim.Unit("red").Should().BeIn(posture);
        sim.ShouldNotHaveSpotted("blue", "red");

        var turnedAt = sim.TimeSeconds;
        sim.Order("blue", new Watch(East));
        var expected = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim), target: posture);

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected, fromTime: turnedAt);
    }

    [Fact]
    public void SP3_forest_between_lengthens_the_spotting_time_by_its_transmission()
    {
        // 20 m of forest (x 51..71) on the line from blue to red, 100 m away.
        var world = TestMap.Create(widthMeters: 400, heightMeters: 60)
            .Fill(TerrainClass.Forest, fromX: 51, fromY: 0, toX: 71, toY: 60)
            .Build();
        var sim = Watching(East, Blue + new Vec2(100, 0), world: world);
        var transmission = LineOfSight.Between(
            world, sim.Unit("blue").EyePosition, sim.Unit("red").AimPoint, SpecSpotting.Rules).Transmission;
        Assert.InRange(transmission, SpecSpotting.Rules.VisibleTransmission, 0.6);
        var expected = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim), transmission: transmission);

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected);
    }

    [Fact]
    public void SP3_looking_away_starts_the_spotting_time_again()
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));
        sim.RunFor(TimeSpan.FromSeconds(2)); // 2 of the 2.5 s
        sim.Order("blue", new Watch(West)).RunFor(TimeSpan.FromSeconds(1));
        sim.ShouldNotHaveSpotted("blue", "red");

        var turnedBackAt = sim.TimeSeconds;
        sim.Order("blue", new Watch(East));
        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim)), fromTime: turnedBackAt);
    }

    [Fact]
    public void SP3_a_broken_line_of_sight_starts_the_spotting_time_again()
    {
        // A 0.8 m mound 6..10 m in front of red hides it prone but not standing.
        var world = TestMap.Create(widthMeters: 400, heightMeters: 60).WithMound(fromX: 111, fromY: 21, toX: 115, toY: 41, height: 0.8).Build();
        var sim = Watching(East, Blue + new Vec2(100, 0), world: world);
        sim.Order("red", new SetPosture(Posture.Prone)).RunFor(TimeSpan.FromSeconds(5)); // visible for its first 1 s
        Assert.False(sim.Simulation.CanSee("blue", "red"), "prone behind the mound red is hidden");
        sim.ShouldNotHaveSpotted("blue", "red");

        sim.Order("red", new SetPosture(Posture.Standing))
            .RunUntil(s => s.Unit("red").Posture == Posture.Standing, TimeSpan.FromSeconds(5), "red standing");
        var visibleAt = sim.Events.OfType<PostureChanged>().Last(e => e.AgentId == "red").Time;
        var spotted = sim.RunUntilSpotted("blue", "red", OneMinute);

        // Counted afresh: 2.5 s after red stood up (give or take the tick it stood up in), not the 1.5 s still missing.
        var full = SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim));
        var tick = sim.Simulation.Config.TickSeconds;
        Assert.InRange(spotted.Time, visibleAt + full - tick - 1e-9, visibleAt + full + tick + 1e-9);
    }

    [Theory]
    [InlineData(Experience.Conscript)]
    [InlineData(Experience.Regular)]
    [InlineData(Experience.SpecialForces)]
    public void SP4_spotting_time_depends_on_the_observers_experience(Experience experience)
    {
        var sim = Watching(East, Blue + new Vec2(100, 0), experience);
        var expected = SpecSpotting.SpotSeconds(experience, EyeToAim(sim));

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", expected);
    }

    [Fact]
    public void SP5_a_wider_focus_cone_spots_an_enemy_off_to_the_side_sooner()
    {
        var wide = SpecSpotting.Rules with { Observation = SpecSpotting.Observation with { FocusHalfAngleDegrees = 60 } };
        var sim = Watching(RedOffToTheRight(45), Blue + new Vec2(100, 0), rules: wide);

        sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.ShouldHaveSpottedAfter("blue", "red", SpecSpotting.SpotSeconds(Experience.Regular, EyeToAim(sim), angleFactor: 1));
    }

    [Fact]
    public void SP5_a_narrower_field_of_view_misses_an_enemy_off_to_the_side()
    {
        // As through a night-vision device: ±10° focus, ±20° field of view.
        var narrow = SpecSpotting.Rules with
        {
            Observation = SpecSpotting.Observation with { FocusHalfAngleDegrees = 10, FieldOfViewHalfAngleDegrees = 20 },
        };
        var sim = Watching(RedOffToTheRight(45), Blue + new Vec2(100, 0), rules: narrow);

        sim.RunFor(TimeSpan.FromMinutes(2));

        sim.ShouldNotHaveSpotted("blue", "red");
    }

    /// <summary>
    /// Red, 200 m east of blue, fires one round at bravo, 10 m north of red. Red first turns to bravo, so it can see it.
    /// </summary>
    private static ScenarioRun RedFiresAtBravo(Vec2 blueWatching)
    {
        var red = Blue + new Vec2(200, 0);
        return Scenario.ForSpotting().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire, SpecShooting.Rifle with { Rounds = 1 })
            .WithFighter("bravo", red + new Vec2(0, 10), Side.Blue, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new Watch(blueWatching))
            .Order("red", new Watch(new Vec2(0, 1)))
            .Step()
            .Order("red", new FireAt("bravo"));
    }

    [Fact]
    public void SP6_a_muzzle_flash_in_the_field_of_view_spots_the_shooter_at_once()
    {
        // Red is 60° off blue's watch direction: without the flash blue would need 3 × 3.75 s = 11.25 s.
        var sim = RedFiresAtBravo(RedOffToTheRight(60));

        sim.RunUntil(s => s.ShotsBy("red").Count > 0, TimeSpan.FromSeconds(10), "red fires");
        var shot = sim.ShotsBy("red")[0];
        sim.Step();

        Assert.True(shot.Time < SpecSpotting.SpotSeconds(Experience.Regular, 200, angleFactor: 3) - 1, "red fires well before blue would spot it");
        sim.ShouldHaveSpottedAfter("blue", "red", 0, fromTime: shot.Time);
    }

    [Fact]
    public void SP6_a_shot_from_behind_is_heard_but_spots_no_one()
    {
        var sim = RedFiresAtBravo(West);

        sim.RunUntil(s => s.ShotsBy("red").Count > 0, TimeSpan.FromSeconds(10), "red fires");
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Contains(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "blue" && e.ShooterId == "red");
        sim.ShouldNotHaveSpotted("blue", "red");
    }

    [Fact]
    public void Watch_turns_the_agent_toward_the_direction_at_once()
    {
        var northEast = new Vec2(1, 1);
        var sim = Watching(East, Blue + new Vec2(100, 0)).Step();

        sim.Order("blue", new Watch(northEast)).Step();

        sim.Unit("blue").Should().BeWatching(northEast);
    }

    [Fact]
    public void Watch_with_no_direction_or_to_an_unknown_agent_is_rejected()
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));

        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new Watch(new Vec2(0, 0))));
        Assert.Throws<OrderRejectedException>(() => sim.Order("nobody", new Watch(East)));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)]
    public void Watch_with_a_direction_that_is_not_finite_is_rejected(double x, double y)
    {
        var sim = Watching(East, Blue + new Vec2(100, 0));

        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new Watch(new Vec2(x, y))));
    }

    [Fact]
    public void An_agent_that_has_spotted_an_enemy_watches_it_until_it_moves_on()
    {
        var sim = Watching(SpecSpotting.Turned(East, -45), Blue + new Vec2(100, 0));
        sim.RunUntilSpotted("blue", "red", OneMinute);
        sim.Step();
        sim.Unit("blue").Should().BeWatching(East);

        sim.Order("blue", new MoveTo(Blue + new Vec2(0, 20))).Step(2);

        sim.Unit("blue").Should().BeWatching(new Vec2(0, 1));
    }

    [Fact]
    public void An_agent_outside_a_squad_watches_its_direction_of_movement_and_keeps_it_when_it_stops()
    {
        var sim = Watching(West, Blue + new Vec2(100, 0));
        var destination = Blue + new Vec2(40, 20);

        sim.Order("blue", new MoveTo(destination)).Step(4);
        sim.Unit("blue").Should().BeWatching(destination - Blue);

        sim.RunUntil(s => s.Unit("blue").IsIdle, OneMinute, "blue arrives");
        sim.Unit("blue").Should().BeWatching(destination - Blue);
    }
}
