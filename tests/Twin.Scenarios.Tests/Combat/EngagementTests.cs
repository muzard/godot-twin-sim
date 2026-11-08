using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Orders and rules of engagement (backlog 0002, R):
/// <list type="bullet">
/// <item><c>HoldFire</c>: the agent shoots only on a <see cref="FireAt"/> order.</item>
/// <item><c>ReturnFire</c> (the default): an agent fired at has the permission and the duty to shoot back at the shooter
/// as soon as it sees it.</item>
/// <item><c>FreeFire</c>: as <c>ReturnFire</c>, and the agent also engages the nearest visible enemy.</item>
/// </list>
/// Agents never shoot at their own side.
/// </summary>
public class EngagementTests
{
    private static readonly Vec2 Blue = new(21, 21);

    private static RasterWorld Open(double width = 300, double height = 40) => TestMap.Create(widthMeters: width, heightMeters: height).Build();

    private static List<ShotFired> ShotsBy(ScenarioRun sim, string shooter) =>
        sim.Events.OfType<ShotFired>().Where(e => e.AgentId == shooter).ToList();

    [Fact]
    public void R1_fire_at_an_own_side_agent_an_unknown_agent_or_oneself_is_rejected()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("blue-2", Blue + new Vec2(10, 0), Side.Blue, RulesOfEngagement.FreeFire)
            .Build();

        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new FireAt("blue-2")));
        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new FireAt("nobody")));
        Assert.Throws<OrderRejectedException>(() => sim.Order("blue", new FireAt("blue")));
    }

    [Fact]
    public void R2_hold_fire_agent_does_not_shoot_even_when_fired_at()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new FireAt("blue"));

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.NotEmpty(ShotsBy(sim, "red"));
        Assert.Empty(ShotsBy(sim, "blue"));
    }

    [Fact]
    public void R3_return_fire_agent_must_shoot_back_at_whoever_shot_at_it()
    {
        // Red fires a single round. If it hits, blue is pinned for 60 s, but the duty to return fire remains.
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire, SpecShooting.Rifle with { Rounds = 1 })
            .Build()
            .Order("red", new FireAt("blue"));

        sim.RunUntil(s => ShotsBy(s, "blue").Count > 0, TimeSpan.FromMinutes(3), "blue returns fire");

        Assert.Equal("red", ShotsBy(sim, "blue")[0].TargetId);
        Assert.True(ShotsBy(sim, "blue")[0].Time > ShotsBy(sim, "red")[0].Time);
    }

    [Fact]
    public void R4_return_fire_agent_does_not_open_fire_first()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.ReturnFire)
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.True(sim.Simulation.CanSee("blue", "red"));
        Assert.Empty(sim.Events.OfType<ShotFired>());
    }

    [Fact]
    public void R5_free_fire_agent_engages_a_shooter_it_sees_only_by_its_muzzle_flash()
    {
        // Red fires one round at bravo nearby. Charlie looks at red through 90 m of forest: hidden except while the
        // muzzle flash lasts (2 s). The flash only matters to third parties, because a duelling pair sees each other
        // through the same forest.
        var world = TestMap.Create(widthMeters: 200, heightMeters: 40)
            .Fill(TerrainClass.Forest, fromX: 20, fromY: 0, toX: 110, toY: 40)
            .Build();
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("red", new Vec2(11, 21), Side.Red, RulesOfEngagement.HoldFire, SpecShooting.Rifle with { Rounds = 1 })
            .WithFighter("bravo", new Vec2(11, 5), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("charlie", new Vec2(131, 21), Side.Blue, RulesOfEngagement.FreeFire)
            .Build()
            .Order("red", new FireAt("bravo"));

        sim.RunFor(TimeSpan.FromSeconds(10));

        var redShot = Assert.Single(ShotsBy(sim, "red"));
        var answer = ShotsBy(sim, "charlie").First();
        Assert.Equal("red", answer.TargetId);
        Assert.InRange(answer.Time, redShot.Time, redShot.Time + SpecShooting.Rifle.FlashSeconds);
    }

    [Fact]
    public void R6_free_fire_agent_engages_the_nearest_visible_enemy()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("red-far", Blue + new Vec2(120, 0), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("red-near", Blue + new Vec2(80, 0), Side.Red, RulesOfEngagement.HoldFire)
            .Build();

        sim.RunUntil(s => ShotsBy(s, "blue").Count > 0, TimeSpan.FromSeconds(10), "blue fires");

        Assert.Equal("red-near", ShotsBy(sim, "blue")[0].TargetId);
    }

    [Fact]
    public void R6_between_enemies_at_the_same_distance_the_first_by_id_is_engaged()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open(width: 300, height: 200))
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("red-b", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("red-a", Blue + new Vec2(0, 100), Side.Red, RulesOfEngagement.HoldFire)
            .Build();

        sim.RunUntil(s => ShotsBy(s, "blue").Count > 0, TimeSpan.FromSeconds(10), "blue fires");

        Assert.Equal("red-a", ShotsBy(sim, "blue")[0].TargetId);
    }

    [Fact]
    public void R7_fire_at_stops_when_the_target_is_out_of_action()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Blue + new Vec2(0, 10), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new FireAt("blue"));
        sim.RunUntil(s => s.Unit("blue").Health == Health.OutOfAction, TimeSpan.FromMinutes(5), "blue out of action");
        var outAt = sim.TimeSeconds;

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(ShotsBy(sim, "red"), e => e.Time > outAt);
        Assert.Null(sim.Unit("red").CurrentOrder);
    }

    [Fact]
    public void R8_an_agent_keeps_moving_along_its_corridor_while_firing()
    {
        var corridor = new Corridor([Blue, new Vec2(281, 21)], Width: 20, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);
        var sim = Scenario.ForCombat().WithTerrain(Open(width: 300, height: 200))
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("red", new Vec2(151, 121), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor));

        sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.NotEmpty(ShotsBy(sim, "blue"));
        var walked = sim.Unit("blue").Position.Xy.DistanceTo(Blue);
        Assert.True(walked >= 0.9 * SpecV01.Infantry.OpenMps * 20, $"blue walked only {walked:0.0} m in 20 s");
    }

    [Fact]
    public void R9_agents_never_shoot_their_own_side()
    {
        var sim = Scenario.ForCombat().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("blue-2", Blue + new Vec2(50, 0), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Blue + new Vec2(100, 0), Side.Red, RulesOfEngagement.HoldFire)
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.NotEmpty(ShotsBy(sim, "blue"));
        Assert.All(ShotsBy(sim, "blue"), shot => Assert.Equal("red", shot.TargetId));
    }
}
