using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// A shot's signature (backlog 0002, S and L8): it is heard by everyone within the weapon's sound range, terrain or not,
/// and for the weapon's flash time the shooter is visible through more forest (T ≥ 0.01 instead of 0.05).
/// Red fires one round at bravo, 16 m away in the open. Everyone else holds fire.
/// </summary>
public class SoundAndFlashTests
{
    private static readonly Vec2 Red = new(11, 21);
    private static readonly Vec2 Bravo = new(11, 5);

    private static WeaponProfile OneRound(WeaponProfile? weapon = null) => (weapon ?? SpecShooting.Rifle) with { Rounds = 1 };

    // Charlie looks at red through 90 m of forest (x 20..110): T ≈ 0.027, between the flash threshold and the normal one.
    private static ScenarioRun BehindForest(WeaponProfile redWeapon, double width = 200, Vec2? listener = null)
    {
        var world = TestMap.Create(widthMeters: width, heightMeters: 40)
            .Fill(TerrainClass.Forest, fromX: 20, fromY: 0, toX: 110, toY: 40)
            .Build();
        var scenario = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire, redWeapon)
            .WithFighter("bravo", Bravo, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("charlie", new Vec2(131, 21), Side.Blue, RulesOfEngagement.HoldFire);
        if (listener is { } at)
        {
            scenario.WithFighter("listener", at, Side.Blue, RulesOfEngagement.HoldFire);
        }

        return scenario.Build().Order("red", new FireAt("bravo"));
    }

    private static ShotFired RunUntilRedFires(ScenarioRun sim)
    {
        sim.RunUntil(s => s.Events.OfType<ShotFired>().Any(e => e.AgentId == "red"), TimeSpan.FromSeconds(10), "red fires");
        return sim.Events.OfType<ShotFired>().First(e => e.AgentId == "red");
    }

    [Fact]
    public void L8_a_muzzle_flash_shows_the_shooter_through_more_forest_for_two_seconds()
    {
        var sim = BehindForest(OneRound());
        Assert.False(sim.Simulation.CanSee("charlie", "red"), "before the shot red is hidden by 90 m of forest");

        RunUntilRedFires(sim);
        Assert.True(sim.Simulation.CanSee("charlie", "red"), "the muzzle flash shows red");

        sim.RunFor(TimeSpan.FromSeconds(3));
        Assert.False(sim.Simulation.CanSee("charlie", "red"), "3 s later the flash is gone");
    }

    [Fact]
    public void S1_a_shot_is_heard_within_the_weapons_sound_range()
    {
        var world = TestMap.Create(widthMeters: 1700, heightMeters: 40).Build();
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire, OneRound())
            .WithFighter("bravo", Bravo, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("near", Red + new Vec2(1400, 0), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("far", Red + new Vec2(1600, 0), Side.Blue, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new FireAt("bravo"));

        RunUntilRedFires(sim);

        var heard = Assert.Single(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "near");
        Assert.Equal("red", heard.ShooterId);
        Assert.Equal(sim.Unit("red").Position.Xy, heard.ShooterPosition);
        Assert.DoesNotContain(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "far");
    }

    [Fact]
    public void S2_a_shot_is_heard_behind_a_hill()
    {
        var world = TestMap.Create(widthMeters: 400, heightMeters: 40).WithRidge(fromX: 150, toX: 160, height: 20).Build();
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire, OneRound())
            .WithFighter("bravo", Bravo, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("listener", new Vec2(301, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .Build()
            .Order("red", new FireAt("bravo"));

        RunUntilRedFires(sim);

        Assert.False(sim.Simulation.CanSee("listener", "red"));
        Assert.Contains(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "listener" && e.ShooterId == "red");
    }

    [Fact]
    public void S3_sound_and_flash_come_from_the_weapon_profile()
    {
        // A suppressed rifle: heard only to 200 m, no muzzle flash.
        var suppressed = OneRound(SpecShooting.Rifle with { SoundRangeMeters = 200, FlashSeconds = 0 });
        var sim = BehindForest(suppressed, width: 400, listener: new Vec2(311, 21));

        RunUntilRedFires(sim);

        Assert.DoesNotContain(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "listener");
        Assert.False(sim.Simulation.CanSee("charlie", "red"), "no flash to see red by");
    }
}
