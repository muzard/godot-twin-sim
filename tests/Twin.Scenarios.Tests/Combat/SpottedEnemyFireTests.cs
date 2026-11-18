using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Fire and spotting (backlog 0004 S5a1: SP11). A fighter fires aimed shots only at an enemy it has spotted itself; at an
/// enemy it has not spotted it could only fire toward the direction, and direction fire is a later ticket. So free fire
/// engages only spotted enemies. An agent fired on turns to its attacker (0003's drill watches the nearest attacker), so
/// it sees the muzzle flash and returns fire as in 0003. Red stands 100 m east of blue.
/// </summary>
public class SpottedEnemyFireTests
{
    private static readonly Vec2 Blue = new(21, 31);
    private static readonly Vec2 Red = new(121, 31);
    private static readonly Vec2 East = new(1, 0);
    private static readonly Vec2 West = new(-1, 0);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    private static RasterWorld Open() => TestMap.Create(widthMeters: 300, heightMeters: 60).Build();

    /// <summary>Free-fire blue watching 60° to the left of red: it needs 3 × 2.5 s = 7.5 s to spot it.</summary>
    private static ScenarioRun FreeFireWatching(Vec2 watching) => Scenario.ForSpotting().WithTerrain(Open())
        .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.FreeFire)
        .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire)
        .Build()
        .Order("blue", new Watch(watching));

    private static Vec2 SixtyDegreesLeftOfRed => SpecSpotting.Turned(East, -60);

    [Fact]
    public void SP11_a_free_fire_fighter_does_not_fire_at_an_enemy_it_has_not_spotted()
    {
        var sim = FreeFireWatching(SixtyDegreesLeftOfRed);

        var spotted = sim.RunUntilSpotted("blue", "red", OneMinute);

        Assert.True(spotted.Time >= 7.5 - 1e-9, $"blue should need 7.5 s to spot red, but spotted it at t={spotted.Time} s");
        Assert.DoesNotContain(sim.ShotsBy("blue"), shot => shot.Time < spotted.Time);
    }

    [Fact]
    public void SP11_a_free_fire_fighter_fires_once_it_has_spotted_the_enemy()
    {
        var sim = FreeFireWatching(SixtyDegreesLeftOfRed);
        var spotted = sim.RunUntilSpotted("blue", "red", OneMinute);

        sim.RunUntil(s => s.ShotsBy("blue").Count > 0, TimeSpan.FromSeconds(5), "blue fires");

        var first = sim.ShotsBy("blue")[0];
        Assert.Equal("red", first.TargetId);
        sim.ShouldBeAt(first, 0, spotted.Time, "blue's first shot at red");
    }

    [Fact]
    public void SP11_a_fighter_ordered_to_fire_fires_only_once_it_has_spotted_its_target()
    {
        var sim = Scenario.ForSpotting().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new Watch(SixtyDegreesLeftOfRed))
            .Step()
            .Order("blue", new FireAt("red"));

        var spotted = sim.RunUntilSpotted("blue", "red", OneMinute);
        sim.RunUntil(s => s.ShotsBy("blue").Count > 0, TimeSpan.FromSeconds(5), "blue fires");

        Assert.True(spotted.Time >= 7.5 - 1e-9, $"blue should need 7.5 s to spot red, but spotted it at t={spotted.Time} s");
        sim.ShouldBeAt(sim.ShotsBy("blue")[0], 0, spotted.Time, "blue's first shot at red");
    }

    [Fact]
    public void SP11_a_free_fire_fighter_never_fires_at_an_enemy_behind_it()
    {
        var sim = FreeFireWatching(West);

        sim.RunFor(OneMinute);

        Assert.True(sim.Simulation.CanSee("blue", "red"), "the line of sight is clear; red is only behind blue");
        Assert.Empty(sim.ShotsBy("blue"));
    }

    [Fact]
    public void SP11_a_fighter_fired_on_from_behind_turns_to_its_attacker_and_returns_fire()
    {
        var sim = Scenario.ForSpotting().WithTerrain(Open())
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new Watch(West))
            .Order("red", new Watch(West)); // red watches blue
        sim.Step();
        sim.Order("red", new FireAt("blue"));

        sim.RunUntil(s => s.ShotsBy("blue").Count > 0, OneMinute, "blue returns fire");

        Assert.Equal("red", sim.ShotsBy("blue")[0].TargetId);
        sim.Unit("blue").Should().BeWatching(Red - Blue, toleranceDegrees: 5);
    }
}
