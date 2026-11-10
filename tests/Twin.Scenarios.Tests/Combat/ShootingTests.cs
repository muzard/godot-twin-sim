using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Firing (backlog 0002, F): red has a <see cref="FireAt"/> order on blue. Blue holds fire, so only red shoots.
/// One aimed shot every 2 s while the target is visible and in range, until the rounds run out.
/// </summary>
public class ShootingTests
{
    private static readonly Vec2 Red = new(11, 11);

    private static ScenarioRun RedFiresAt(RasterWorld world, Vec2 blue, WeaponProfile? weapon = null) => Scenario.ForCombat()
        .WithTerrain(world)
        .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire, weapon)
        .WithFighter("blue", blue, Side.Blue, RulesOfEngagement.HoldFire)
        .Build()
        .Order("red", new FireAt("blue"));

    private static List<ShotFired> ShotsBy(ScenarioRun sim, string shooter) =>
        sim.Events.OfType<ShotFired>().Where(e => e.AgentId == shooter).ToList();

    [Fact]
    public void F2_beyond_maximum_range_no_shot_is_fired()
    {
        var sim = RedFiresAt(TestMap.Create(widthMeters: 700, heightMeters: 20).Build(), blue: Red + new Vec2(602, 0));

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(sim.Simulation.CanSee("red", "blue"), "precondition: blue is visible, only out of range");
        Assert.Empty(ShotsBy(sim, "red"));
    }

    [Fact]
    public void F5_an_agent_fires_one_shot_every_two_seconds()
    {
        // 400 m: a hit is unlikely, but hits would not change the rate (only putting blue out of action ends the order).
        var sim = RedFiresAt(TestMap.Create(widthMeters: 500, heightMeters: 20).Build(), blue: Red + new Vec2(400, 0));

        sim.RunFor(TimeSpan.FromSeconds(60));

        var shots = ShotsBy(sim, "red");
        Assert.True(shots.Count >= 20, $"expected about 30 shots in 60 s, got {shots.Count}");
        Assert.All(shots.Zip(shots.Skip(1)), pair => Assert.Equal(2.0, pair.Second.Time - pair.First.Time, 9));
    }

    [Fact]
    public void F6_an_agent_without_ammunition_does_not_fire()
    {
        var sim = RedFiresAt(TestMap.Create(widthMeters: 600, heightMeters: 20).Build(), blue: Red + new Vec2(550, 0),
            weapon: SpecShooting.Rifle with { Rounds = 3 });

        sim.RunFor(TimeSpan.FromSeconds(60));

        Assert.Equal(3, ShotsBy(sim, "red").Count);
        Assert.Equal(0, sim.Unit("red").RoundsLeft);
    }

    [Fact]
    public void F7_no_shot_is_fired_without_line_of_sight()
    {
        var world = TestMap.Create(widthMeters: 200, heightMeters: 20).WithRidge(fromX: 60, toX: 64, height: 5).Build();
        var sim = RedFiresAt(world, blue: Red + new Vec2(100, 0));

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Empty(ShotsBy(sim, "red"));
        Assert.IsType<FireAt>(sim.Unit("red").CurrentOrder);
    }
}
