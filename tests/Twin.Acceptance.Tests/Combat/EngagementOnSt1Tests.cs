using System.Diagnostics;
using System.Globalization;
using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.World;
using Twin.Testing;
using Xunit.Abstractions;

namespace Twin.Acceptance.Tests.Combat;

/// <summary>
/// Shooting on the real MML area <c>scenarios/st1</c> (backlog 0002, K). The points were chosen from the elevation
/// model: K1 has a hill about 20 m above the sight line between the two agents, and K2 is a clear line over a field.
/// </summary>
public class EngagementOnSt1Tests(ITestOutputHelper output)
{
    private static readonly LoadedWorld St1 = ScenarioOnDisk.Load("st1").Definition.World;

    private static Vec2 Etrs(double easting, double northing) => St1.Origin.ToLocal(new EtrsPoint(easting, northing));

    [Fact]
    public void K1_agents_on_opposite_sides_of_a_hill_on_st1_cannot_see_each_other_but_hear_the_shots()
    {
        var red = Etrs(404349, 6687501);
        var sim = Scenario.ForCombat().WithTerrain(St1.World)
            .WithFighter("blue", Etrs(404125, 6687445), Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("red", red, Side.Red, RulesOfEngagement.FreeFire)
            .WithFighter("bravo", red + new Vec2(0, 10), Side.Blue, RulesOfEngagement.HoldFire)
            .Build();

        Assert.False(sim.Simulation.CanSee("blue", "red"));
        Assert.False(sim.Simulation.CanSee("red", "blue"));

        sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.DoesNotContain(sim.Events.OfType<ShotFired>(), e => e.AgentId == "blue" || e.TargetId == "blue");
        Assert.Contains(sim.Events.OfType<ShotHeard>(), e => e.AgentId == "blue" && e.ShooterId == "red");
    }

    [Fact]
    public void K2_across_a_field_on_st1_they_see_and_engage_each_other()
    {
        var sim = Scenario.ForCombat().WithTerrain(St1.World)
            .WithFighter("blue", Etrs(403871, 6687191), Side.Blue, RulesOfEngagement.FreeFire)
            .WithFighter("red", Etrs(404009, 6687083), Side.Red, RulesOfEngagement.FreeFire)
            .Build();
        Assert.True(sim.Simulation.CanSee("blue", "red"));

        // Every shot must be fired with line of sight, at the start or the end of the step in which it was fired.
        var seenBefore = Visibility(sim.Simulation);
        var checkedEvents = 0;
        var violations = new List<string>();
        sim.RunUntil(s =>
        {
            var seenAfter = Visibility(s.Simulation);
            foreach (var shot in s.Events.Skip(checkedEvents).OfType<ShotFired>())
            {
                var pair = (shot.AgentId, shot.TargetId);
                if (!seenBefore.Contains(pair) && !seenAfter.Contains(pair))
                {
                    violations.Add($"{shot.AgentId} shot {shot.TargetId} at t={shot.Time} without line of sight");
                }
            }

            checkedEvents = s.Events.Count;
            seenBefore = seenAfter;
            return s.Simulation.Units.Any(u => u.Health == Health.OutOfAction);
        }, TimeSpan.FromMinutes(10), "one side out of action");

        Assert.NotEmpty(sim.Events.OfType<ShotFired>());
        Assert.NotEmpty(sim.Events.OfType<HealthChanged>());
        Assert.Empty(violations);
    }

    [Fact]
    public void K3_line_of_sight_query_time_is_measured()
    {
        const int Queries = 10_000;
        var random = new DeterministicRandom(seed: 1);
        var world = St1.World;
        Vec3 RandomPoint(double height)
        {
            var p = new Vec2(random.NextDouble() * world.Bounds.MaxX, random.NextDouble() * world.Bounds.MaxY);
            return p.WithZ(world.GroundHeight(p.X, p.Y) + height);
        }

        var pairs = Enumerable.Range(0, Queries).Select(_ => (RandomPoint(1.6), RandomPoint(1.0))).ToList();
        var started = Stopwatch.GetTimestamp();
        var visible = pairs.Count(pair => LineOfSight.Between(world, pair.Item1, pair.Item2, SpecShooting.Rules).IsVisible(SpecShooting.Rules));
        var elapsed = Stopwatch.GetElapsedTime(started);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"K3 st1: {Queries} line-of-sight queries in {elapsed.TotalMilliseconds:0.0} ms, {visible} visible"));
        Assert.InRange(visible, 1, Queries - 1);
    }

    private static SortedSet<(string, string)> Visibility(Simulation simulation)
    {
        var seen = new SortedSet<(string, string)>();
        foreach (var a in simulation.Units)
        {
            foreach (var b in simulation.Units)
            {
                if (a.Id != b.Id && simulation.CanSee(a.Id, b.Id))
                {
                    seen.Add((a.Id, b.Id));
                }
            }
        }

        return seen;
    }
}
