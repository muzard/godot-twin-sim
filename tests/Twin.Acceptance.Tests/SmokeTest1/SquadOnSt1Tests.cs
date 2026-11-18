using Twin.Sim.Command;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// The reference squad on the real MML area <c>scenarios/st1</c> (backlog 0004). Found running the st1 ambush demo: a
/// forest road slants across the corridor's first leg near x = 300; members of the wedge's right arm reach it first and
/// wait at its edge while the squad leader walks up to deploy. They must not hold the squad (before the fix the squad
/// stood still at x ≈ 311 for good).
/// </summary>
public class SquadOnSt1Tests
{
    [Fact]
    public void A_squad_in_a_wedge_gets_past_the_forest_road_on_the_first_leg()
    {
        var st1 = ScenarioOnDisk.Load("st1").Definition;
        var corridor = st1.Corridor;
        var sim = Scenario.ForSquad()
            .WithTerrain(st1.World.World)
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, corridor.Centerline[0])
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(30), permitRadiusMeters: 25)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(120))
            .Build()
            .Order("rj", new MoveThroughCorridor(corridor));

        sim.RunUntil(s => s.Unit("rj").Position.X > 330, TimeSpan.FromMinutes(60), "rj past the forest road (x > 330)");
    }
}
