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
/// Edge cases of deploying at the forest edge found in review (backlog 0004, S2c): a squad in file deploys too; the RJ
/// deploys at the edge where its route leaves the forest for good, not where it first steps out of it (a squad that
/// starts in the open, a clearing on the way); a leader already closer than 5 m to the edge deploys where it stands.
/// </summary>
public class SquadEdgeDeploymentEdgeCaseTests
{
    private static readonly TimeSpan TwentyMinutes = TimeSpan.FromMinutes(20);
    private const double Tolerance = 1;

    private static ScenarioRun Squad(RasterWorld world, Vec2 start, Vec2 destination, SquadFormation formation) =>
        Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, start)
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build()
            .Order("rj", new MoveThroughCorridor(new Corridor([start, destination], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));

    private static CommandIssued DeploymentCommand(ScenarioRun sim) =>
        sim.Events.OfType<CommandIssued>().First(e => e.AgentId == "rj" &&
            e.Command.StartsWith(SpecSquad.DeployAtForestEdgeCommand, StringComparison.Ordinal));

    [Fact]
    public void A_squad_in_file_walks_up_to_the_forest_edge_and_deploys_too()
    {
        // Forest west of x = 250: pj walks 30 m ahead of rj and reaches the edge long before rj reaches its point; it waits
        // there and does not hold the squad. (From a file the tail needs more than the 60 s limit to reach its spot.)
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 250, 0, 500, 200).Build();
        var sim = Squad(world, new Vec2(80, 101), new Vec2(420, 101), SquadFormation.File);

        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(), TwentyMinutes, "a permission request");

        sim.Unit("rj").Should().BeAt(new Vec2(250 - SpecSquad.EdgeInsetMeters, 101), Tolerance);
        var request = Assert.Single(sim.Events.OfType<PermissionRequested>());
        Assert.Equal("rj", request.AgentId);
        Assert.True(request.Time - DeploymentCommand(sim).Time <= SpecSquad.EdgeDeploymentSeconds + SpecV01.DtSeconds + 1e-9);
        Assert.All(SpecSquad.Reference().Members, m => Assert.True(sim.Unit(m.UnitId).Position.X < 250, $"{m.UnitId} left the forest"));
    }

    [Fact]
    public void A_squad_that_starts_in_the_open_deploys_where_its_route_leaves_the_forest()
    {
        // Open land with a forest band x 150–300: entering the forest needs no permit, leaving it at x = 300 does.
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200).Fill(TerrainClass.Forest, 150, 0, 300, 200).Build();
        var sim = Squad(world, new Vec2(80, 101), new Vec2(420, 101), SquadFormation.Wedge);

        sim.RunUntil(s => s.Events.OfType<EdgeDeployed>().Any(), TwentyMinutes, "squad deployed at the forest edge");

        sim.Unit("rj").Should().BeAt(new Vec2(300 - SpecSquad.EdgeInsetMeters, 101), Tolerance);
        Assert.InRange(DeploymentCommand(sim).Time, 60, double.MaxValue); // not before it has walked through the band
        sim.Step();
        var request = Assert.Single(sim.Events.OfType<PermissionRequested>());
        Assert.Equal(("rj", HardBoundary.ForestEdgeId), (request.AgentId, request.BoundaryId));
        Assert.InRange(request.At.X, 300, 300 + SpecV01.CellSize);
    }

    [Fact]
    public void After_a_clearing_on_the_way_the_first_permit_covers_the_far_edge()
    {
        // Forest with a clearing x 200–230 and open land east of x = 330: the squad deploys at x = 200 and is granted; the
        // squad-wide forest-edge permit (decided 2026-11-19) covers the far edge at x = 330 too, so the squad neither deploys
        // nor asks again there.
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 200, 0, 230, 200)
            .Fill(TerrainClass.Open, 330, 0, 500, 200).Build();
        var sim = Squad(world, new Vec2(80, 101), new Vec2(420, 101), SquadFormation.Wedge);
        var deployedAt = new List<Vec2>();
        sim.RunUntil(s =>
        {
            if (s.Events.OfType<EdgeDeployed>().Count() > deployedAt.Count)
            {
                deployedAt.Add(s.Unit("rj").Position.Xy);
            }

            return s.SquadSettled("r1");
        }, TwentyMinutes, "squad settled at B");

        sim.Unit("rj").Should().BeAt(new Vec2(420, 101));
        var deployed = Assert.Single(deployedAt);
        Assert.InRange(deployed.X, 200 - SpecSquad.EdgeInsetMeters - Tolerance, 200 - SpecSquad.EdgeInsetMeters + Tolerance);
        var request = Assert.Single(sim.Events.OfType<PermissionRequested>());
        Assert.Equal("rj", request.AgentId);
    }

    [Fact]
    public void A_leader_closer_than_5_m_to_the_edge_deploys_where_it_stands()
    {
        // Forest east of x = 100, open land west of it; the squad stands in a row eastward from rj at (103, 101), 3 m inside
        // the edge, and is ordered west out of the forest.
        var world = TestMap.Create(widthMeters: 300, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 0, 0, 100, 200).Build();
        var start = new Vec2(103, 101);
        var sim = Squad(world, start, new Vec2(20, 101), SquadFormation.Wedge);

        sim.RunUntil(s => s.Events.OfType<EdgeDeployed>().Any(), TimeSpan.FromMinutes(2), "squad deployed at the forest edge");

        sim.Unit("rj").Should().BeAt(start, 0.01);
        Assert.True(sim.TimeSeconds - DeploymentCommand(sim).Time < SpecSquad.EdgeDeploymentSeconds, "deployed before the time limit");
        sim.ShouldAllBeInSlots("r1", toleranceMeters: Tolerance);
    }
}
