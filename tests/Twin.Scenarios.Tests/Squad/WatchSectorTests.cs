using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Watch sectors (backlog 0004 S5a1: SP7). A member following its squad watches its formation place's sector, an angle
/// to the right of the squad's direction of travel (<see cref="SpecSpotting.WatchSectors"/>), and keeps it when the
/// squad has settled. So in a wedge an enemy on the right flank is spotted by the right arm and not by the left.
/// </summary>
public class WatchSectorTests
{
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 East = new(1, 0);

    [Theory]
    [InlineData(SquadFormation.File)]
    [InlineData(SquadFormation.Wedge)]
    public void SP7_each_member_watches_its_sector_relative_to_the_direction_of_travel(SquadFormation formation)
    {
        var sim = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference() with { Formation = formation }, Start)
            .Build()
            .Order("rj", new MoveTo(new Vec2(420, 101)))
            .RunUntil(s => s.Unit("rj").Position.X > 200, TimeSpan.FromMinutes(10), "rj past x = 200");

        foreach (var id in SpecSpotting.FormationOrder)
        {
            sim.Unit(id).Should().BeWatching(SpecSpotting.Turned(East, SpecSpotting.WatchDegrees(formation, id)));
        }
    }

    [Fact]
    public void SP7_a_settled_squad_keeps_watching_its_sectors()
    {
        var sim = SpottingLayouts.FlankAtRest();

        foreach (var id in SpecSpotting.FormationOrder)
        {
            sim.Unit(id).Should().BeWatching(SpecSpotting.Turned(East, SpecSpotting.WatchDegrees(SquadFormation.Wedge, id)));
        }
    }

    [Fact]
    public void A_squad_leader_ordered_to_watch_keeps_watching_that_way_while_its_squad_rests()
    {
        var sim = SpottingLayouts.FlankAtRest();
        var north = new Vec2(0, 1);

        sim.Order("rj", new Watch(north)).RunFor(TimeSpan.FromSeconds(10));

        sim.Unit("rj").Should().BeWatching(north);
    }

    [Fact]
    public void SP7_in_a_wedge_an_enemy_on_the_right_flank_is_spotted_by_the_right_arm()
    {
        var sim = SpottingLayouts.FlankAtRest();
        Assert.False(sim.Simulation.CanSee("vj", "red"), "red lies hidden behind its mound");

        var first = sim.RedStandsUp().RunUntilAnyoneSpots("red", TimeSpan.FromMinutes(1));

        Assert.Equal("vj", first.AgentId);
    }

    [Fact]
    public void SP7_in_a_wedge_an_enemy_on_the_right_flank_is_not_spotted_by_the_left_arm()
    {
        var sim = SpottingLayouts.FlankAtRest();

        sim.RedStandsUp().RunFor(TimeSpan.FromMinutes(2));

        foreach (var id in SpottingLayouts.WedgeLeftArm)
        {
            Assert.True(sim.Simulation.CanSee(id, "red"), $"{id} has a clear line of sight to red; only its sector keeps it from spotting");
            sim.ShouldNotHaveSpotted(id, "red");
        }
    }
}
