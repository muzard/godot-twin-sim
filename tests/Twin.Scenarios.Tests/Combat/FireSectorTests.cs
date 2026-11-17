using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Never fire towards one's own (backlog 0003, LF). A shooter's fire sector is everything within 30° either side of its
/// direction to the target, in front of it, out to the weapon's maximum range. No shot is fired while an own fighter is
/// in it, whatever the reason for the shot: an order, a rule of engagement or the drill.
/// Blue at (21, 101) and red 100 m east; blue-2 is the team-mate.
/// </summary>
public class FireSectorTests
{
    private static readonly Vec2 Blue = new(21, 101);
    private static readonly Vec2 Red = new(121, 101);

    private static Vec2 At(double degrees, double meters) =>
        Blue + new Vec2(Math.Cos(degrees * Math.PI / 180) * meters, Math.Sin(degrees * Math.PI / 180) * meters);

    // Blue engages red either under free fire or on a FireAt order; red holds fire.
    private static ScenarioRun BlueEngagesRed(Vec2 friend, bool fireAtOrder)
    {
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 200).Build())
            .WithFighter("blue", Blue, Side.Blue, fireAtOrder ? RulesOfEngagement.HoldFire : RulesOfEngagement.FreeFire)
            .WithFighter("blue-2", friend, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        return fireAtOrder ? sim.Order("blue", new FireAt("red")) : sim;
    }

    private static double AngleDegrees(Vec2 from, Vec2 a, Vec2 b) =>
        Math.Acos(Math.Clamp((a - from).Normalized().Dot((b - from).Normalized()), -1, 1)) * 180 / Math.PI;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LF1_no_shot_is_fired_with_an_own_fighter_within_thirty_degrees_of_the_muzzle(bool fireAtOrder)
    {
        var sim = BlueEngagesRed(friend: At(25, 50), fireAtOrder);

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.True(sim.Simulation.CanSee("blue", "red"), "precondition: blue sees red");
        Assert.Empty(sim.ShotsBy("blue"));
    }

    [Fact]
    public void LF2_a_friend_beyond_the_target_inside_the_sector_also_blocks_fire()
    {
        var sim = BlueEngagesRed(friend: At(10, 150), fireAtOrder: true);

        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Empty(sim.ShotsBy("blue"));
    }

    [Theory]
    [InlineData(35, 50)]
    [InlineData(-35, 50)]
    [InlineData(180, 10)]
    public void LF3_a_friend_outside_the_sector_or_behind_the_shooter_does_not(double degrees, double meters)
    {
        var sim = BlueEngagesRed(friend: At(degrees, meters), fireAtOrder: true);

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(sim.ShotsBy("blue"));
    }

    [Fact]
    public void LF3_a_friend_beyond_the_weapons_range_does_not()
    {
        // Rounds do not reach past 600 m, so a friend there is safe.
        var sim = Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: 700, heightMeters: 40).Build())
            .WithFighter("blue", new Vec2(11, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(111, 21), Side.Red, RulesOfEngagement.HoldFire)
            .WithFighter("blue-2", new Vec2(631, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new FireAt("red"));

        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(sim.ShotsBy("blue"));
    }

    [Fact]
    public void LF4_the_shooter_fires_once_the_sector_clears()
    {
        // Blue-2 stands on the line 40 m out and walks north, out of the sector after about 23 m.
        var sim = BlueEngagesRed(friend: Blue + new Vec2(40, 0), fireAtOrder: true)
            .Order("blue-2", new MoveTo(Blue + new Vec2(40, 90)));

        sim.RunUntil(s => s.ShotsBy("blue").Any(), TimeSpan.FromMinutes(2), "blue fires");

        // The first tick after which blue-2 is 30° or more off the line to red. Blue fires on that tick or the next.
        var first = sim.ShotsBy("blue")[0];
        var cleared = Enumerable.Range(0, sim.Trail("blue-2").Count)
            .Select(i => (double?)(i * SpecV01.DtSeconds))
            .FirstOrDefault(t => AngleDegrees(Blue, Red, sim.PositionAt("blue-2", t!.Value)) >= 30);
        Assert.True(cleared is not null,
            $"blue fired at t={first.Time} while blue-2 was still inside the sector, at {sim.PositionAt("blue-2", first.Time)}");
        Assert.InRange(first.Time, cleared.Value, cleared.Value + SpecV01.DtSeconds);
    }
}
