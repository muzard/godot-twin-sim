using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>
/// Line of sight (backlog 0002, L): the line from the observer's eye to the target's aim point is blocked by the ground
/// and by buildings, and attenuated by forest below the canopy: T = exp(−L_forest / 25 m). The target is visible when
/// the line is clear and T ≥ 0.05. Observer at x = 11, target at x = 111 on a 200 m long map, both standing.
/// </summary>
public class LineOfSightTests
{
    private static readonly CombatRules Rules = SpecShooting.Rules;
    private static readonly Vec2 Observer = new(11, 11);
    private static readonly Vec2 Target = new(111, 11);

    private static TestMap Strip() => TestMap.Create(widthMeters: 200, heightMeters: 20);

    private static SightLine Look(RasterWorld world, Vec2 from, Vec2 to) => LineOfSight.Between(world,
        from.WithZ(world.GroundHeight(from.X, from.Y) + SpecShooting.Standing.EyeHeight),
        to.WithZ(world.GroundHeight(to.X, to.Y) + SpecShooting.Standing.AimHeight),
        Rules);

    [Fact]
    public void L1_on_flat_open_ground_the_target_is_visible_with_full_transmission()
    {
        var line = Look(Strip().Build(), Observer, Target);

        Assert.True(line.Clear);
        Assert.Equal(1.0, line.Transmission, 12);
        Assert.True(line.IsVisible(Rules));
    }

    [Fact]
    public void L2_a_ridge_between_shooter_and_target_blocks_line_of_sight()
    {
        // 5 m ridge: well above both the 1.6 m eye and the 1.0 m aim point.
        var line = Look(Strip().WithRidge(fromX: 60, toX: 64, height: 5).Build(), Observer, Target);

        Assert.False(line.Clear);
        Assert.False(line.IsVisible(Rules));
    }

    [Fact]
    public void L3_a_shooter_on_high_ground_sees_over_a_low_ridge()
    {
        // The same ridge, but the observer stands 10 m higher: the line passes about 6 m above the ridge.
        var world = Strip().WithHeights(p => p.X < 20 ? 10 : 0).WithRidge(fromX: 60, toX: 64, height: 5).Build();

        var line = Look(world, Observer, Target);

        Assert.True(line.Clear);
        Assert.True(line.IsVisible(Rules));
    }

    [Fact]
    public void L4_a_building_between_them_blocks_line_of_sight()
    {
        var world = Strip().Fill(TerrainClass.Building, fromX: 60, fromY: 10, toX: 62, toY: 12).Build();

        Assert.False(Look(world, Observer, Target).Clear);
    }

    [Fact]
    public void L5_forest_attenuates_by_the_length_of_the_line_inside_it()
    {
        // Forest cells from x = 50 to x = 70: 20 m of the line, well below the 15 m canopy.
        var world = Strip().Fill(TerrainClass.Forest, fromX: 50, fromY: 0, toX: 70, toY: 20).Build();

        var line = Look(world, Observer, Target);

        // The line is sampled every 0.5 m, so the forest length is known to within one sample.
        Assert.True(line.Clear);
        Assert.InRange(line.Transmission, SpecShooting.Transmission(20.5), SpecShooting.Transmission(19.5));
    }

    [Fact]
    public void L6_beyond_about_75_m_of_forest_the_target_is_not_visible()
    {
        var eighty = Strip().Fill(TerrainClass.Forest, fromX: 30, fromY: 0, toX: 110, toY: 20).Build();
        var seventy = Strip().Fill(TerrainClass.Forest, fromX: 30, fromY: 0, toX: 100, toY: 20).Build();

        Assert.False(Look(eighty, Observer, Target).IsVisible(Rules), "80 m of forest: T ≈ 0.04 < 0.05");
        Assert.True(Look(seventy, Observer, Target).IsVisible(Rules), "70 m of forest: T ≈ 0.06 ≥ 0.05");
    }

    [Fact]
    public void L7_line_of_sight_is_symmetric_for_equal_postures_on_flat_ground()
    {
        // A 30 m forest strip off the middle, so a one-sided error would show.
        var world = Strip().Fill(TerrainClass.Forest, fromX: 40, fromY: 0, toX: 70, toY: 20).Build();

        var forward = Look(world, Observer, Target);
        var back = Look(world, Target, Observer);

        Assert.Equal(forward.IsVisible(Rules), back.IsVisible(Rules));
        Assert.True(Math.Abs(forward.Transmission - back.Transmission) < 0.02,
            $"transmission {forward.Transmission} one way, {back.Transmission} the other");
    }

    [Fact]
    public void Forest_above_the_line_does_not_attenuate_a_line_over_the_canopy()
    {
        // Observer on a 40 m hill looking at a target on a 40 m hill across a forest valley: the line runs above the canopy.
        var world = Strip().WithHeights(p => p.X < 20 || p.X > 100 ? 40 : 0)
            .Fill(TerrainClass.Forest, fromX: 30, fromY: 0, toX: 90, toY: 20).Build();

        var line = Look(world, Observer, Target);

        Assert.True(line.Clear);
        Assert.Equal(1.0, line.Transmission, 12);
    }
}
