using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// What fire does to the target (backlog 0002, E). The first hit pins it completely for 60 s: it drops prone at once and
/// does not move, shoot, change posture or ask for permission. After that it is wounded: half speed, half hit
/// probability, and it resumes its order. A second hit puts it out of action. Every shot at an agent, hit or miss,
/// suppresses it for 10 s (prone, not moving on). No test-only hit model: the tests run until a real hit or miss happens.
/// </summary>
public class EffectsTests
{
    private static readonly TimeSpan Plenty = TimeSpan.FromMinutes(5);

    // Blue walks east along y = 21; red stands 10 m north of blue's start and fires at it. Both hold fire otherwise.
    private static readonly Vec2 BlueStart = new(21, 21);
    private static readonly Vec2 RedPosition = new(21, 31);

    private static ScenarioRun BlueWalksPastRed(double redDistance = 10, double mapWidth = 300)
    {
        var red = BlueStart + new Vec2(0, redDistance);
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: mapWidth, heightMeters: Math.Max(40, redDistance + 40)).Build())
            .WithFighter("blue", BlueStart, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        var corridor = new Corridor([BlueStart, new Vec2(mapWidth - 19, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        return sim.Order("blue", new MoveThroughCorridor(corridor)).Order("red", new FireAt("blue"));
    }

    private static List<Health> HealthHistory(ScenarioRun sim, string id) =>
        sim.Events.OfType<HealthChanged>().Where(e => e.AgentId == id).Select(e => e.Health).ToList();

    [Fact]
    public void E1_the_first_hit_pins_the_agent_completely_for_sixty_seconds()
    {
        var sim = BlueWalksPastRed();
        var hit = sim.RunUntilHit("blue", Plenty);
        sim.Order("red", new CeaseFire());
        sim.Order("blue", new FireAt("red")); // held: a pinned agent does not shoot
        var pinnedAt = sim.Unit("blue").Position;

        sim.Unit("blue").Should().HaveHealth(Health.Pinned).BeIn(Posture.Prone);
        Assert.Equal(UnitActivity.Pinned, sim.Unit("blue").Activity);

        sim.RunFor(TimeSpan.FromSeconds(59));

        sim.Unit("blue").Should().HaveHealth(Health.Pinned).BeIn(Posture.Prone);
        Assert.Equal(pinnedAt, sim.Unit("blue").Position);
        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.AgentId == "blue");
        Assert.DoesNotContain(sim.Events.OfType<PostureChanged>(), e => e.AgentId == "blue" && e.Time > hit.Time);
        Assert.DoesNotContain(sim.Events.OfType<PermissionRequested>(), e => e.AgentId == "blue");
    }

    [Fact]
    public void E2_after_sixty_seconds_a_pinned_agent_is_wounded_and_resumes_its_order_at_half_speed()
    {
        var sim = BlueWalksPastRed();
        var hit = sim.RunUntilHit("blue", Plenty);
        sim.Order("red", new CeaseFire());
        var pinnedAt = sim.Unit("blue").Position;

        sim.RunFor(TimeSpan.FromSeconds(59.5));
        Assert.Equal(pinnedAt, sim.Unit("blue").Position);

        // Wounded at 60 s, back on its feet after the 1 s posture change, then walking at half speed.
        sim.RunFor(TimeSpan.FromSeconds(3));
        var wounded = Assert.Single(sim.Events.OfType<HealthChanged>(), e => e.AgentId == "blue" && e.Health == Health.Wounded);
        Assert.InRange(wounded.Time, hit.Time + 60 - 1e-9, hit.Time + 60 + SpecV01.DtSeconds + 1e-9);
        sim.Unit("blue").Should().HaveHealth(Health.Wounded).BeIn(Posture.Standing);
        Assert.IsType<MoveThroughCorridor>(sim.Unit("blue").CurrentOrder);

        var before = sim.Unit("blue").Position.Xy;
        sim.Step();
        var expected = SpecV01.Infantry.OpenMps * SpecShooting.Rules.WoundedSpeedFactor * SpecV01.DtSeconds;
        Assert.Equal(expected, sim.Unit("blue").Position.Xy.DistanceTo(before), 9);
    }

    [Fact]
    public void E3_a_second_hit_while_pinned_puts_the_agent_out_of_action()
    {
        var sim = BlueWalksPastRed();
        var hit = sim.RunUntilHit("blue", Plenty);

        // Red keeps firing at 10 m; a prone target is still hit about one shot in five.
        sim.RunUntil(s => s.Unit("blue").Health == Health.OutOfAction, TimeSpan.FromSeconds(59), "blue out of action");

        Assert.Equal([Health.Pinned, Health.OutOfAction], HealthHistory(sim, "blue"));
        Assert.True(sim.TimeSeconds < hit.Time + 60);
    }

    [Fact]
    public void E3_a_hit_on_a_wounded_agent_puts_it_out_of_action()
    {
        var sim = BlueWalksPastRed();
        sim.RunUntilHit("blue", Plenty);
        sim.Order("red", new CeaseFire()).RunFor(TimeSpan.FromSeconds(62));
        sim.Unit("blue").Should().HaveHealth(Health.Wounded);

        sim.Order("red", new FireAt("blue"))
            .RunUntil(s => s.Unit("blue").Health == Health.OutOfAction, Plenty, "blue out of action");

        Assert.Equal([Health.Pinned, Health.Wounded, Health.OutOfAction], HealthHistory(sim, "blue"));
    }

    [Fact]
    public void E4_an_agent_out_of_action_neither_moves_shoots_nor_asks()
    {
        var sim = BlueWalksPastRed();
        sim.RunUntil(s => s.Unit("blue").Health == Health.OutOfAction, Plenty, "blue out of action");
        var outAt = sim.TimeSeconds;
        var position = sim.Unit("blue").Position;

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(position, sim.Unit("blue").Position);
        Assert.Equal(UnitActivity.OutOfAction, sim.Unit("blue").Activity);
        Assert.Null(sim.Unit("blue").CurrentOrder);
        Assert.DoesNotContain(sim.Events, e => e.AgentId == "blue" && e.Time > outAt);
    }

    [Fact]
    public void E5_a_shot_at_an_agent_suppresses_it_for_ten_seconds()
    {
        // 500 m: the first shot is almost surely a miss (P ≈ 2 %); the test checks that it was.
        var sim = BlueWalksPastRed(redDistance: 500, mapWidth: 300);
        sim.RunUntil(s => s.Events.OfType<ShotFired>().Any(), TimeSpan.FromSeconds(10), "red fires");
        var shot = sim.Events.OfType<ShotFired>().Single();
        Assert.False(shot.Hit, "precondition: the first shot misses");
        sim.Order("red", new CeaseFire());
        var suppressedAt = sim.Unit("blue").Position;

        sim.Unit("blue").Should().BeSuppressed().BeIn(Posture.Prone).HaveHealth(Health.Unhurt);
        sim.RunFor(TimeSpan.FromSeconds(9));
        sim.Unit("blue").Should().BeSuppressed();
        Assert.Equal(suppressedAt, sim.Unit("blue").Position);

        // Suppression ends at 10 s; standing up takes 1 s; then blue walks on with its order.
        sim.RunFor(TimeSpan.FromSeconds(3));
        sim.Unit("blue").Should().BeIn(Posture.Standing);
        Assert.Equal(UnitActivity.Moving, sim.Unit("blue").Activity);
        Assert.True(sim.Unit("blue").Position.Xy.DistanceTo(suppressedAt.Xy) > 0);
        Assert.IsType<MoveThroughCorridor>(sim.Unit("blue").CurrentOrder);
    }

    [Fact]
    public void E6_each_new_shot_extends_suppression()
    {
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 40).Build())
            .WithFighter("red", new Vec2(11, 21), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("blue", new Vec2(561, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new FireAt("blue"));

        sim.RunFor(TimeSpan.FromSeconds(30));
        sim.Order("red", new CeaseFire()).RunFor(TimeSpan.FromSeconds(15));

        var lastShot = sim.Events.OfType<ShotFired>().Last().Time;
        var ended = Assert.Single(sim.Events.OfType<SuppressionEnded>(), e => e.AgentId == "blue");
        Assert.InRange(ended.Time, lastShot + 10 - 1e-9, lastShot + 10 + SpecV01.DtSeconds + 1e-9);
    }
}
