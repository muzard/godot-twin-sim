using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// A road through the forest is a forest edge too (stepping onto it leaves the forest, ADR 0009). Found running the
/// st1 ambush demo (backlog 0004, S2c): a road slanting across the route reaches the wedge's right arm long before the
/// RJ; those members wait at its edge while the RJ walks up to deploy, and must not hold the squad, even though their slots
/// lie in the forest on the road's far side.
/// </summary>
public class SquadEdgeDeploymentRoadTests
{
    // A 4 m road through (150, 101), nearly along the route: it runs 3 m east for every 1 m north, so south of the route
    // (the right arm's side) it lies far to the west.
    private static double DistanceToRoad(Vec2 p) => Math.Abs((p.X - 150) - (3 * (p.Y - 101))) / Math.Sqrt(10);

    [Fact]
    public void Members_waiting_at_a_slanting_road_do_not_hold_the_squad_from_deploying_at_it()
    {
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .FillWhere(TerrainClass.Road, p => DistanceToRoad(p) <= 2)
            .Fill(TerrainClass.Open, 300, 0, 500, 200).Build();
        var start = new Vec2(80, 101);
        var sim = Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, start)
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build()
            .Order("rj", new MoveThroughCorridor(new Corridor([start, new Vec2(420, 101)], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));

        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(e => e.AgentId == "rj"), TimeSpan.FromMinutes(10), "rj asks to cross the road");

        var leader = sim.Unit("rj").Position.Xy;
        Assert.True(leader.X < 150, $"rj at {leader} should stand west of the road");
        // 5 m inside the edge: from the road's edge (2 m from its centre line), give or take its 2 m cell steps.
        Assert.InRange(DistanceToRoad(leader) - 2, SpecSquad.EdgeInsetMeters - 2, SpecSquad.EdgeInsetMeters + 2);
    }
}
