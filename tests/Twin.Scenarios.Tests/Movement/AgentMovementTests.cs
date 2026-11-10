using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Movement;

/// <summary>
/// An agent follows its planned path at the local speed: v · dt per step, no acceleration. Its eyes are at
/// ground height plus eye height (standing, 1.6 m); its position stays on the ground. M8.
/// </summary>
public class AgentMovementTests
{
    private static readonly Vec2 Start = new(11, 11);
    private static readonly Vec2 End = new(51, 11);

    private static Corridor StraightCorridor() =>
        new([Start, End], Width: 10, SoftMargin: 5, SoftPenalty: SpecV01.SoftPenalty, HardBoundaries: []);

    private static ScenarioRun AgentIn(RasterWorld world) => Scenario.Create()
        .WithTerrain(world)
        .WithTickRate(SpecV01.TickRateHz)
        .WithUnit("alpha", at: Start, profile: SpecV01.Infantry)
        .Build();

    [Fact]
    public void M8_agent_advances_v_times_dt_per_step_at_the_local_terrain_speed()
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 20, fill: TerrainClass.Forest).Build();
        var sim = AgentIn(world);
        sim.Order("alpha", new MoveThroughCorridor(StraightCorridor())).Step(2);
        var before = sim.Unit("alpha").Position.Xy;

        sim.Step();

        var travelled = sim.Unit("alpha").Position.Xy.DistanceTo(before);
        Assert.Equal(SpecV01.Infantry.ForestMps * SpecV01.DtSeconds, travelled, 9);
    }

    [Fact]
    public void M8_agent_eyes_are_at_ground_height_plus_eye_height_while_its_position_stays_on_the_ground()
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 20).WithHeights(p => 0.05 * p.X).Build();
        var sim = AgentIn(world);

        sim.Order("alpha", new MoveThroughCorridor(StraightCorridor())).Step(5);

        var unit = sim.Unit("alpha");
        var ground = world.GroundHeight(unit.Position.X, unit.Position.Y);
        unit.Should().BeOnGround(world);
        Assert.Equal(ground + SpecV01.Infantry.EyeHeightMeters, unit.EyePosition.Z, 9);
        Assert.Equal(unit.Position.Xy, unit.EyePosition.Xy);
    }

    [Fact]
    public void Agent_reaches_the_end_of_the_corridor_and_becomes_idle()
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 20).Build();
        var sim = AgentIn(world);

        sim.Order("alpha", new MoveThroughCorridor(StraightCorridor()))
           .RunUntil(s => s.Unit("alpha").IsIdle, TimeSpan.FromMinutes(5), "alpha idle");

        sim.Unit("alpha").Should().BeAt(End).BeIdle();
    }

    [Fact]
    public void Corridor_order_with_fewer_than_two_centerline_points_is_rejected()
    {
        var sim = AgentIn(TestMap.Create(widthMeters: 60, heightMeters: 20).Build());
        var corridor = new Corridor([End], Width: 10, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);

        Assert.Throws<OrderRejectedException>(() => sim.Order("alpha", new MoveThroughCorridor(corridor)));
    }

    [Fact]
    public void Corridor_order_ending_outside_the_world_is_rejected()
    {
        var sim = AgentIn(TestMap.Create(widthMeters: 60, heightMeters: 20).Build());
        var corridor = new Corridor([Start, new Vec2(500, 11)], Width: 10, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);

        Assert.Throws<OrderRejectedException>(() => sim.Order("alpha", new MoveThroughCorridor(corridor)));
    }
}
