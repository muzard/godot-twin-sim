using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Positions for the drill (backlog 0003, CP), judged against given attackers:
/// <list type="bullet">
/// <item>Cover: no attacker can see the agent there lying prone.</item>
/// <item>Firing position: cover, and the agent sees an attacker from there crouching, with a clear fire sector.</item>
/// </list>
/// The search looks within 20 m on a 2 m grid, at points reachable without crossing a hard boundary, nearest first; for a
/// firing position, forward first (no farther from the attacker than the agent is now). The queries are
/// <c>Simulation.FindCover</c> and <c>Simulation.FindFiringPosition</c>; they return null when there is none.
/// Each test also checks the returned point against the definition with the 0002 line of sight.
/// </summary>
public class CoverPositionTests
{
    private static readonly CombatRules Rules = SpecShooting.Rules;
    private static readonly Vec2 Blue = new(41, 21);
    private static readonly Vec2 Red = new(141, 21);

    private static ScenarioRun Place(RasterWorld world, params (string Id, Vec2 At, Side Side)[] others)
    {
        var scenario = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", Red, Side.Red, RulesOfEngagement.HoldFire);
        foreach (var (id, at, side) in others)
        {
            scenario.WithFighter(id, at, side, RulesOfEngagement.HoldFire);
        }

        return scenario.Build();
    }

    private static Vec3 Ground(RasterWorld world, Vec2 p, double above) => p.WithZ(world.GroundHeight(p.X, p.Y) + above);

    // The attacker (standing) sees a point's prone aim point.
    private static bool SeesProne(RasterWorld world, Vec2 attacker, Vec2 point) => LineOfSight.Between(world,
        Ground(world, attacker, SpecShooting.Standing.EyeHeight), Ground(world, point, SpecShooting.Prone.AimHeight), Rules).IsVisible(Rules);

    // From a point, a crouching agent sees the attacker (standing).
    private static bool SeesFromCrouch(RasterWorld world, Vec2 point, Vec2 attacker) => LineOfSight.Between(world,
        Ground(world, point, SpecShooting.Crouching.EyeHeight), Ground(world, attacker, SpecShooting.Standing.AimHeight), Rules).IsVisible(Rules);

    private static RasterWorld Strip(Func<TestMap, TestMap> features) =>
        features(TestMap.Create(widthMeters: 200, heightMeters: 60)).Build();

    [Fact]
    public void CP1_behind_a_building_is_cover_but_not_a_firing_position()
    {
        // A building south-east of blue (cells 46–52 × 12–18) hides the ground west of it from red.
        var world = Strip(m => m.Fill(TerrainClass.Building, fromX: 47, fromY: 13, toX: 51, toY: 17));
        var sim = Place(world);

        var cover = sim.Simulation.FindCover("blue", "red");

        Assert.True(SeesProne(world, Red, Blue), "precondition: blue is exposed where it is");
        Assert.NotNull(cover);
        Assert.False(SeesProne(world, Red, cover.Value), $"red sees blue prone at {cover}");
        Assert.True(cover.Value.DistanceTo(Blue) <= 20);
        Assert.Null(sim.Simulation.FindFiringPosition("blue", "red"));
    }

    [Fact]
    public void CP2_behind_a_low_rise_is_a_firing_position()
    {
        // A 0.5 m rise north-east of blue (cells 52–54 × 26–36): it hides a prone agent behind it, not a crouching one.
        var world = Strip(m => m.WithMound(fromX: 53, fromY: 27, toX: 53, toY: 35, height: 0.5));
        var sim = Place(world);

        var position = sim.Simulation.FindFiringPosition("blue", "red");

        Assert.NotNull(position);
        Assert.False(SeesProne(world, Red, position.Value), $"red sees blue prone at {position}");
        Assert.True(SeesFromCrouch(world, position.Value, Red), $"blue cannot see red crouching at {position}");
        Assert.True(position.Value.DistanceTo(Blue) <= 20);
        Assert.NotNull(sim.Simulation.FindCover("blue", "red"));
    }

    [Fact]
    public void CP3_on_open_ground_there_is_neither()
    {
        var sim = Place(Strip(m => m));

        Assert.Null(sim.Simulation.FindCover("blue", "red"));
        Assert.Null(sim.Simulation.FindFiringPosition("blue", "red"));
    }

