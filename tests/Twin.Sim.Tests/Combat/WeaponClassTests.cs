using Twin.Sim.Combat;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>Every weapon has a class, which a listener recognises by the sound (backlog 0004 S5a2, WS3).</summary>
public class WeaponClassTests
{
    [Fact]
    public void The_simulations_weapons_have_their_classes()
    {
        Assert.Equal(WeaponClass.Rifle, WeaponProfile.Rifle.Class);
        Assert.Equal(WeaponClass.MachineGun, WeaponProfile.MachineGun.Class);
        Assert.Equal(WeaponClass.HeavyMachineGun, WeaponProfile.HeavyMachineGun.Class);
    }

    [Fact]
    public void The_heavy_machine_gun_has_the_values_of_backlog_0003()
    {
        Assert.Equal(SpecSuppression.HeavyMachineGun, WeaponProfile.HeavyMachineGun);
    }
}
