using Twin.Sim.Combat;
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
/// Edge cases of squad permits found in review (backlog 0004, S2b): a permit lasts one order (and its successor), not
/// the next order; no member leaves the forest without a permit; after a denial a member goes round the boundary if it
/// can; a successor carries the squad's permits.
/// </summary>
public class SquadPermitEdgeCaseTests
{
    private static readonly TimeSpan TwentyMinutes = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan LeaderDelay = TimeSpan.FromSeconds(20);
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);
    private static readonly HardBoundary RoadAcross = new("road", [new(250, 0), new(250, 200)], BoundarySource.Order);

    private static Corridor Corridor(Vec2 from, Vec2 to, params HardBoundary[] boundaries) =>
        new([from, to], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, boundaries);

    private static Scenario Squad(RasterWorld world, SquadFormation formation, LeaderAnswer answer = LeaderAnswer.Grant) =>
        Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .WithLeader(answer, after: LeaderDelay, permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60));

    private static void NoMemberCrossesUnpermitted(ScenarioRun sim, RasterWorld world, Corridor corridor)
    {
        foreach (var id in SpecSquad.Reference().Members.Select(m => m.UnitId))
        {
            var violations = RouteAudit.Violations(world, corridor, sim.Trail(id), sim.Squad("r1").Permits);
            Assert.True(violations.Count == 0, $"{id}: " + string.Join("; ", violations));
        }
    }

    [Fact]
    public void A_permit_does_not_carry_over_to_the_leaders_next_order()
    {
        var sim = Squad(TestMap.Create(widthMeters: 500, heightMeters: 200).Build(), SquadFormation.File).Build();
        sim.Order("rj", new MoveThroughCorridor(Corridor(Start, B, RoadAcross))).RunUntilSquadSettled("r1", TwentyMinutes);

        sim.Order("rj", new MoveThroughCorridor(Corridor(B, Start, RoadAcross))).Step();

        Assert.Equal(2, sim.Events.OfType<PermissionRequested>().Count(e => e.AgentId == "rj"));
        Assert.Empty(sim.Squad("r1").Permits);
    }

    [Fact]
    public void No_member_leaves_the_forest_without_a_permit()
    {
        // Forest everywhere but an open notch right of the route (x 240–260, y 60–95): the RJ stays in the forest, while
        // the wedge's right arm would walk out into the notch.
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 240, 60, 260, 95).Build();
        var corridor = Corridor(Start, B);
        var sim = Squad(world, SquadFormation.Wedge).Build();

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntilSquadSettled("r1", TwentyMinutes);

        sim.Unit("rj").Should().BeAt(B);
        NoMemberCrossesUnpermitted(sim, world, corridor);
    }

    [Fact]
    public void After_a_denial_a_member_goes_round_the_boundary_and_the_squad_arrives()
    {
        // The road reaches up from the south edge to y = 95 at x = 250; the right arm must cross it, asks, is refused,
        // and goes round its north end instead.
        var road = new HardBoundary("road", [new(250, 0), new(250, 95)], BoundarySource.Order);
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200).Build();
        var corridor = Corridor(Start, B, road);
        var sim = Squad(world, SquadFormation.Wedge, LeaderAnswer.Deny).Build();

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntilSquadSettled("r1", TwentyMinutes);

        Assert.Contains(sim.Events.OfType<PermissionAnswered>(), e => e.Answer == LeaderAnswer.Deny);
        sim.Unit("rj").Should().BeAt(B);
        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
        NoMemberCrossesUnpermitted(sim, world, corridor);
    }

    [Fact]
    public void A_successor_carries_the_squads_permits_and_does_not_ask_again()
    {
        // The RJ is granted the road at x = 250, then falls at x ≈ 150 to red, 6 m off the route.
        var sim = Squad(TestMap.Create(widthMeters: 500, heightMeters: 200).Build(), SquadFormation.File)
            .WithFighter("red", new Vec2(150, 107), Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        sim.Order("rj", new MoveThroughCorridor(Corridor(Start, B, RoadAcross)))
            .RunUntil(s => s.Unit("rj").Position.X > 145, TwentyMinutes, "rj near red");
        Assert.Single(sim.Squad("r1").Permits);
        sim.RunUntilOutOfAction("rj", "red", TwentyMinutes);

        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", TimeSpan.FromSeconds(30), "vj in command").RunFor(TimeSpan.FromSeconds(10));

        Assert.IsType<MoveThroughCorridor>(sim.Unit("vj").CurrentOrder);
        Assert.DoesNotContain(sim.Events.OfType<PermissionRequested>(), e => e.AgentId == "vj");
        Assert.Single(sim.Squad("r1").Permits);
    }
}
