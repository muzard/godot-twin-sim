using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>A listener learns the weapon class of a shot it hears (backlog 0004 S5a2, WS3).</summary>
public class ShotHearingTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    // blue fires at red across open ground; bravo, on blue's side, stands 40 m north of blue, out of its fire sector (a
    // friend in it would stop blue firing), and only listens.
    private static ScenarioRun Firing(WeaponProfile weapon) => Scenario.ForCombat()
        .WithTerrain(TestMap.Create(widthMeters: 400, heightMeters: 100).Build())
        .WithFighter("blue", new Vec2(50, 51), Side.Blue, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(weapon))
        .WithFighter("red", new Vec2(150, 51), Side.Red, RulesOfEngagement.HoldFire)
        .WithFighter("bravo", new Vec2(50, 91), Side.Blue, RulesOfEngagement.HoldFire)
        .Build()
        .Order("blue", new FireAt("red"));

    [Theory]
    [InlineData(WeaponClass.Rifle)]
    [InlineData(WeaponClass.MachineGun)]
    [InlineData(WeaponClass.HeavyMachineGun)]
    public void WS3_a_listener_learns_the_weapon_class_of_the_shot_it_hears(WeaponClass weaponClass)
    {
        var weapon = weaponClass switch
        {
            WeaponClass.Rifle => SpecShooting.Rifle,
            WeaponClass.MachineGun => SpecSuppression.MachineGun,
            _ => SpecSuppression.HeavyMachineGun,
        };
        var sim = Firing(weapon);

        sim.RunUntil(s => s.Events.OfType<ShotHeard>().Any(e => e.AgentId == "bravo"), OneMinute, "bravo hears a shot");

        var heard = sim.Events.OfType<ShotHeard>().First(e => e.AgentId == "bravo");
        Assert.Equal(("blue", weaponClass), (heard.ShooterId, heard.WeaponClass));
    }
}
