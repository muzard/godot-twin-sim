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
/// Headings and observation sectors (backlog 0004, S2c). The squad observes all round. On the move the point observes
/// ahead and the others their own side: in a file the point is the first and the others alternate left (odd places)
/// and right (even places); in a wedge the point is the apex and each arm observes its own side. The two at the back (the
/// last two of a file, the ends of a wedge's arms) also observe the rear and the air. Deployed at the forest edge,
/// everyone observes out of the forest, and the two ends of the line also their flank, the rear and the air. Every
/// sector is 90° wide, and a member faces the centre of its first sector. Sectors are data only: seeing does not depend
/// on them yet (the observation model is in development).
/// </summary>
public class SquadObservationTests
{
    private static readonly Vec2 East = new(1, 0);
    private static readonly Vec2 West = new(-1, 0);
    private static readonly Vec2 North = new(0, 1);
    private static readonly Vec2 South = new(0, -1);

    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);

    // Who observes what on the move, per formation. File order: pj, k1, k2, rj, j1, j2, vj, kk, t1. Wedge: the open line
    // t1, vj, kk, rj, j1, j2, pj, k1, k2 folded back at j1.
    private sealed record Roles(string Point, string[] Left, string[] Right, string[] Back);

    private static Roles RolesIn(SquadFormation formation) => formation == SquadFormation.File
        ? new("pj", ["k1", "rj", "j2", "kk"], ["k2", "j1", "vj", "t1"], ["kk", "t1"])
        : new("j1", ["rj", "kk", "vj", "t1"], ["j2", "pj", "k1", "k2"], ["t1", "k2"]);

    private static IEnumerable<string> Members => SpecSquad.Reference().Members.Select(m => m.UnitId);

    // The squad walks east on open ground; after 60 s everyone follows its slot.
    private static ScenarioRun OnTheMoveEast(SquadFormation formation)
    {
        var sim = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .Build();
        sim.Order("rj", new MoveThroughCorridor(new Corridor([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));
        return sim.RunFor(TimeSpan.FromSeconds(60));
    }

    private static IEnumerable<Vec2> AllRound()
    {
        for (var degrees = 0; degrees < 360; degrees += 5)
        {
            var radians = degrees * Math.PI / 180;
            yield return new Vec2(Math.Cos(radians), Math.Sin(radians));
        }
    }

    private static void ObservesAllRound(ScenarioRun sim)
    {
        foreach (var direction in AllRound())
        {
            Assert.True(Members.Any(id => sim.Unit(id).Observes(direction)), $"nobody in the squad observes {direction}");
        }
    }

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void On_the_move_the_point_observes_ahead_and_the_others_their_own_side(SquadFormation formation)
    {
        var sim = OnTheMoveEast(formation);
        var roles = RolesIn(formation);

        sim.Unit(roles.Point).Should().Observe(East).NotObserve(North).NotObserve(South);
        foreach (var id in roles.Left)
        {
            sim.Unit(id).Should().Observe(North).NotObserve(South);
        }

        foreach (var id in roles.Right)
        {
            sim.Unit(id).Should().Observe(South).NotObserve(North);
        }
    }

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void On_the_move_the_two_at_the_back_also_observe_the_rear_and_the_air(SquadFormation formation)
    {
        var sim = OnTheMoveEast(formation);
        var back = RolesIn(formation).Back;

        foreach (var id in Members)
        {
            if (back.Contains(id))
            {
                sim.Unit(id).Should().Observe(West).WatchTheAir();
            }
            else
            {
                sim.Unit(id).Should().NotObserve(West).NotWatchTheAir();
            }
        }
    }

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void On_the_move_the_squad_observes_all_round(SquadFormation formation)
    {
        ObservesAllRound(OnTheMoveEast(formation));
    }

    [Fact]
    public void On_the_move_each_member_faces_the_centre_of_its_own_sector()
    {
        var sim = OnTheMoveEast(SquadFormation.Wedge);

        sim.Unit("j1").Should().Face(East);  // the apex
        sim.Unit("rj").Should().Face(North); // left arm
        sim.Unit("j2").Should().Face(South); // right arm
    }

    // The squad deployed at the forest edge of SquadEdgeDeploymentTests: forest west of x = 250, facing out east.
    private static ScenarioRun DeployedAtTheForestEdge()
    {
        var world = TestMap.Create(widthMeters: 500, heightMeters: 200, fill: TerrainClass.Forest)
            .Fill(TerrainClass.Open, 250, 0, 500, 200).Build();
        var sim = Scenario.ForSquad()
            .WithTerrain(world)
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, Start)
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(60))
            .Build();
        sim.Order("rj", new MoveThroughCorridor(new Corridor([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));
        return sim.RunUntil(s => s.Events.OfType<EdgeDeployed>().Any(), TimeSpan.FromMinutes(20), "squad deployed at the forest edge");
    }

    [Fact]
    public void At_the_forest_edge_everyone_faces_out_and_the_ends_of_the_line_also_watch_their_flanks_the_rear_and_the_air()
    {
        var sim = DeployedAtTheForestEdge();

        foreach (var id in Members)
        {
            sim.Unit(id).Should().Face(East).Observe(East);
        }

        sim.Unit("t1").Should().Observe(North).Observe(West).WatchTheAir(); // the left end of the line
        sim.Unit("k2").Should().Observe(South).Observe(West).WatchTheAir(); // the right end
        foreach (var id in new[] { "vj", "kk", "rj", "j1", "j2", "pj", "k1" })
        {
            sim.Unit(id).Should().NotObserve(North).NotObserve(South).NotObserve(West).NotWatchTheAir();
        }

        ObservesAllRound(sim);
    }

    [Fact]
    public void A_unit_outside_a_squad_faces_the_way_it_moves()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: new Vec2(100, 100)).Build();

        sim.Order("alpha", new MoveTo(new Vec2(100, 300))).RunFor(TimeSpan.FromSeconds(5));

        sim.Unit("alpha").Should().Face(North);
    }
}
