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
/// Deploying at the forest edge (backlog 0004, S2c: SM6). When the RJ needs a permit to leave the forest, it does not ask
/// where it stands: it walks on along its reference route to a point 5 m inside the edge and commands the deployment.
/// The squad takes an open line along the edge, the wedge straightened (left to right t1, vj, kk, rj, j1, j2, pj, k1,
/// k2), one interval apart with the RJ at its point, every member in the forest 5 m inside the edge; members that do not
/// fit the corridor form a second line 10 m deeper. The RJ asks when the line has formed (<see cref="EdgeDeployed"/>), or at the latest 60 s
/// after its command. The permit covers the line.
/// </summary>
public class SquadEdgeDeploymentTests
{
    private static readonly TimeSpan TwentyMinutes = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan LeaderDelay = TimeSpan.FromSeconds(20);
    private const double PermitRadius = 10;
    private const double Tolerance = 1;

    // Forest west of x = 250, open land east of it; 500 × 200 m. The squad starts in the forest in a row on y = 101 with
    // rj at x = 80; B = (420, 101) lies in the open, so every way to B leaves the forest and the RJ must ask.
    private const double EdgeX = 250;
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    // The open line, left to right when facing out of the forest (east): left is north.
    private static readonly string[] LineOrder = ["t1", "vj", "kk", "rj", "j1", "j2", "pj", "k1", "k2"];
    private const int LeaderInLine = 3;

