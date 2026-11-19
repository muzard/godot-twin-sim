using Twin.Sim.Corridors;
using Twin.Sim.Geometry;

namespace Twin.Sim.Tests.Geometry;

/// <summary>The corridor's direction of movement and the forward test used for sideways forest exits (backlog 0004).</summary>
public class CorridorDirectionTests
{
    private static Corridor Through(params Vec2[] points) => new(points, 40, 10, 1, HardBoundaries: []);

    [Fact]
    public void The_direction_is_that_of_the_nearest_centerline_segment()
    {
        var corridor = Through(new(0, 0), new(100, 0), new(100, 100));

        Assert.Equal(new Vec2(1, 0), corridor.DirectionAt(new Vec2(40, 10)));
        Assert.Equal(new Vec2(0, 1), corridor.DirectionAt(new Vec2(110, 60)));
    }

    [Fact]
    public void At_a_bend_equally_near_both_segments_the_first_one_wins()
    {
        var corridor = Through(new(0, 0), new(100, 0), new(100, 100));

        Assert.Equal(new Vec2(1, 0), corridor.DirectionAt(new Vec2(100, 0)));
    }

    [Fact]
    public void Repeated_centerline_points_are_skipped()
    {
        var corridor = Through(new(0, 0), new(0, 0), new(0, 100));

        Assert.Equal(new Vec2(0, 1), corridor.DirectionAt(new Vec2(5, 0)));
    }

    [Fact]
    public void A_centerline_of_a_single_point_has_no_direction_and_every_move_counts_as_forward()
    {
        var corridor = Through(new Vec2(0, 0));

        Assert.Equal(Vec2.Zero, corridor.DirectionAt(new Vec2(5, 5)));
        Assert.True(corridor.IsForward(new Vec2(5, 5), new Vec2(5, 5), new Vec2(5, 0), 45));
    }

    [Fact]
    public void A_move_of_no_length_counts_as_forward()
    {
        Assert.True(Through(new(0, 0), new(100, 0)).IsForward(new Vec2(50, 0), new Vec2(50, 0), new Vec2(50, 0), 45));
    }

    [Fact]
    public void A_move_exactly_at_the_limit_is_forward_and_one_beyond_it_is_not()
    {
        var corridor = Through(new(0, 0), new(100, 0));
        var at = new Vec2(50, 0);

        Assert.True(corridor.IsForward(at, at, at + new Vec2(1, 1), 45));
        Assert.False(corridor.IsForward(at, at, at + new Vec2(1, 1.01), 45));
        Assert.False(corridor.IsForward(at, at, at + new Vec2(0, 1), 45), "square to the centerline is sideways");
        Assert.False(corridor.IsForward(at, at, at + new Vec2(-1, 0), 45), "backward is not forward");
    }
}
