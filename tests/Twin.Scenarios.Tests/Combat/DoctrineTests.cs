using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Combat;

/// <summary>
/// Doctrine: rules decided by people, kept as data (<see cref="Doctrine"/> in <see cref="CombatRules"/> and the
/// <c>doctrine</c> section of scenario.json) instead of being written into the code. Decided 2026-11-09:
/// <list type="bullet">
/// <item>The duty to return fire beats a <see cref="FireAt"/> order whose target cannot be engaged.</item>
/// <item>A permission answer that arrives while the agent is pinned or suppressed is handled when it is free again.</item>
/// </list>
/// Each test also runs the other setting, to show that the behaviour follows the data.
/// </summary>
public class DoctrineTests
{
    private static CombatRules With(Doctrine doctrine) => SpecShooting.Rules with { Doctrine = doctrine };

    // Blue is ordered to fire at red-hidden, behind a 20 m ridge. Red, in plain sight, fires one round at blue.
    private static ScenarioRun OrderedAtHiddenTargetAndFiredAt(Doctrine doctrine)
    {
        var world = TestMap.Create(widthMeters: 300, heightMeters: 40).WithRidge(fromX: 150, toX: 160, height: 20).Build();
        return Scenario.ForCombat().WithCombatRules(With(doctrine)).WithTerrain(world)
            .WithFighter("blue", new Vec2(21, 21), Side.Blue, RulesOfEngagement.ReturnFire)
            .WithFighter("red", new Vec2(81, 21), Side.Red, RulesOfEngagement.HoldFire, SpecShooting.Rifle with { Rounds = 1 })
            .WithFighter("red-hidden", new Vec2(221, 21), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new FireAt("red-hidden"))
            .Order("red", new FireAt("blue"));
    }

    [Fact]
    public void By_default_the_duty_to_return_fire_beats_a_fire_at_order_on_a_hidden_target()
    {
        Assert.True(Doctrine.Default.ReturnFireOverridesFireAt);
        var sim = OrderedAtHiddenTargetAndFiredAt(Doctrine.Default);

        sim.RunUntil(s => s.Events.OfType<ShotFired>().Any(e => e.AgentId == "blue"), TimeSpan.FromMinutes(3), "blue returns fire");

        Assert.Equal("red", sim.Events.OfType<ShotFired>().First(e => e.AgentId == "blue").TargetId);
        Assert.IsType<FireAt>(sim.Unit("blue").CurrentOrder);
    }

    [Fact]
    public void With_the_duty_turned_off_a_fire_at_order_keeps_the_agent_on_its_ordered_target()
    {
        var sim = OrderedAtHiddenTargetAndFiredAt(Doctrine.Default with { ReturnFireOverridesFireAt = false });

        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.Contains(sim.Events.OfType<ShotFired>(), e => e.AgentId == "red");
        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.AgentId == "blue");
    }

    // Blue must cross a road it may not cross without permission (no detour), so it asks at once. Red, 500 m away,
    // keeps blue suppressed for 40 s; the leader answers after 20 s.
    private static ScenarioRun AskingWhileSuppressed(Doctrine doctrine)
    {
        var a = new Vec2(51, 31);
        var road = new HardBoundary("road", [new(0, 50), new(600, 50)], BoundarySource.Order);
        var corridor = new Corridor([a, new Vec2(51, 71)], Width: 60, SoftMargin: 25, SoftPenalty: 1, HardBoundaries: [road]);
        var sim = Scenario.ForCombat().WithCombatRules(With(doctrine))
            .WithTerrain(TestMap.Create(widthMeters: 600, heightMeters: 100).Build())
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(20), permitRadiusMeters: 10)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(120))
            .WithFighter("blue", a, Side.Blue, RulesOfEngagement.HoldFire)
            .WithFighter("red", new Vec2(551, 31), Side.Red, RulesOfEngagement.HoldFire)
            .Build()
            .Order("blue", new MoveThroughCorridor(corridor))
            .Order("red", new FireAt("blue"));
        sim.RunFor(TimeSpan.FromSeconds(40));
        sim.Order("red", new CeaseFire());
        return sim;
    }

    [Fact]
    public void By_default_a_permission_answer_that_arrives_while_suppressed_is_handled_when_the_agent_is_free()
    {
        Assert.Equal(PermissionAnswerTiming.WhenFree, Doctrine.Default.PermissionAnswers);
        var sim = AskingWhileSuppressed(Doctrine.Default);
        var lastShot = sim.Events.OfType<ShotFired>().Last().Time;

        sim.RunUntil(s => s.Events.OfType<PermissionAnswered>().Any(), TimeSpan.FromMinutes(2), "answer handled");

        var request = sim.Events.OfType<PermissionRequested>().First();
        Assert.True(request.Time + 20 < lastShot, "precondition: the leader answered while blue was suppressed");
        var answer = sim.Events.OfType<PermissionAnswered>().First();
        Assert.True(answer.Time >= lastShot + SpecShooting.Rules.SuppressionSeconds - 1e-9,
            $"answer handled at t={answer.Time}, before the suppression ended (last shot t={lastShot})");
    }

    [Fact]
    public void With_immediate_answers_the_answer_is_logged_on_time_but_the_agent_still_waits_until_it_is_free()
    {
        var sim = AskingWhileSuppressed(Doctrine.Default with { PermissionAnswers = PermissionAnswerTiming.Immediately });

        var request = sim.Events.OfType<PermissionRequested>().First();
        var answer = Assert.Single(sim.Events.OfType<PermissionAnswered>());
        Assert.Equal(request.Time + 20, answer.Time, 9);
        sim.Unit("blue").Should().BeSuppressed();
        Assert.Equal(new Vec2(51, 31), sim.Unit("blue").Position.Xy);

        sim.RunUntil(s => s.Unit("blue").IsIdle, TimeSpan.FromMinutes(5), "blue arrives");
        sim.Unit("blue").Should().BeAt(new Vec2(51, 71));
    }
}
