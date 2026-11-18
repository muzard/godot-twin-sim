using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Hard boundaries and permits for a squad (backlog 0004, S2b: SM3–SM5, SM7). The RJ asks as in v0.1 and the squad halts
/// in formation; a permit is the squad's, valid within the permit radius plus the formation's half-width of the requested
/// point. No member crosses an unpermitted boundary: a slot across one to the side tucks in to the path's side (a double
/// file), and a member whose way forward crosses one stops and asks the RJ, which asks the platoon leader after the
/// relay time.
/// </summary>
public class SquadBoundaryTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LeaderDelay = TimeSpan.FromSeconds(20);
    private const double PermitRadius = 10;
    private static readonly Vec2 East = new(1, 0);

    // Open flat ground, 500 × 200 m. The squad starts in a row on y = 101 with rj at x = 80; B = (420, 101).
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    private static RasterWorld OpenGround() => TestMap.Create(widthMeters: 500, heightMeters: 200).Build();

    private static Corridor Corridor(Vec2 from, double width, params HardBoundary[] boundaries) =>
        new([from, B], width, SpecV01.SoftMargin, SpecV01.SoftPenalty, boundaries);

    private static HardBoundary Line(string id, Vec2 a, Vec2 b) => new(id, [a, b], BoundarySource.Order);

    private static ScenarioRun Squad(RasterWorld world, SquadFormation formation, LeaderAnswer answer = LeaderAnswer.Grant) =>
        Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .WithLeader(answer, after: LeaderDelay, permitRadiusMeters: PermitRadius)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build();

    private static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId);

    // Audits every member's trail from step `since` on (the corridor order), against the squad's permits.
    private static void NoMemberCrossesUnpermitted(ScenarioRun sim, RasterWorld world, Corridor corridor, int since = 0)
    {
        foreach (var id in Members)
        {
            var violations = RouteAudit.Violations(world, corridor, sim.Trail(id).Skip(since).ToList(), sim.Squad("r1").Permits);
            Assert.True(violations.Count == 0, $"{id}: " + string.Join("; ", violations));
        }
    }

    // A road across the whole map at x = 250: the only way to B crosses it.
    private static readonly HardBoundary RoadAcross = Line("road", new(250, 0), new(250, 200));

    // The squad first walks east in formation to (200, 101) and settles; then the RJ is ordered on to B across the road,
    // so it asks for permission there with its squad formed up behind it.
    private static (ScenarioRun Sim, Corridor Corridor, int Since) FormedUpBeforeTheRoad(SquadFormation formation)
    {
        var sim = Squad(OpenGround(), formation);
        sim.Order("rj", new MoveTo(new Vec2(200, 101))).RunUntilSquadSettled("r1", TenMinutes);
        var corridor = Corridor(new Vec2(200, 101), width: 80, RoadAcross);
        var since = sim.Trail("rj").Count - 1;
        sim.Order("rj", new MoveThroughCorridor(corridor)).Step();
        return (sim, corridor, since);
    }

    // The squad walks east to (200, 101) and settles; then the RJ is ordered north-east to (260, 180) across a road along
    // y = 150 over the whole map, so it asks where it stands, facing east, with its destination off to the north-east.
    private static ScenarioRun FormedUpFacingEastOrderedNorthEast(SquadFormation formation)
    {
        var sim = Squad(OpenGround(), formation);
        sim.Order("rj", new MoveTo(new Vec2(200, 101))).RunUntilSquadSettled("r1", TenMinutes);
        var corridor = new Corridor([new(200, 101), new(260, 180)], 80, SpecV01.SoftMargin, SpecV01.SoftPenalty,
            [Line("road", new(0, 150), new(500, 150))]);
        return sim.Order("rj", new MoveThroughCorridor(corridor)).Step();
    }

    [Fact]
    public void SM3_the_squad_halts_in_formation_while_the_leader_waits_for_permission()
    {
        var sim = FormedUpFacingEastOrderedNorthEast(SquadFormation.Wedge);

        sim.RunFor(TimeSpan.FromSeconds(10)); // the platoon leader answers after 20 s

        sim.Unit("rj").Should().HaveActivity(UnitActivity.WaitingForPermission);
        var leader = sim.Unit("rj").Position.Xy;
        foreach (var id in Members.Where(id => id != "rj"))
        {
            var slot = FormationSlots.For(SquadFormation.Wedge, SpecSquad.Reference().Definition, SpecSquad.IntervalMeters).Single(s => s.UnitId == id);
            Assert.True(sim.Simulation.SlotOf(id)!.Value.DistanceTo(SquadMovementAssertions.OnStraightRoute(leader, East, slot)) < 0.01,
                $"{id}'s slot should face the way the squad came (east), not its destination");
            sim.ShouldBeInSlot(id, toleranceMeters: 1);
            sim.Unit(id).Should().BeIdle();
        }
    }

    [Fact]
    public void SM7_a_drawn_boundary_halts_the_squad_where_the_leader_asks()
    {
        var sim = FormedUpFacingEastOrderedNorthEast(SquadFormation.File);
        var before = Members.ToDictionary(id => id, id => sim.Unit(id).Position.Xy);

        sim.RunFor(TimeSpan.FromSeconds(10));

        foreach (var id in Members)
        {
            Assert.True(sim.Unit(id).Position.Xy.DistanceTo(before[id]) < 0.01, $"{id} moved up while the RJ waited");
        }
    }

    [Fact]
    public void SM4_after_a_grant_the_squad_crosses_in_formation_within_the_squads_permit()
    {
        var (sim, corridor, since) = FormedUpBeforeTheRoad(SquadFormation.Wedge);

        sim.RunUntilSquadSettled("r1", TenMinutes);

        sim.Unit("rj").Should().BeAt(B);
        var permit = Assert.Single(sim.Squad("r1").Permits);
        Assert.Equal(PermitRadius + (5 * SpecSquad.WedgeStepMeters), permit.Radius, precision: 6); // radius + half-width (k2, right of rj)
        NoMemberCrossesUnpermitted(sim, (RasterWorld)sim.Terrain, corridor, since);

        // In formation: k2, at the right end of the wedge, crosses the road 35.4 m right of the RJ's crossing.
        var k2Crossing = TestGeometry.Crossings(sim.Trail("k2"), RoadAcross.Line).Single();
        Assert.InRange(k2Crossing.Y, 101 - (5 * SpecSquad.WedgeStepMeters) - 1, 101 - (5 * SpecSquad.WedgeStepMeters) + 1);
    }

    [Fact]
    public void SM4_after_a_denial_the_squad_takes_the_respecting_route_and_nobody_crosses()
    {
        // After forming up at (200, 101) the RJ is ordered 100 m on to (300, 101). A road across the way ends at y = 160,
        // inside the 80 m wide corridor (outer limit 65 m): going round its north end costs more than 1.5 × the straight
        // way, so the RJ asks; after the denial the squad goes round, in file.
        var road = Line("road", new(250, 0), new(250, 160));
        var destination = new Vec2(300, 101);
        var corridor = new Corridor([new(200, 101), destination], 80, SpecV01.SoftMargin, SpecV01.SoftPenalty, [road]);
        var world = OpenGround();
        var sim = Squad(world, SquadFormation.File, LeaderAnswer.Deny);
        sim.Order("rj", new MoveTo(new Vec2(200, 101))).RunUntilSquadSettled("r1", TenMinutes);
        var since = sim.Trail("rj").Count - 1;

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntilSquadSettled("r1", TenMinutes);

        Assert.Contains(sim.Events.OfType<PermissionAnswered>(), e => e.AgentId == "rj" && e.Answer == LeaderAnswer.Deny);
        sim.Unit("rj").Should().BeAt(destination);
        Assert.Empty(sim.Squad("r1").Permits);
        NoMemberCrossesUnpermitted(sim, world, corridor, since);
    }

    [Fact]
    public void SM4_after_a_denial_with_no_way_round_the_squad_halts_as_one()
    {
        var corridor = Corridor(Start, width: 60, RoadAcross);
        var sim = Squad(OpenGround(), SquadFormation.File, LeaderAnswer.Deny);

        sim.Order("rj", new MoveThroughCorridor(corridor))
            .RunUntil(s => s.Events.OfType<Blocked>().Any(e => e.AgentId == "rj"), TenMinutes, "rj blocked");
        sim.RunFor(TimeSpan.FromSeconds(30));

        foreach (var id in Members)
        {
            Assert.True(sim.Unit(id).Position.X < 250, $"{id} crossed the road");
            Assert.Equal(0, sim.Unit(id).Velocity.Length, precision: 9);
        }
    }

    [Fact]
    public void SM5_slots_across_a_boundary_to_the_side_tuck_in_to_a_double_file()
    {
        // A road runs along the route 8 m to its right (y = 93) across the whole map: the wedge's right arm beyond its apex
        // j1 (7.1 m out) — j2, pj, k1 and k2, 14–35 m out — would stand across it.
        var road = Line("road", new(0, 93), new(500, 93));
        var corridor = Corridor(Start, width: 80, road);
        var world = OpenGround();
        var sim = Squad(world, SquadFormation.Wedge);

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntil(s => s.Unit("rj").Position.X > 250, TenMinutes, "rj past x = 250");

        foreach (var id in new[] { "j2", "pj", "k1", "k2" })
        {
            var slot = sim.Simulation.SlotOf(id)!.Value;
            Assert.InRange(slot.Y, 93, 94); // on the path's side of the road, at most 1 m from it
            sim.ShouldBeInSlot(id, toleranceMeters: 1);
        }

        sim.RunUntilSquadSettled("r1", TenMinutes);
        NoMemberCrossesUnpermitted(sim, world, corridor);
    }

    // A road reaches up from the south edge to y = 95, 6 m short of the route, at x = 250. The RJ walks past its end;
    // the wedge's right arm (its apex j1, 7.1 m out and 7.1 m ahead of rj, then j2, pj, k1 and k2) must cross it.
    private static readonly HardBoundary RoadEndingShort = Line("road", new(250, 0), new(250, 95));

    [Fact]
    public void SM5_a_member_whose_way_forward_crosses_a_boundary_asks_the_leader_who_asks_the_platoon_leader()
    {
        var sim = Squad(OpenGround(), SquadFormation.Wedge);

        sim.Order("rj", new MoveThroughCorridor(Corridor(Start, width: 80, RoadEndingShort)))
            .RunUntil(s => s.Events.OfType<PermissionRequested>().Any(e => e.AgentId == "rj"), TenMinutes, "rj asks");

        var requests = sim.Events.OfType<PermissionRequested>().ToList();
        var member = requests.First();
        Assert.Equal("j1", member.AgentId);                    // the apex, ahead of rj, reaches the road first
        Assert.Equal("road", member.BoundaryId);
        var leader = requests.Single(e => e.AgentId == "rj");
        Assert.Equal(("road", member.At), (leader.BoundaryId, leader.At));
        Assert.InRange(leader.Time, member.Time + SpecSquad.RelaySeconds - 1e-9, member.Time + SpecSquad.RelaySeconds + SpecV01.DtSeconds + 1e-9);
        sim.Unit("j1").Should().HaveActivity(UnitActivity.WaitingForPermission);
        Assert.True(sim.Unit("j1").Position.X < 250, "j1 waits before the road");
    }

    [Fact]
    public void SM5_the_squad_halts_while_a_members_request_is_answered_and_crosses_after_the_grant()
    {
        var world = OpenGround();
        var corridor = Corridor(Start, width: 80, RoadEndingShort);
        var sim = Squad(world, SquadFormation.Wedge);
        sim.Order("rj", new MoveThroughCorridor(corridor))
            .RunUntil(s => s.Events.OfType<PermissionRequested>().Any(e => e.AgentId == "rj"), TenMinutes, "rj asks");

        sim.RunFor(TimeSpan.FromSeconds(10));
        sim.Unit("rj").Should().HaveGroundSpeed(0);

        sim.RunUntilSquadSettled("r1", TenMinutes);
        sim.Unit("rj").Should().BeAt(B);
        Assert.Single(sim.Squad("r1").Permits);
        NoMemberCrossesUnpermitted(sim, world, corridor);
    }
}
