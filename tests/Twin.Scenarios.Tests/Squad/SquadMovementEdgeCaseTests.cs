using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Edge cases of squad movement found in review (backlog 0004, S2a): a member whose slot is far away behind an obstacle
/// still finds its way, and a squad whose leader falls mid-move does not walk off in an arbitrary direction. Since S2b the
/// successor carries the order on, so the members re-form round it along its route.
/// </summary>
public class SquadMovementEdgeCaseTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    private static Corridor Straight() => new([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);

    [Fact]
    public void A_member_finds_its_way_round_an_obstacle_to_a_slot_far_away()
    {
        // k1 is sent 70 m north (a plain move goes straight through anything); a 20 m wide building then stands between it
        // and the squad, 26–41 m south of it, so the straight way back is blocked and its slot is beyond the search window.
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Fill(TerrainClass.Building, 150, 130, 190, 145).Build())
            .WithSquad(SpecSquad.Reference(), Start)
            .Build();
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Position.X > 150, TenMinutes, "rj past x = 150");
        sim.Order("k1", new MoveTo(new Vec2(170, 171))).RunUntil(s => s.Unit("k1").Position.Y >= 171 - 1e-9, TenMinutes, "k1 sent away");

        sim.Order("k1", new Rejoin()).RunUntilSquadSettled("r1", TenMinutes);

        sim.ShouldBeInSlot("k1", toleranceMeters: 1);
        Assert.All(sim.Trail("k1").SkipWhile(p => p.Y < 171 - 1e-9),
            p => Assert.NotEqual(TerrainClass.Building, ((IWorldQuery)sim.Terrain).ClassAt(p.X, p.Y)));
    }

    [Fact]
    public void Members_re_form_round_the_successor_along_its_route_when_the_leader_falls_mid_move()
    {
        // red stands 6 m north of the route at x = 200 and holds fire until ordered.
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = Twin.Sim.Squads.SquadFormation.Wedge }, Start)
            .WithFighter("red", new Vec2(200, 107), Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Position.X > 195, TenMinutes, "rj near red");
        sim.RunUntilOutOfAction("rj", "red", TenMinutes);
        var members = SpecSquad.Reference().Members.Select(m => m.UnitId).Where(id => id != "rj").ToList();
        var before = members.ToDictionary(id => id, id => sim.Unit(id).Position.Xy);

        sim.RunFor(TimeSpan.FromSeconds(60)); // vj takes command after 5 s and carries the order on (S2b)

        Assert.Equal("vj", sim.Squad("r1").LeaderId);
        var leader = sim.Unit("vj").Position.Xy;
        foreach (var id in members.Where(id => id != "vj"))
        {
            sim.ShouldBeInSlot(id, toleranceMeters: 1);
            Assert.True(sim.Unit(id).Position.Xy.DistanceTo(leader) <= 6 * SpecSquad.IntervalMeters,
                $"{id} is {sim.Unit(id).Position.Xy.DistanceTo(leader):0.##} m from the new leader: it walked off");
            Assert.True(sim.Unit(id).Position.Xy.DistanceTo(before[id]) <= 6 * SpecSquad.IntervalMeters);
        }
    }
}
