using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Fire modes in a fight (backlog 0003, M): a <see cref="FireAt"/> order and fire opened on one's own under free fire
/// are aimed; the drill's first shots are snap shots, one per snap interval (0.7 s for the rifle). A machine-gun burst is
/// one shot event of five rounds.
/// </summary>
public class FireModeTests
{
    private static readonly Vec2 Blue = new(21, 21);

    private static ScenarioRun Duel(RulesOfEngagement blue, WeaponProfile? red = null, double distance = 100) =>
        Scenario.ForCombat()
            .WithTerrain(TestMap.Create(widthMeters: distance + 40, heightMeters: 40).Build())
            .WithFighter("blue", Blue, Side.Blue, blue)
            .WithFighter("red", Blue + new Vec2(distance, 0), Side.Red, RulesOfEngagement.HoldFire, red)
            .Build();

    [Fact]
    public void M3_fire_at_orders_and_free_fire_engagements_are_aimed()
    {
        var ordered = Duel(RulesOfEngagement.HoldFire).Order("blue", new FireAt("red"));
        var free = Duel(RulesOfEngagement.FreeFire);

        ordered.RunFor(TimeSpan.FromSeconds(10));
        free.RunFor(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(ordered.ShotsBy("blue"));
        Assert.NotEmpty(free.ShotsBy("blue"));
        Assert.All(ordered.ShotsBy("blue").Concat(free.ShotsBy("blue")), s => Assert.Equal(FireMode.Aimed, s.Mode));
    }

    [Fact]
    public void M4_snap_shots_come_at_the_snap_interval()
    {
        // Red fires one round from 500 m; blue answers with three snap shots.
        var sim = Duel(RulesOfEngagement.ReturnFire, SpecSuppression.NeverHits(SpecShooting.Rifle) with { Rounds = 1 }, distance: 500)
            .Order("red", new FireAt("blue"));

        sim.RunUntil(s => s.ShotsBy("blue").Count >= 3, TimeSpan.FromSeconds(20), "three snap shots");

        var snaps = sim.ShotsBy("blue").Take(3).ToList();
        Assert.All(snaps, s => Assert.Equal(FireMode.Snap, s.Mode));
        var interval = SpecShooting.Rifle.SnapSecondsPerShot;
        Assert.All(snaps.Zip(snaps.Skip(1)), pair =>
            Assert.InRange(pair.Second.Time - pair.First.Time, interval, interval + SpecV01.DtSeconds));
    }

    [Fact]
    public void M5_a_burst_is_one_shot_event_of_five_rounds()
    {
        var sim = Duel(RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecSuppression.MachineGun) with { Rounds = 20 })
            .Order("red", new FireAt("blue"));

        sim.RunUntil(s => s.ShotsBy("red").Any(), TimeSpan.FromSeconds(10), "red fires");

        var burst = Assert.Single(sim.ShotsBy("red"));
        Assert.Equal(5, burst.Rounds);
        Assert.Equal(15, sim.Unit("red").RoundsLeft);
    }
}
