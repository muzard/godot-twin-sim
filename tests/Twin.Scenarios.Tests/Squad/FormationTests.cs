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
/// Formations on the move (backlog 0004, S2a: FO1–FO5). Slots are <c>(along, across)</c> relative to the squad leader,
/// measured along its path and across it (positive to the left). A file walks the leader's path. A formation wider than
/// the corridor's inner zone is compressed evenly to fit, keeping at least 1 m between neighbouring slots across, or
/// becomes a file. A slot in impassable terrain moves toward the leader's path to the nearest usable point. A formation
/// change reaches each member through the chain of command (S1).
/// </summary>
public class FormationTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly Vec2 East = new(1, 0);
    private static readonly double S = SpecSquad.WedgeStepMeters;

    // Open flat ground, 500 × 200 m; the squad starts in a row on the centerline y = 101 with rj at x = 80, and the
    // corridor runs east to B = (420, 101).
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    private static Corridor Straight(double width = 80) =>
        new([Start, B], width, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);

    private static ScenarioRun Squad(SquadFormation formation, Func<TestMap, TestMap>? terrain = null)
    {
        var map = (terrain ?? (m => m))(TestMap.Create(widthMeters: 500, heightMeters: 200));
        return Scenario.ForSquad()
            .WithTerrain(map.Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .Build();
    }

    private static FormationSlot Nominal(SquadFormation formation, string unitId) =>
        FormationSlots.For(formation, SpecSquad.Reference().Definition, SpecSquad.IntervalMeters).Single(s => s.UnitId == unitId);

    private static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId).Where(id => id != "rj");

    private static Vec2 Slot(ScenarioRun sim, string unitId) =>
        sim.Simulation.SlotOf(unitId) ?? throw new ScenarioAssertionException($"{unitId} has no slot");

    private static ScenarioRun OnTheMove(ScenarioRun sim, double pastX = 200) =>
        sim.Order("rj", new MoveThroughCorridor(Straight()))
            .RunUntil(s => s.Unit("rj").Position.X > pastX, TenMinutes, $"rj past x = {pastX}");

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void FO1_on_a_straight_route_each_slot_is_its_offset_from_the_leader(SquadFormation formation)
    {
        var sim = OnTheMove(Squad(formation));

        var leader = sim.Unit("rj").Position.Xy;
        foreach (var id in Members)
        {
            var expected = SquadMovementAssertions.OnStraightRoute(leader, East, Nominal(formation, id));
            Assert.True(Slot(sim, id).DistanceTo(expected) < 0.01, $"{id}'s slot is {Slot(sim, id)}, expected {expected}");
        }
    }

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void FO1_once_formed_up_the_members_keep_their_slots_on_the_move(SquadFormation formation)
    {
        var sim = OnTheMove(Squad(formation));

        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
    }

    [Fact]
    public void FO2_in_file_every_member_walks_the_leaders_path_round_a_bend()
    {
        // An L-shaped corridor: east, then north. rj starts 60 m along it, so the file has room behind it.
        IReadOnlyList<Vec2> centerline = [new(31, 31), new(231, 31), new(231, 261)];
        var corridor = new Corridor(centerline, Width: 40, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []);
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 300, heightMeters: 300).Build())
            .WithSquad(SpecSquad.Reference(), new Vec2(91, 31))
            .Build();
        sim.Order("rj", new MoveThroughCorridor(corridor))
            .RunUntil(s => s.Unit("rj").Activity == UnitActivity.Moving, TenMinutes, "squad formed up");
        var formedUp = sim.Trail("rj").Count - 1;
        sim.RunUntil(s => s.Unit("rj").Position.Xy.DistanceTo(centerline[^1]) < 60, TenMinutes, "rj 60 m from B");
        var compared = sim.Trail("rj").Count - 1;

        sim.RunUntilSquadSettled("r1", TenMinutes);

        // Members ahead of rj walk its route before it does, so compare with its whole track to B; members behind it
        // start on the path extended straight back from its start (the route's first leg runs east).
        var leaderTrack = sim.Trail("rj").Prepend(new Vec2(91 - 60, 31)).ToList();
        foreach (var id in Members)
        {
            var track = sim.Trail(id).Skip(formedUp).Take(compared - formedUp + 1);
            var farthest = track.Max(p => TestGeometry.DistanceToPolyline(p, leaderTrack));
            Assert.True(farthest <= 1, $"{id} strayed {farthest:0.##} m from the leader's path");
        }
    }

    [Fact]
    public void FO3_a_formation_command_changes_the_squads_formation_at_once_and_is_logged()
    {
        var sim = OnTheMove(Squad(SquadFormation.File));

        sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));

        Assert.Equal(SquadFormation.Wedge, sim.Squad("r1").Formation);
        var changed = Assert.Single(sim.Events.OfType<FormationChanged>());
        Assert.Equal(("rj", "r1", SquadFormation.Wedge), (changed.AgentId, changed.SquadId, changed.Formation));
    }

    [Fact]
    public void FO3_each_member_takes_its_new_slot_when_it_acts_on_the_command()
    {
        var sim = OnTheMove(Squad(SquadFormation.File));
        var command = sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));

        // k2 (experienced) has acted at 1 s, pj at 3 s; k1 (inexperienced, through pj) only at 8 s.
        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Equal(SquadFormation.Wedge, sim.Squad("r1").FormationOf("k2"));
        Assert.Equal(SquadFormation.Wedge, sim.Squad("r1").FormationOf("pj"));
        Assert.Equal(SquadFormation.File, sim.Squad("r1").FormationOf("k1"));
        var leader = sim.Unit("rj").Position.Xy;
        Assert.True(Slot(sim, "k1").DistanceTo(SquadMovementAssertions.OnStraightRoute(leader, East, Nominal(SquadFormation.File, "k1"))) < 0.01,
            "k1 still has its file slot");

        sim.RunFor(TimeSpan.FromSeconds(30));
        sim.ShouldHaveActedOn("k1", command,
            SpecSquad.InexperiencedReactionSeconds + SpecSquad.RelaySeconds + SpecSquad.InexperiencedReactionSeconds);
        Assert.Equal(SquadFormation.Wedge, sim.Squad("r1").FormationOf("k1"));
    }

    [Fact]
    public void FO3_after_the_change_the_squad_moves_on_in_the_new_formation()
    {
        var sim = OnTheMove(Squad(SquadFormation.File), pastX: 120);
        sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));

        sim.RunUntil(s => s.Unit("rj").Position.X > 300, TenMinutes, "rj past x = 300");

        var leader = sim.Unit("rj").Position.Xy;
        foreach (var id in Members)
        {
            Assert.True(Slot(sim, id).DistanceTo(SquadMovementAssertions.OnStraightRoute(leader, East, Nominal(SquadFormation.Wedge, id))) < 0.01);
        }

        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
    }

    [Fact]
    public void FO4_a_wedge_wider_than_the_corridor_is_compressed_evenly_to_fit()
    {
        // Inner zone 20 m wide: the wedge reaches 5 steps out to the right of rj (k2, 35.4 m; rj walks the left arm), which
        // scale to 10 m, so each step out becomes 2.0 m.
        const double width = 20;
        var sim = Squad(SquadFormation.Wedge);
        sim.Order("rj", new MoveThroughCorridor(Straight(width)))
            .RunUntil(s => s.Unit("rj").Position.X > 200, TenMinutes, "rj past x = 200");

        var leader = sim.Unit("rj").Position.Xy;
        var scale = (width / 2) / (5 * S);
        foreach (var id in Members)
        {
            var nominal = Nominal(SquadFormation.Wedge, id);
            var expected = SquadMovementAssertions.OnStraightRoute(leader, East, nominal with { Across = nominal.Across * scale });
            Assert.True(Slot(sim, id).DistanceTo(expected) < 0.01, $"{id}'s slot is {Slot(sim, id)}, expected {expected}");
            Assert.True(Math.Abs(Slot(sim, id).Y - Start.Y) <= (width / 2) + 1e-9, $"{id}'s slot is outside the inner zone");
        }

        Assert.True(S * scale >= SpecSquad.MinimumGapMeters, "precondition: the gap across stays at least 1 m");
    }

    [Fact]
    public void FO4_a_corridor_too_narrow_for_the_wedge_gives_a_file()
    {
        // Inner zone 6 m wide: scaling to 3 m would leave 0.6 m between neighbours across, under the 1 m minimum.
        const double width = 6;
        var sim = Squad(SquadFormation.Wedge);
        sim.Order("rj", new MoveThroughCorridor(Straight(width)))
            .RunUntil(s => s.Unit("rj").Position.X > 200, TenMinutes, "rj past x = 200");

        var leader = sim.Unit("rj").Position.Xy;
        foreach (var id in Members)
        {
            var expected = SquadMovementAssertions.OnStraightRoute(leader, East, Nominal(SquadFormation.File, id));
            Assert.True(Slot(sim, id).DistanceTo(expected) < 0.01, $"{id}'s slot is {Slot(sim, id)}, expected the file slot {expected}");
        }
    }

    [Fact]
    public void FO5_a_slot_inside_a_building_moves_toward_the_leaders_path_to_the_nearest_usable_point()
    {
        // At B the wedge's k2 slot (the right tail) would be (398.8, 65.6), inside a building that fills x 393–405,
        // y 58–72; k1's slot (405.9, 72.7) is just outside it.
        var sim = Squad(SquadFormation.Wedge, m => m.Fill(TerrainClass.Building, 393, 58, 405, 72));

        sim.Order("rj", new MoveThroughCorridor(Straight(width: 80))).RunUntilSquadSettled("r1", TenMinutes);

        var nominal = SquadMovementAssertions.OnStraightRoute(B, East, Nominal(SquadFormation.Wedge, "k2"));
        var slot = Slot(sim, "k2");
        Assert.Equal(nominal.X, slot.X, precision: 6);                      // on the line toward the path
        Assert.InRange(slot.Y, 72, 72 + SpecV01.CellSize);                  // just north of the building
        Assert.NotEqual(TerrainClass.Building, ((IWorldQuery)sim.Terrain).ClassAt(slot.X, slot.Y));
        sim.ShouldBeInSlot("k2", toleranceMeters: 1);
        sim.Unit("k2").Should().BeIdle();
    }
}
