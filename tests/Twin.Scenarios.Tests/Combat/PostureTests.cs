using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Posture (backlog 0002, PO): standing, crouching or prone sets eye height, aim point, speed and target size.
/// <see cref="SetPosture"/> takes 1 s, during which the agent neither moves nor shoots, and keeps the agent's order.
/// </summary>
public class PostureTests
{
    private static readonly Vec2 Start = new(11, 11);

    private static Corridor Straight() => new([Start, new Vec2(51, 11)], Width: 10, SoftMargin: 5, SoftPenalty: 1, HardBoundaries: []);

    private static ScenarioRun Alone(RasterWorld world) => Scenario.ForCombat()
        .WithTerrain(world)
        .WithFighter("blue", Start, Side.Blue, RulesOfEngagement.HoldFire)
        .Build();

    [Theory]
    [InlineData(Posture.Standing, 1.6)]
    [InlineData(Posture.Crouching, 1.1)]
    [InlineData(Posture.Prone, 0.3)]
    public void PO1_eye_height_follows_posture(Posture posture, double eyeHeight)
    {
        var world = TestMap.Create(widthMeters: 60, heightMeters: 20).WithHeights(p => 0.05 * p.X).Build();
        var sim = Alone(world);

        sim.Order("blue", new SetPosture(posture)).RunFor(TimeSpan.FromSeconds(1.5));

        var unit = sim.Unit("blue");
        unit.Should().BeIn(posture).BeOnGround(world);
        Assert.Equal(world.GroundHeight(unit.Position.X, unit.Position.Y) + eyeHeight, unit.EyePosition.Z, 9);
    }

    [Theory]
    [InlineData(Posture.Standing, 0.45)]
    [InlineData(Posture.Crouching, 0.225)]
    [InlineData(Posture.Prone, 0.045)]
    public void PO2_crouching_and_crawling_slow_the_agent(Posture posture, double metersPerStep)
    {
        // Forest: 0.9 m/s, so 0.45 m per 0.5 s step standing, times the posture's speed factor.
        var sim = Alone(TestMap.Create(widthMeters: 60, heightMeters: 20, fill: TerrainClass.Forest).Build());
        sim.Order("blue", new SetPosture(posture)).RunFor(TimeSpan.FromSeconds(1.5));
        sim.Order("blue", new MoveThroughCorridor(Straight())).Step(2);
        var before = sim.Unit("blue").Position.Xy;

        sim.Step();

        Assert.Equal(metersPerStep, sim.Unit("blue").Position.Xy.DistanceTo(before), 9);
    }

    [Fact]
    public void PO3_changing_posture_takes_one_second_without_moving_or_shooting()
    {
        var sim = Alone(TestMap.Create(widthMeters: 60, heightMeters: 20).Build());
        sim.Order("blue", new MoveThroughCorridor(Straight())).Step(4);
        var before = sim.Unit("blue").Position;

        sim.Order("blue", new SetPosture(Posture.Crouching)).Step(2);

        Assert.Equal(before, sim.Unit("blue").Position);
        sim.Step();
        Assert.True(sim.Unit("blue").Position.Xy.DistanceTo(before.Xy) > 0, "moves again after the second");
        sim.Unit("blue").Should().BeIn(Posture.Crouching);
        Assert.IsType<MoveThroughCorridor>(sim.Unit("blue").CurrentOrder);
    }

    [Fact]
    public void PO4_a_prone_target_behind_a_low_rise_is_hidden_but_a_standing_one_is_not()
    {
        // A 0.5 m rise halfway between them; the observer lies prone (eye 0.3 m).
        var world = TestMap.Create(widthMeters: 100, heightMeters: 20).WithRidge(fromX: 48, toX: 52, height: 0.5).Build();
        var sim = Scenario.ForCombat().WithTerrain(world)
            .WithFighter("blue", new Vec2(11, 11), Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(91, 11), Side.Red, RulesOfEngagement.HoldFire)
            .Build();
        sim.Order("blue", new SetPosture(Posture.Prone)).Order("red", new SetPosture(Posture.Prone)).RunFor(TimeSpan.FromSeconds(1.5));

        Assert.False(sim.Simulation.CanSee("blue", "red"), "prone target: aim point 0.2 m, below the rise");

        sim.Order("red", new SetPosture(Posture.Standing)).RunFor(TimeSpan.FromSeconds(1.5));

        Assert.True(sim.Simulation.CanSee("blue", "red"), "standing target: aim point 1.0 m, above the rise");
    }
}
