using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Corridors;

/// <summary>
/// Hard boundaries and permission requests (spec v0.1, "Lupapyyntö"). The agent plans a route that respects hard
/// boundaries and a reference route that may cross them. It asks its (scripted) leader for permission when the
/// respecting route does not exist or costs more than the threshold (1.5) times the reference, and waits in place.
/// A permit opens the named boundary within a radius of the requested crossing point. After a denial or a timeout it
/// takes the respecting route if there is one, and otherwise stops and reports that it is blocked. C4–C7.
/// </summary>
public class PermissionTests
{
    // Open 100 m x 100 m map. The agent goes 40 m north from A to B; a "road" it must not cross runs west-east
    // at y = 50 from the west edge to RoadEnd. Going round the road's east end is the detour.
    private static readonly Vec2 A = new(51, 31);
    private static readonly Vec2 B = new(51, 71);
    private static readonly Vec2 StraightCrossing = new(51, 50);
    private const double ShortDetourRoadEnd = 59;   // detour ≈ 1.17 x the straight route: below the threshold
    private const double LongDetourRoadEnd = 95;    // detour ≈ 2.7 x: above the threshold
    private const double NoDetourRoadEnd = 100;     // road spans the whole map: no detour
    private const double PermitRadius = 10;
    private static readonly TimeSpan LeaderDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PermissionTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(10);

    private static HardBoundary Road(double end) => new("road", [new(0, 50), new(end, 50)], BoundarySource.Order);

    private static Corridor CorridorWith(HardBoundary boundary) =>
        new([A, B], Width: 60, SoftMargin: SpecV01.SoftMargin, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: [boundary]);

