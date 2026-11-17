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
/// probability, and it resumes its order. A second hit puts it out of action. No test-only hit model: the tests run
/// until a real hit happens.
/// <para>
/// Backlog 0003 replaced 0002's E5 and E6 ("every shot suppresses for 10 s", "each new shot extends it") with suppression
/// as fire pressure; see <see cref="SuppressionTests"/> (SU4–SU7).
/// </para>
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
}
