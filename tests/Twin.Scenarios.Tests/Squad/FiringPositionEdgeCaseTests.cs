using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Edge cases of firing positions in contact found in review (backlog 0005). Members remember the enemies they know of,
/// so once the squad moves on, a member that cannot fire at one must follow its squad rather than seek a firing position,
/// or the squad would wait for it for good.
/// </summary>
public class FiringPositionEdgeCaseTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    private static readonly Vec2 Destination = new(420, 101);

    [Fact]
    public void Once_the_squad_moves_on_no_member_stays_behind_for_a_firing_position()
    {
        // The wedge heads east under return fire, its weapons never hitting; red stands in the open south-east, holding
        // fire. The point team's rifles reach only 30 m, so it cannot fire at red when the squad halts on it.
        WeaponProfile Weapons(SquadMemberSpec m) => m.Team == SpecSquad.PointTeam
            ? SpecFiringPositions.ShortRange(SpecSuppression.NeverHits(SpecShooting.Rifle), 30)
            : SpecSuppression.NeverHits(m.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);
        var sim = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, new Vec2(80, 101),
                rules: RulesOfEngagement.ReturnFire, weapons: Weapons)
            .WithFighter("red", new Vec2(300, 40), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("rj", new MoveTo(Destination));
        sim.RunUntil(s => s.Squad("r1").IsHalted, TimeSpan.FromMinutes(5), "the squad halts on red");
        sim.RunUntil(s => s.Events.OfType<CannotFireReported>().Any() ||
            SpecSquad.Reference().Members.Any(m => s.Unit(m.UnitId).Activity == UnitActivity.MovingToFiringPosition),
            TwoMinutes, "precondition: a member seeks a firing position or reports it cannot fire");
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.Order("rj", new ResumeMovement()).Step();
        sim.RunUntil(s => s.Unit("rj").Position.Xy.DistanceTo(Destination) < 0.5, TimeSpan.FromMinutes(10), "rj at its destination");

        sim.RunUntilSquadSettled("r1", TimeSpan.FromMinutes(2)); // and every member with it, in its slot
    }
}