    private static RasterWorld ForestWestOfTheEdge() =>
        TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, EdgeX, 0, 500, 200).Build();

    private static Corridor Corridor(double width = 60) =>
        new([Start, B], width, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);

    private static ScenarioRun Squad(
        RasterWorld world, Corridor corridor, LeaderAnswer answer = LeaderAnswer.Grant, SquadDoctrine? doctrine = null)
    {
        var scenario = Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, Start)
            .WithLeader(answer, after: LeaderDelay, permitRadiusMeters: PermitRadius)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60));
        if (doctrine is not null)
        {
            scenario.WithSquadDoctrine(doctrine);
        }

        return scenario.Build().Order("rj", new MoveThroughCorridor(corridor));
    }

    private static ScenarioRun RunUntilDeployed(ScenarioRun sim) =>
        sim.RunUntil(s => s.Events.OfType<EdgeDeployed>().Any(), TwentyMinutes, "squad deployed at the forest edge");

    private static ScenarioRun RunUntilTheLeaderAsks(ScenarioRun sim) =>
        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(e => e.AgentId == "rj"), TwentyMinutes, "rj asks");

    private static CommandIssued DeploymentCommand(ScenarioRun sim) =>
        sim.Events.OfType<CommandIssued>().Single(e => e.AgentId == "rj" &&
            e.Command.StartsWith(SpecSquad.DeployAtForestEdgeCommand, StringComparison.Ordinal));

    private static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId);

    private static void InTheForest(ScenarioRun sim, RasterWorld world, string id)
    {
        var p = sim.Unit(id).Position.Xy;
        Assert.True(world.ClassAt(p.X, p.Y) == TerrainClass.Forest, $"{id} at {p} should stand in the forest");
    }

    [Fact]
    public void SM6_the_squad_deploys_in_an_open_line_5_m_inside_the_forest_edge()
    {
        var world = ForestWestOfTheEdge();
        var sim = Squad(world, Corridor());

        RunUntilDeployed(sim);

        for (var i = 0; i < LineOrder.Length; i++)
        {
            var id = LineOrder[i];
            var p = sim.Unit(id).Position.Xy;
            Assert.InRange(p.X, EdgeX - SpecSquad.EdgeInsetMeters - Tolerance, EdgeX - SpecSquad.EdgeInsetMeters + Tolerance);
            var y = Start.Y + ((LeaderInLine - i) * SpecSquad.IntervalMeters);
            Assert.True(Math.Abs(p.Y - y) <= Tolerance, $"{id} at {p} should stand at y = {y} (± {Tolerance} m) in the line");
            InTheForest(sim, world, id);
        }

        sim.ShouldAllBeInSlots("r1", toleranceMeters: Tolerance);
    }

    [Fact]
    public void SM6_the_leader_asks_to_leave_the_forest_only_once_the_squad_has_deployed()
    {
        var sim = Squad(ForestWestOfTheEdge(), Corridor());

        RunUntilTheLeaderAsks(sim);

        var request = sim.Events.OfType<PermissionRequested>().Single(e => e.AgentId == "rj");
        Assert.Equal(HardBoundary.ForestEdgeId, request.BoundaryId);
        var deployed = Assert.Single(sim.Events.OfType<EdgeDeployed>());
        Assert.Equal(("rj", "r1"), (deployed.AgentId, deployed.SquadId));
        Assert.InRange(request.Time, deployed.Time - 1e-9, deployed.Time + SpecV01.DtSeconds + 1e-9);
        Assert.True(DeploymentCommand(sim).Time < deployed.Time, "the RJ commands the deployment before the line has formed");
        Assert.InRange(sim.Unit("rj").Position.X, EdgeX - SpecSquad.EdgeInsetMeters - Tolerance, EdgeX - SpecSquad.EdgeInsetMeters + Tolerance);
    }

    [Fact]
    public void SM6_members_take_their_line_spots_when_the_deployment_command_reaches_them()
    {
        var sim = Squad(ForestWestOfTheEdge(), Corridor());
        sim.RunUntil(s => s.Events.OfType<CommandIssued>().Any(e =>
            e.Command.StartsWith(SpecSquad.DeployAtForestEdgeCommand, StringComparison.Ordinal)), TwentyMinutes, "deployment commanded");
        var command = DeploymentCommand(sim);

        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.ShouldHaveActedOn("k2", command, SpecSquad.ExperiencedReactionSeconds);
        sim.ShouldHaveActedOn("k1", command,
            SpecSquad.InexperiencedReactionSeconds + SpecSquad.RelaySeconds + SpecSquad.InexperiencedReactionSeconds);
    }

    [Fact]
    public void SM6_the_leader_asks_at_the_deployment_time_limit_when_the_line_has_not_formed()
    {
        // A 3 s limit: k1 does not even hear the command until 8 s after it.
        const double limit = 3;
        var sim = Squad(ForestWestOfTheEdge(), Corridor(), doctrine: SpecSquad.Doctrine with { EdgeDeploymentSeconds = limit });

        RunUntilTheLeaderAsks(sim);

        var request = sim.Events.OfType<PermissionRequested>().Single(e => e.AgentId == "rj");
        var command = DeploymentCommand(sim);
        Assert.InRange(request.Time, command.Time + limit - 1e-9, command.Time + limit + SpecV01.DtSeconds + 1e-9);
        Assert.Empty(sim.Events.OfType<EdgeDeployed>());
    }

    [Fact]
    public void SM6_after_a_grant_the_squad_leaves_the_forest_within_the_permit_and_arrives_in_formation()
    {
        var world = ForestWestOfTheEdge();
        var corridor = Corridor();
        var sim = Squad(world, corridor);

        sim.RunUntilSquadSettled("r1", TwentyMinutes);

        sim.Unit("rj").Should().BeAt(B);
        var permit = Assert.Single(sim.Squad("r1").Permits);
        Assert.Equal(HardBoundary.ForestEdgeId, permit.BoundaryId);
        // radius + the line's half-width: its farthest spot from the RJ, k2, five intervals right
        Assert.Equal(PermitRadius + ((LineOrder.Length - 1 - LeaderInLine) * SpecSquad.IntervalMeters), permit.Radius, precision: 6);
        foreach (var id in Members)
        {
            var violations = RouteAudit.Violations(world, corridor, sim.Trail(id), sim.Squad("r1").Permits);
            Assert.True(violations.Count == 0, $"{id}: " + string.Join("; ", violations));
        }

        sim.ShouldAllBeInSlots("r1", toleranceMeters: Tolerance);
    }

    [Fact]
    public void SM6_after_a_denial_with_no_way_round_the_squad_stays_in_the_forest_and_halts_as_one()
    {
        var sim = Squad(ForestWestOfTheEdge(), Corridor(), LeaderAnswer.Deny);

        sim.RunUntil(s => s.Events.OfType<Blocked>().Any(e => e.AgentId == "rj"), TwentyMinutes, "rj blocked");
        sim.RunFor(TimeSpan.FromSeconds(30));

        foreach (var id in Members)
        {
            Assert.True(sim.Unit(id).Position.X < EdgeX, $"{id} left the forest");
            Assert.Equal(0, sim.Unit(id).Velocity.Length, precision: 9);
        }
    }

    [Fact]
    public void SM6_members_that_do_not_fit_the_corridor_form_a_second_line_10_m_deeper()
    {
        // A 14 m wide corridor: its outer limit is 7 + 25 = 32 m either side of the centerline, so the line fits out to
        // ±30 m (t1 to pj); k1 and k2, 40 and 50 m right, form a second line centred on the RJ, one interval apart.
        var world = ForestWestOfTheEdge();
        var sim = Squad(world, Corridor(width: 14));

        RunUntilDeployed(sim);

        var firstLineX = EdgeX - SpecSquad.EdgeInsetMeters;
        for (var i = 0; i < LineOrder.Length - 2; i++)
        {
            var id = LineOrder[i];
            sim.Unit(id).Should().BeAt(new Vec2(firstLineX, Start.Y + ((LeaderInLine - i) * SpecSquad.IntervalMeters)), Tolerance);
        }

        var secondLineX = firstLineX - SpecSquad.SecondLineDepthMeters;
        sim.Unit("k1").Should().BeAt(new Vec2(secondLineX, Start.Y + (SpecSquad.IntervalMeters / 2)), Tolerance);
        sim.Unit("k2").Should().BeAt(new Vec2(secondLineX, Start.Y - (SpecSquad.IntervalMeters / 2)), Tolerance);
        foreach (var id in Members)
        {
            InTheForest(sim, world, id);
        }
    }

    // A forest edge at 30° from north–south through (250, 101): open land east of x = 250 + (y − 101)·tan 30°. Facing out
    // of the forest is east-south-east, and left along the edge is north-north-east.
    private static readonly double Tan30 = Math.Tan(Math.PI / 6);
    private static readonly Vec2 Outward = new(Math.Cos(Math.PI / 6), -Math.Sin(Math.PI / 6));
    private static readonly Vec2 AlongTheEdgeLeft = new(Math.Sin(Math.PI / 6), Math.Cos(Math.PI / 6));

    private static double SlantedEdgeX(double y) => EdgeX + ((y - Start.Y) * Tan30);

    // Distance from a point inside the forest to the slanted edge.
    private static double InsideSlantedEdge(Vec2 p) => (SlantedEdgeX(p.Y) - p.X) * Math.Cos(Math.PI / 6);

    [Fact]
    public void SM6_the_line_follows_a_slanted_forest_edge()
    {
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .FillWhere(TerrainClass.Open, p => p.X > SlantedEdgeX(p.Y)).Build();
        var sim = Squad(world, Corridor());

        RunUntilDeployed(sim);

        // The edge follows 2 m cells, a staircase, so allow 1.5 m against the straight line it approximates.
        const double stairs = 1.5;
        var leader = sim.Unit("rj").Position.Xy;
        Assert.InRange(leader.Y, Start.Y - Tolerance, Start.Y + Tolerance); // on its route
        for (var i = 0; i < LineOrder.Length; i++)
        {
            var id = LineOrder[i];
            var p = sim.Unit(id).Position.Xy;
            Assert.InRange(InsideSlantedEdge(p), SpecSquad.EdgeInsetMeters - stairs, SpecSquad.EdgeInsetMeters + stairs);
            var along = (p - leader).Dot(AlongTheEdgeLeft);
            var expected = (LeaderInLine - i) * SpecSquad.IntervalMeters;
            Assert.True(Math.Abs(along - expected) <= stairs, $"{id} stands {along:0.##} m along the edge from rj, expected {expected}");
            Assert.True(Math.Abs((p - leader).Dot(Outward)) <= stairs, $"{id} at {p} is not in line with rj along the edge");
            InTheForest(sim, world, id);
        }
    }

    [Fact]
    public void SM6_a_deployment_survives_a_round_trip_through_the_log_and_repeats_byte_for_byte()
    {
        string Run() => EventLog.ToJsonLines(Squad(ForestWestOfTheEdge(), Corridor()).RunUntilSquadSettled("r1", TwentyMinutes).Events);

        var first = Run();

        Assert.Contains(nameof(EdgeDeployed), first, StringComparison.Ordinal);
        Assert.Equal(first, Run());
        Assert.Equal(first, EventLog.ToJsonLines(EventLog.FromJsonLines(first)));
    }
}