    private static ScenarioRun Ordered(RasterWorld world, Corridor corridor, LeaderAnswer answer, Vec2? start = null)
    {
        var sim = Scenario.Create()
            .WithTerrain(world)
            .WithTickRate(SpecV01.TickRateHz)
            .WithUnit("alpha", at: start ?? A, profile: SpecV01.Infantry)
            .WithLeader(answer, after: LeaderDelay, permitRadiusMeters: PermitRadius)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: PermissionTimeout)
            .Build();
        return sim.Order("alpha", new MoveThroughCorridor(corridor));
    }

    private static RasterWorld OpenWorld() => TestMap.Create(widthMeters: 100, heightMeters: 100).Build();

    private static void AssertRouteValid(ScenarioRun sim, RasterWorld world, Corridor corridor)
    {
        var violations = RouteAudit.Violations(world, corridor, sim.Trail("alpha"), RouteAudit.GrantedPermits(sim.Events, PermitRadius));
        Assert.True(violations.Count == 0, "Route violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void C4_without_a_permit_the_agent_takes_a_detour_within_the_threshold_without_asking()
    {
        var world = OpenWorld();
        var corridor = CorridorWith(Road(ShortDetourRoadEnd));
        var sim = Ordered(world, corridor, LeaderAnswer.Grant);

        sim.RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        sim.Unit("alpha").Should().BeAt(B);
        Assert.Empty(sim.Events.OfType<PermissionRequested>());
        AssertRouteValid(sim, world, corridor);
    }

    [Fact]
    public void C4_the_forest_edge_is_a_hard_boundary_so_the_agent_stays_under_cover_when_the_detour_is_within_the_threshold()
    {
        // Decision 2026-11-08: leaving forest always crosses a hard boundary. A clearing lies across the straight way.
        var world = TestMap.Create(widthMeters: 60, heightMeters: 60, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, fromX: 20, fromY: 26, toX: 40, toY: 34)
            .Build();
        var start = new Vec2(31, 11);
        var end = new Vec2(31, 51);
        var corridor = new Corridor([start, end], Width: 40, SoftMargin: 10, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: []);
        var sim = Ordered(world, corridor, LeaderAnswer.Grant, start);

        sim.RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        sim.Unit("alpha").Should().BeAt(end);
        Assert.Empty(sim.Events.OfType<PermissionRequested>());
        Assert.All(sim.Trail("alpha"), p => Assert.Equal(TerrainClass.Forest, world.ClassAt(p.X, p.Y)));
    }

    [Fact]
    public void C5_when_the_detour_exceeds_the_threshold_the_agent_requests_permission_naming_the_boundary_and_crossing_point()
    {
        var sim = Ordered(OpenWorld(), CorridorWith(Road(LongDetourRoadEnd)), LeaderAnswer.Grant);

        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(), TimeSpan.FromSeconds(5), "permission requested");

        var request = sim.Events.OfType<PermissionRequested>().Single();
        Assert.Equal("alpha", request.AgentId);
        Assert.Equal("road", request.BoundaryId);
        Assert.True(request.At.DistanceTo(StraightCrossing) <= SpecV01.CellSize,
            $"the crossing point should be on the reference route, near {StraightCrossing}, but is {request.At}");
    }

    [Fact]
    public void C5_while_waiting_for_an_answer_the_agent_stays_in_place()
    {
        var sim = Ordered(OpenWorld(), CorridorWith(Road(LongDetourRoadEnd)), LeaderAnswer.Grant);
        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(), TimeSpan.FromSeconds(5), "permission requested");
        var waitingAt = sim.Unit("alpha").Position;

        sim.RunFor(LeaderDelay / 2);

        Assert.Equal(UnitActivity.WaitingForPermission, sim.Unit("alpha").Activity);
        Assert.Equal(waitingAt, sim.Unit("alpha").Position);
        Assert.Equal(Vec3.Zero, sim.Unit("alpha").Velocity);
    }

    [Fact]
    public void C6_after_a_permit_the_agent_crosses_within_the_permit_radius_and_arrives()
    {
        var world = OpenWorld();
        var corridor = CorridorWith(Road(NoDetourRoadEnd));
        var sim = Ordered(world, corridor, LeaderAnswer.Grant);

        sim.RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        sim.Unit("alpha").Should().BeAt(B);
        Assert.Contains(sim.Events.OfType<PermissionAnswered>(), a => a.Answer == LeaderAnswer.Grant);
        Assert.NotEmpty(TestGeometry.Crossings(sim.Trail("alpha"), Road(NoDetourRoadEnd).Line));
        AssertRouteValid(sim, world, corridor);
    }

    [Fact]
    public void C7_after_a_denial_the_agent_takes_the_detour_if_there_is_one()
    {
        var world = OpenWorld();
        var corridor = CorridorWith(Road(LongDetourRoadEnd));
        var sim = Ordered(world, corridor, LeaderAnswer.Deny);

        sim.RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        sim.Unit("alpha").Should().BeAt(B);
        Assert.Contains(sim.Events.OfType<PermissionAnswered>(), a => a.Answer == LeaderAnswer.Deny);
        AssertRouteValid(sim, world, corridor);
    }

    [Fact]
    public void C7_after_a_denial_without_a_detour_the_agent_stops_and_reports_it_is_blocked()
    {
        var sim = Ordered(OpenWorld(), CorridorWith(Road(NoDetourRoadEnd)), LeaderAnswer.Deny);

        sim.RunUntil(s => s.Events.OfType<Blocked>().Any(), LeaderDelay + TimeSpan.FromSeconds(5), "alpha blocked");
        var blockedAt = sim.Unit("alpha").Position;
        sim.RunFor(TimeSpan.FromSeconds(30));

        Assert.Equal("alpha", sim.Events.OfType<Blocked>().Single().AgentId);
        Assert.Equal(UnitActivity.Blocked, sim.Unit("alpha").Activity);
        Assert.Equal(blockedAt, sim.Unit("alpha").Position);
        Assert.Empty(TestGeometry.Crossings(sim.Trail("alpha"), Road(NoDetourRoadEnd).Line));
    }

    [Fact]
    public void C7_when_the_leader_does_not_answer_the_agent_takes_the_detour_after_the_timeout()
    {
        var world = OpenWorld();
        var corridor = CorridorWith(Road(LongDetourRoadEnd));
        var sim = Ordered(world, corridor, LeaderAnswer.NoAnswer);
        sim.RunUntil(s => s.Events.OfType<PermissionRequested>().Any(), TimeSpan.FromSeconds(5), "permission requested");
        var requestedAt = sim.TimeSeconds;

        sim.RunUntil(s => s.Unit("alpha").IsIdle, Generous, "alpha idle");

        var timeout = Assert.Single(sim.Events.OfType<PermissionTimedOut>());
        Assert.True(timeout.Time >= requestedAt + PermissionTimeout.TotalSeconds - 1e-9, "the agent should wait for the whole timeout");
        Assert.Empty(sim.Events.OfType<PermissionAnswered>());
        sim.Unit("alpha").Should().BeAt(B);
        AssertRouteValid(sim, world, corridor);
    }

    [Fact]
    public void C7_when_the_leader_does_not_answer_and_there_is_no_detour_the_agent_reports_it_is_blocked()
    {
        var sim = Ordered(OpenWorld(), CorridorWith(Road(NoDetourRoadEnd)), LeaderAnswer.NoAnswer);

        sim.RunUntil(s => s.Events.OfType<Blocked>().Any(), PermissionTimeout + TimeSpan.FromSeconds(5), "alpha blocked");

        Assert.Single(sim.Events.OfType<PermissionTimedOut>());
        Assert.Equal(UnitActivity.Blocked, sim.Unit("alpha").Activity);
        sim.Unit("alpha").Should().BeAt(A);
    }
}
