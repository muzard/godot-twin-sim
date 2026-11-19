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
/// Forward and lateral forest-edge crossings (backlog 0004, decided with the product owner 2026-11-19). The squad's
/// forest-edge permit covers crossings in the direction of movement only. A member that would leave the forest sideways
/// because the formation is wider than the forest does not cross and does not ask: it keeps inside the edge, and the
/// formation narrows.
/// Beside the forest bands below lies bog, slower than forest, so the leader's own route keeps to the forest.
/// </summary>
public class SquadForestPermitEdgeCaseTests
{
    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);
    private const double PermitRadius = 10;

    private static Scenario Squad(RasterWorld world, SquadFormation formation, Vec2 at) => Scenario.ForSquad()
        .WithTerrain(world)
        .WithSquad(SpecSquad.Reference() with { Formation = formation }, at)
        .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: PermitRadius)
        .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60));

    private static bool InForest(RasterWorld world, Vec2 p) => ((IWorldQuery)world).ClassAt(p.X, p.Y) == TerrainClass.Forest;

    [Fact]
    public void A_member_does_not_leave_the_forest_sideways_and_does_not_ask()
    {
        // A band of forest 40 m wide (y 80–120) along the route east, bog on both sides. The wedge's arms reach 4 wedge
        // steps (28 m) to each side of rj at y = 101, beyond the band: the members whose spots lie in the bog keep inside
        // the edge instead, and the squad walks on narrowed.
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Bog)
            .Fill(TerrainClass.Forest, 0, 80, 500, 120).Build();
        var start = new Vec2(40, 101);
        var b = new Vec2(450, 101);
        var corridor = new Corridor([start, b], 120, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);
        var sim = Squad(world, SquadFormation.Wedge, start).Build().Order("rj", new MoveThroughCorridor(corridor));

        sim.RunUntil(s => s.Unit("rj").Position.Xy.DistanceTo(b) < 0.5, HalfAnHour, "rj at B");
        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.Empty(sim.Events.OfType<PermissionRequested>());
        foreach (var member in SpecSquad.Reference().Members)
        {
            var violations = RouteAudit.Violations(world, corridor, sim.Trail(member.UnitId), []);
            Assert.True(violations.Count == 0, $"{member.UnitId}: " + string.Join("; ", violations));
            Assert.True(sim.Unit(member.UnitId).Position.Xy.DistanceTo(b) <= 40,
                $"{member.UnitId} should have come along to B, but is at {sim.Unit(member.UnitId).Position.Xy}");
        }
    }

    [Fact]
    public void The_squads_forest_edge_permit_does_not_cover_leaving_the_forest_sideways()
    {
        // Forest west of x = 100, open land to x = 130, then a band of forest 40 m wide (y 80–120) with bog on both sides.
        // The squad leaves the first forest forward on a permit, crosses the open land in file and walks into the band;
        // past x = 250 rj commands the wedge. The permit does not cover the band's sides: no member leaves the band.
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Bog)
            .Fill(TerrainClass.Forest, 0, 0, 100, 200)
            .Fill(TerrainClass.Open, 100, 0, 130, 200)
            .Fill(TerrainClass.Forest, 130, 80, 500, 120).Build();
        var start = new Vec2(40, 101);
        var b = new Vec2(450, 101);
        var sim = Squad(world, SquadFormation.File, start).Build()
            .Order("rj", new MoveThroughCorridor(new Corridor([start, b], 120, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));
        sim.RunUntil(s => s.Unit("rj").Position.X >= 250, HalfAnHour, "rj in the band past x = 250");
        Assert.Contains(sim.Squad("r1").Permits, p => p.BoundaryId == HardBoundary.ForestEdgeId); // precondition
        var requestsBefore = sim.Events.OfType<PermissionRequested>().Count();

        sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        var outside = new SortedSet<string>(StringComparer.Ordinal);
        sim.RunUntil(s =>
        {
            foreach (var member in SpecSquad.Reference().Members)
            {
                if (!InForest(world, s.Unit(member.UnitId).Position.Xy))
                {
                    outside.Add($"{member.UnitId} at {s.Unit(member.UnitId).Position.Xy}");
                }
            }

            return s.Unit("rj").Position.Xy.DistanceTo(b) < 0.5;
        }, HalfAnHour, "rj at B");

        Assert.True(outside.Count == 0, "no member should leave the band after the wedge command: " + string.Join("; ", outside.Take(5)));
        Assert.Equal(requestsBefore, sim.Events.OfType<PermissionRequested>().Count());
    }
}
