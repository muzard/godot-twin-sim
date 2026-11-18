using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Watch sectors (backlog 0004 S5a1: SP7, merged with S2c). A member following its squad watches its S2c observation
/// sectors: the centre of each is a watch direction, its first one the member's <see cref="Twin.Sim.Units.Unit.WatchDirection"/>,
/// and the best angle factor of them counts. So in a wedge an enemy on the right flank is spotted by the right arm and not
/// by the left, and the last two of a file, which also observe the rear, spot an enemy behind it.
/// </summary>
public class WatchSectorTests
{
    private static readonly Vec2 Start = new(80, 101);

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void SP7_each_member_on_the_move_watches_the_centre_of_its_first_observation_sector(SquadFormation formation)
    {
        var sim = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .Build()
            .Order("rj", new MoveTo(new Vec2(420, 101)))
            .RunUntil(s => s.Unit("rj").Position.X > 200, TimeSpan.FromMinutes(10), "rj past x = 200");

        foreach (var id in SpottingLayouts.Members)
        {
            var sectors = sim.Unit(id).ObservationSectors;
            Assert.NotEmpty(sectors);
            sim.Unit(id).Should().BeWatching(sectors[0].Centre);
        }
    }

    [Fact]
    public void SP7_a_settled_squad_keeps_watching_its_observation_sectors()
    {
        var sim = SpottingLayouts.FlankAtRest();

        sim.Unit("j1").Should().BeWatching(SpottingLayouts.East);
        foreach (var id in SpottingLayouts.WedgeLeftArm)
        {
            sim.Unit(id).Should().BeWatching(new Vec2(0, 1));
        }

        foreach (var id in SpottingLayouts.WedgeRightArm)
        {
            sim.Unit(id).Should().BeWatching(new Vec2(0, -1));
        }
    }

    [Fact]
    public void A_squad_leader_ordered_to_watch_keeps_watching_that_way_while_its_squad_rests()
    {
        var sim = SpottingLayouts.FlankAtRest();
        var west = new Vec2(-1, 0);

        sim.Order("rj", new Watch(west)).RunFor(TimeSpan.FromSeconds(10));

        sim.Unit("rj").Should().BeWatching(west);
    }

    [Fact]
    public void SP7_in_a_wedge_an_enemy_on_the_right_flank_is_spotted_by_the_right_arm()
    {
        var sim = SpottingLayouts.FlankAtRest();
        Assert.False(sim.Simulation.CanSee("j2", "red"), "red lies hidden behind its mound");

        var first = sim.RedStandsUp().RunUntilAnyoneSpots("red", TimeSpan.FromMinutes(1));

        Assert.Equal("j2", first.AgentId);
    }

    [Fact]
    public void SP7_in_a_wedge_the_left_arm_does_not_spot_an_enemy_on_the_right_flank_before_it_hears_of_it()
    {
        var sim = SpottingLayouts.FlankAtRest();

        sim.RedStandsUp().RunFor(TimeSpan.FromMinutes(2));

        foreach (var id in SpottingLayouts.WedgeLeftArm)
        {
            Assert.True(sim.Simulation.CanSee(id, "red"), $"{id} has a clear line of sight to red; only its sectors keep it from spotting");
            var heard = sim.Events.OfType<SpotReceived>().FirstOrDefault(e => e.AgentId == id && e.EnemyId == "red");
            if (sim.SpottedBy(id, "red") is { } spotted)
            {
                Assert.True(heard is not null && heard.Time <= spotted.Time,
                    $"{id} spotted red at t={spotted.Time} s before hearing of it ({heard?.Time.ToString() ?? "never"})");
            }
        }

        sim.ShouldNotHaveSpotted("vj", "red"); // out of voice range of the spotter and watching away
        sim.ShouldNotHaveSpotted("t1", "red");
    }

    [Fact]
    public void SP7_the_last_two_of_a_file_spot_an_enemy_behind_it_with_their_rear_sectors()
    {
        var sim = SpottingLayouts.BehindTheFile();

        var first = sim.RedStandsUp().RunUntilAnyoneSpots("red", TimeSpan.FromMinutes(1));

        Assert.Contains(first.AgentId, new[] { "kk", "t1" });
    }
}