    [Fact]
    public void CP4_the_nearest_cover_within_twenty_meters_is_chosen()
    {
        // Blue at x = 61, red 100 m east. A building 6 m west of blue (cells 54–56) gives cover from x = 53, about 8 m
        // away; a second one farther west (cells 44–46) gives cover from x = 43 as well. The nearer cover is chosen.
        var near = Strip(m => m
            .Fill(TerrainClass.Building, fromX: 55, fromY: 19, toX: 55, toY: 23)
            .Fill(TerrainClass.Building, fromX: 45, fromY: 19, toX: 45, toY: 23));
        var sim = Scenario.ForCombat().WithTerrain(near)
            .WithFighter("blue", new Vec2(61, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(161, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build();

        var cover = sim.Simulation.FindCover("blue", "red");

        Assert.NotNull(cover);
        Assert.InRange(cover.Value.DistanceTo(new Vec2(61, 21)), 0, 9);
        Assert.True(cover.Value.X < 54, $"cover is behind the nearer building, at {cover}");
    }

    [Fact]
    public void CP4_cover_farther_than_twenty_meters_is_not_chosen()
    {
        // Only a building whose shadow starts 28 m west of blue.
        var far = Strip(m => m.Fill(TerrainClass.Building, fromX: 35, fromY: 19, toX: 35, toY: 23));
        var sim = Scenario.ForCombat().WithTerrain(far)
            .WithFighter("blue", new Vec2(61, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(161, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build();

        Assert.Null(sim.Simulation.FindCover("blue", "red"));
    }

    [Fact]
    public void CP5_a_position_behind_a_hard_boundary_is_not_chosen()
    {
        // The building's shadow (from x = 53) is across a road blue may not cross (an order boundary at x = 58).
        var world = Strip(m => m.Fill(TerrainClass.Building, fromX: 55, fromY: 19, toX: 55, toY: 23));
        var road = new HardBoundary("road", [new Vec2(58, 0), new Vec2(58, 60)], BoundarySource.Order);
        var corridor = new Corridor([new Vec2(61, 11), new Vec2(61, 51)], Width: 6, SoftMargin: 2, SoftPenalty: 1, HardBoundaries: [road]);
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("blue", new Vec2(61, 21), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(161, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor))
            .Step();

        var cover = sim.Simulation.FindCover("blue", "red");

        Assert.True(cover is null || cover.Value.X > 58, $"cover across the road at {cover}");
    }

    [Fact]
    public void CP6_a_position_without_a_clear_fire_sector_is_not_a_firing_position()
    {
        // As CP2, but a team-mate stands between the rise and red: every spot behind the rise has him in its sector.
        var world = Strip(m => m.WithMound(fromX: 53, fromY: 27, toX: 53, toY: 35, height: 0.5));
        var sim = Place(world, ("blue-2", new Vec2(81, 29), Side.Blue));

        Assert.Null(sim.Simulation.FindFiringPosition("blue", "red"));
    }

    [Fact]
    public void CP7_a_position_must_hide_the_agent_from_every_known_attacker()
    {
        // The building hides the spot from red to the east, not from red-2 to the north.
        var world = Strip(m => m.Fill(TerrainClass.Building, fromX: 47, fromY: 13, toX: 51, toY: 17));
        var sim = Place(world, ("red-2", new Vec2(41, 57), Side.Red));

        Assert.NotNull(sim.Simulation.FindCover("blue", "red"));
        Assert.Null(sim.Simulation.FindCover("blue", "red", "red-2"));
    }

    [Fact]
    public void CP8_a_firing_position_forward_is_preferred_to_a_nearer_one_behind()
    {
        // Two low rises (0.4 m) across the map, one 18 m ahead of blue and one 4 m behind it. Red is 60 m east.
        // Behind the far rise lie firing positions from about 12 m ahead; behind the near one from about 6 m back.
        Vec2 red = new(101, 21);
        RasterWorld World(bool ahead) => Strip(m =>
        {
            var map = m.WithRidge(fromX: 36, toX: 38, height: 0.4);
            return ahead ? map.WithRidge(fromX: 58, toX: 60, height: 0.4) : map;
        });

        var both = World(ahead: true);
        var forward = Scenario.ForCombat().WithTerrain(both)
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire)
            .Build().Simulation.FindFiringPosition("blue", "red");
        var onlyBehind = World(ahead: false);
        var backward = Scenario.ForCombat().WithTerrain(onlyBehind)
            .WithFighter("blue", Blue, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.HoldFire)
            .Build().Simulation.FindFiringPosition("blue", "red");

        Assert.True(SeesProne(both, red, Blue), "precondition: blue is exposed where it is");
        Assert.NotNull(forward);
        Assert.InRange(forward.Value.X, 50, 58);
        Assert.False(SeesProne(both, red, forward.Value));
        Assert.NotNull(backward);
        Assert.InRange(backward.Value.X, 26, 36);
        Assert.False(SeesProne(onlyBehind, red, backward.Value));
    }
}
