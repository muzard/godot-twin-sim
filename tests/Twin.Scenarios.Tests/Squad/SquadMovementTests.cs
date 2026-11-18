using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Squad movement (backlog 0004, S2a: SM1, SM2, SM8, SM9). Only the squad leader is ordered; the members follow it in
/// their slots. The leader moves at 0.8 × its speed, halts while an able member is more than 20 m from its slot, and
/// moves on when all are within 5 m, so the squad first forms up and then moves off. Nobody is left behind: a pinned or
/// out-of-action member halts the squad, a wounded one is waited for. A member ordered directly is detached until it is
/// ordered <see cref="Rejoin"/>.
/// </summary>
public class SquadMovementTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly Vec2 East = new(1, 0);

    // Open flat ground, 500 × 200 m. The squad stands in a row on the corridor's centerline (y = 101, a row of cell
    // centers), the leader rj at its west end (x = 80), the others 3 m apart eastward. The corridor runs east to
    // B = (420, 101); 80 m wide, so even the wedge (21.2 m left and 35.4 m right of rj) fits without compression.
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    private static Corridor Straight(double width = 80) =>
        new([Start, B], width, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);

    private static ScenarioRun SquadOnOpenGround(
        SquadFormation formation = SquadFormation.File, Func<TestMap, TestMap>? terrain = null,
        Func<RasterWorld, RasterWorld>? world = null, Func<Scenario, Scenario>? more = null)
    {
        var map = (terrain ?? (m => m))(TestMap.Create(widthMeters: 500, heightMeters: 200));
        var scenario = Scenario.ForSquad()
            .WithTerrain((world ?? (w => w))(map.Build()))
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start);
        return (more ?? (s => s))(scenario).Build();
    }

    private static FormationSlot SlotOf(SquadFormation formation, string unitId) =>
        FormationSlots.For(formation, SpecSquad.Reference().Definition, SpecSquad.IntervalMeters).Single(s => s.UnitId == unitId);

    private static double DistanceToSlot(ScenarioRun sim, string unitId) =>
        sim.Unit(unitId).Position.Xy.DistanceTo(sim.Simulation.SlotOf(unitId) ?? throw new InvalidOperationException($"{unitId} has no slot"));

    private static IEnumerable<string> MembersOtherThanTheLeader => SpecSquad.Reference().Members.Select(m => m.UnitId).Where(id => id != "rj");

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void SM1_ordering_only_the_leader_moves_the_whole_squad_to_the_destination(SquadFormation formation)
    {
        var sim = SquadOnOpenGround(formation);

        sim.Order("rj", new MoveThroughCorridor(Straight())).RunUntilSquadSettled("r1", TenMinutes);

        sim.Unit("rj").Should().BeAt(B).BeIdle().HaveNoOrder();
        foreach (var id in MembersOtherThanTheLeader)
        {
            var expected = SquadMovementAssertions.OnStraightRoute(B, East, SlotOf(formation, id));
            Assert.True(sim.Simulation.SlotOf(id) is { } slot && slot.DistanceTo(expected) < 0.01,
                $"{id}'s slot should be {expected} around the destination, but is {sim.Simulation.SlotOf(id)}");
            sim.ShouldBeInSlot(id, toleranceMeters: 1);
            sim.Unit(id).Should().BeIdle();
        }
    }

    [Fact]
    public void SM1_a_move_to_order_to_the_leader_moves_the_squad_too()
    {
        var sim = SquadOnOpenGround();

        sim.Order("rj", new MoveTo(B)).RunUntilSquadSettled("r1", TenMinutes);

        sim.Unit("rj").Should().BeAt(B).BeIdle();
        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
    }

    [Fact]
    public void SM1_before_the_leaders_first_movement_order_no_member_has_a_slot_or_moves()
    {
        var sim = SquadOnOpenGround();

        sim.RunFor(TimeSpan.FromSeconds(30));

        var members = SpecSquad.Reference().Members;
        for (var i = 0; i < members.Count; i++)
        {
            Assert.Null(sim.Simulation.SlotOf(members[i].UnitId));
            sim.Unit(members[i].UnitId).Should().BeAt(Start + new Vec2(i * Scenario.SquadSpacingMeters, 0)).BeIdle();
        }
    }

    [Fact]
    public void SM1_the_leader_waits_in_place_while_the_squad_forms_up()
    {
        var sim = SquadOnOpenGround();

        sim.Order("rj", new MoveThroughCorridor(Straight())).Step(2);

        // t1 stands 74 m from its slot 50 m behind the leader.
        Assert.True(DistanceToSlot(sim, "t1") > SpecSquad.StragglerMeters);
        sim.Unit("rj").Should().HaveActivity(UnitActivity.WaitingForSquad).BeAt(Start);
    }

    [Fact]
    public void SM1_the_leader_moves_off_once_every_member_is_near_its_slot()
    {
        var sim = SquadOnOpenGround();

        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Activity == UnitActivity.Moving, FiveMinutes, "rj moving");

        // A member moves at most one step (1.3 m/s × 0.5 s) between the leader's check and this one.
        foreach (var id in MembersOtherThanTheLeader)
        {
            Assert.True(DistanceToSlot(sim, id) <= SpecSquad.RegroupMeters + 1,
                $"{id} is {DistanceToSlot(sim, id):0.##} m from its slot when the leader moves off");
        }
    }

    [Fact]
    public void SM1_once_formed_up_the_leader_moves_at_its_speed_factor()
    {
        var sim = SquadOnOpenGround();

        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Position.X > 150, TenMinutes, "rj past x = 150");

        sim.Unit("rj").Should().HaveGroundSpeed(SpecSquad.LeaderSpeedFactor * SpecV01.Infantry.OpenMps);
    }

    [Fact]
    public void SM2_no_member_leaves_the_corridors_outer_limit_round_a_bend()
    {
        // An L-shaped corridor 30 m wide with a 10 m soft margin (outer limit 25 m): the wedge is compressed to fit, and
        // round the bend slots that would fall outside are moved in.
        IReadOnlyList<Vec2> centerline = [new(41, 41), new(261, 41), new(261, 261)];
        var corridor = new Corridor(centerline, Width: 30, SoftMargin: 10, SpecV01.SoftPenalty, HardBoundaries: []);
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 300).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, centerline[0])
            .Build();

        sim.Order("rj", new MoveThroughCorridor(corridor)).RunUntilSquadSettled("r1", TenMinutes);

        foreach (var member in SpecSquad.Reference().Members)
        {
            var farthest = sim.Trail(member.UnitId).Max(p => TestGeometry.DistanceToPolyline(p, centerline));
            Assert.True(farthest <= corridor.OuterLimit + 1e-6,
                $"{member.UnitId} went {farthest:0.##} m from the centerline (outer limit {corridor.OuterLimit} m)");
        }
    }

    [Fact]
    public void SM2_no_member_crosses_a_cliff_on_its_way()
    {
        // A cliff line slants across the right arm's tracks (j2, pj, k1 and k2 walk 14–35 m right of the route): they must
        // go round its north end, which stops 13 m short of the route.
        IReadOnlyList<Vec2> cliff = [new(200, 88), new(215, 60)];
        var sim = SquadOnOpenGround(SquadFormation.Wedge, world: w => w.WithCliffs([cliff]));

        sim.Order("rj", new MoveThroughCorridor(Straight())).RunUntilSquadSettled("r1", TenMinutes);

        foreach (var member in SpecSquad.Reference().Members)
        {
            Assert.Empty(TestGeometry.Crossings(sim.Trail(member.UnitId), cliff));
        }

        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
    }

    [Fact]
    public void SM8_the_leader_waits_for_a_member_slowed_by_a_bog()
    {
        // A bog (0.6 m/s) under the wedge's right arm for 60 m, while the leader walks open ground at 0.8 × 1.3 m/s.
        var sim = SquadOnOpenGround(SquadFormation.Wedge, terrain: m => m.Fill(TerrainClass.Bog, 200, 60, 260, 92));
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Activity == UnitActivity.Moving, FiveMinutes, "rj moving off");

        sim.RunUntil(s => s.Unit("rj").Activity == UnitActivity.WaitingForSquad, FiveMinutes, "rj waiting for the squad");

        var straggler = MembersOtherThanTheLeader.MaxBy(id => DistanceToSlot(sim, id))!;
        Assert.True(DistanceToSlot(sim, straggler) > SpecSquad.StragglerMeters);
        Assert.Equal(TerrainClass.Bog, ((IWorldQuery)sim.Terrain).ClassAt(sim.Unit(straggler).Position.X, sim.Unit(straggler).Position.Y));
        sim.Unit("rj").Should().HaveGroundSpeed(0);
    }

    [Fact]
    public void SM8_the_leader_moves_on_when_the_straggler_is_back()
    {
        var sim = SquadOnOpenGround(SquadFormation.Wedge, terrain: m => m.Fill(TerrainClass.Bog, 200, 60, 260, 92));
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Activity == UnitActivity.Moving, FiveMinutes, "rj moving off")
            .RunUntil(s => s.Unit("rj").Activity == UnitActivity.WaitingForSquad, FiveMinutes, "rj waiting for the squad");

        sim.RunUntil(s => s.Unit("rj").Activity == UnitActivity.Moving, FiveMinutes, "rj moving on");

        foreach (var id in MembersOtherThanTheLeader)
        {
            Assert.True(DistanceToSlot(sim, id) <= SpecSquad.RegroupMeters + 1, $"{id} is not back near its slot");
        }

        sim.RunUntilSquadSettled("r1", TenMinutes);
        sim.Unit("rj").Should().BeAt(B);
    }

    // A squad of four: rj and j1 (command team), pj and k2 (point team), all regulars, standing 3 m apart from x = 80
    // (k2 at x = 89). In file: pj 20 m and k2 10 m ahead of rj, j1 10 m behind. red stands 5 m south of k2 and holds
    // fire until ordered.
    private static ScenarioRun SmallSquadWithAnEnemyNextToK2() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
        .WithSquad(new SquadSpec("r2", [SpecSquad.CommandTeam, SpecSquad.PointTeam],
        [
            SpecSquad.Member("rj", SquadRole.SquadLeader, SpecSquad.CommandTeam, SpecSquad.Experienced),
            SpecSquad.Member("j1", SquadRole.Rifleman, SpecSquad.CommandTeam, SpecSquad.Experienced),
            SpecSquad.Member("pj", SquadRole.TeamLeader, SpecSquad.PointTeam, SpecSquad.Experienced),
            SpecSquad.Member("k2", SquadRole.Rifleman, SpecSquad.PointTeam, SpecSquad.Experienced),
        ]), Start)
        .WithFighter("red", new Vec2(89, 96), Side.Red, RulesOfEngagement.HoldFire)
        .Build();

    // SM9: nobody is left behind. A squad does not move on normally under fire: while a member is pinned the whole squad
    // halts (breaking contact, where return fire frees the pinned member to move, comes with S6b). A wounded member is
    // waited for like any straggler. Until casualty care tells the dead (left) from the badly wounded (carried), an
    // out-of-action member halts the squad too (provisional, see backlog 0004).
    private static (ScenarioRun Sim, double HitAt) SmallSquadWithK2Pinned()
    {
        var sim = SmallSquadWithAnEnemyNextToK2();
        var hit = sim.Order("red", new FireAt("k2")).RunUntilHit("k2", FiveMinutes);
        sim.Order("red", new CeaseFire());
        return (sim, hit.Time);
    }

    [Fact]
    public void SM9_a_pinned_member_halts_the_whole_squad()
    {
        var (sim, hitAt) = SmallSquadWithK2Pinned();

        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunFor(TimeSpan.FromSeconds(hitAt + 50 - sim.TimeSeconds)); // 10 s before the 60 s pin ends

        sim.Unit("k2").Should().HaveHealth(Health.Pinned);
        sim.Unit("rj").Should().HaveActivity(UnitActivity.WaitingForSquad).BeAt(Start);
    }

    [Fact]
    public void SM9_a_wounded_member_is_not_left_behind()
    {
        var (sim, _) = SmallSquadWithK2Pinned();

        sim.Order("rj", new MoveThroughCorridor(Straight())).RunUntilSquadSettled("r2", TenMinutes);

        sim.Unit("rj").Should().BeAt(B);
        sim.Unit("k2").Should().HaveHealth(Health.Wounded);
        sim.ShouldBeInSlot("k2", toleranceMeters: 1);
    }

    [Fact]
    public void SM9_an_out_of_action_member_halts_the_squad_until_casualty_care_exists()
    {
        var sim = SmallSquadWithAnEnemyNextToK2();
        sim.RunUntilOutOfAction("k2", "red", FiveMinutes);

        sim.Order("rj", new MoveThroughCorridor(Straight())).RunFor(TimeSpan.FromMinutes(2));

        sim.Unit("rj").Should().HaveActivity(UnitActivity.WaitingForSquad).BeAt(Start);
    }

    // The squad is on its way in file; k1 is ordered 70 m north, off the squad's route. Returns where k1 was sent.
    private static (ScenarioRun Sim, Vec2 SentTo) SquadWithK1SentAway()
    {
        var sim = SquadOnOpenGround();
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Position.X > 150, TenMinutes, "rj past x = 150");
        var sentTo = new Vec2(sim.Unit("k1").Position.X, 171);
        sim.Order("k1", new MoveTo(sentTo)).Step();
        return (sim, sentTo);
    }

    [Fact]
    public void A_member_ordered_directly_leaves_the_squads_movement()
    {
        var (sim, _) = SquadWithK1SentAway();

        Assert.False(sim.Squad("r1").IsFollowing("k1"));
        Assert.Null(sim.Simulation.SlotOf("k1"));
    }

    [Fact]
    public void The_squad_does_not_wait_for_a_detached_member()
    {
        var (sim, sentTo) = SquadWithK1SentAway();

        sim.RunFor(TimeSpan.FromSeconds(60));

        sim.Unit("k1").Should().BeAt(sentTo);
        sim.Unit("rj").Should().HaveActivity(UnitActivity.Moving);
        foreach (var id in MembersOtherThanTheLeader.Where(id => id != "k1"))
        {
            sim.ShouldBeInSlot(id, toleranceMeters: 1); // the rest of the squad goes on with the leader
        }
    }

    [Fact]
    public void Rejoin_brings_a_detached_member_back_and_the_squad_waits_for_it()
    {
        var (sim, _) = SquadWithK1SentAway();
        sim.RunFor(TimeSpan.FromSeconds(60));

        sim.Order("k1", new Rejoin()).Step();

        Assert.True(sim.Squad("r1").IsFollowing("k1"));
        sim.Unit("rj").Should().HaveActivity(UnitActivity.WaitingForSquad);
        sim.RunUntilSquadSettled("r1", TenMinutes);
        sim.ShouldBeInSlot("k1", toleranceMeters: 1);
    }

    [Fact]
    public void Rejoin_to_a_member_that_is_following_changes_nothing()
    {
        var sim = SquadOnOpenGround();

        sim.Order("k1", new Rejoin()).Step();

        Assert.True(sim.Squad("r1").IsFollowing("k1"));
        sim.Unit("k1").Should().BeIdle();
    }

    [Fact]
    public void Rejoin_is_rejected_for_the_leader_and_for_an_agent_outside_a_squad()
    {
        var sim = SmallSquadWithAnEnemyNextToK2();

        Assert.Throws<OrderRejectedException>(() => sim.Order("rj", new Rejoin()));
        Assert.Throws<OrderRejectedException>(() => sim.Order("red", new Rejoin()));
    }

    [Fact]
    public void A_formation_order_to_anyone_but_the_squad_leader_is_rejected()
    {
        var sim = SmallSquadWithAnEnemyNextToK2();

        Assert.Throws<OrderRejectedException>(() => sim.Order("k2", new SetSquadFormation(SquadFormation.Wedge)));
        Assert.Throws<OrderRejectedException>(() => sim.Order("red", new SetSquadFormation(SquadFormation.Wedge)));
    }
}
