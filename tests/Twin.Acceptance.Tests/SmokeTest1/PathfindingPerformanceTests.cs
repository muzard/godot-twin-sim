using System.Diagnostics;
using System.Globalization;
using Twin.Sim.Movement;
using Twin.Testing;
using Xunit.Abstractions;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// P1: pathfinding time is measured and recorded on the 1 x 1 km (st1) and 2 x 2 km (st1-2km) areas. v0.1 sets no
/// limit, so these tests only fail if the scenario cannot be loaded or planned. See the output with <c>./dev smoke</c>.
/// Wall-clock time is fine here: this is a measurement in a test, not simulation logic.
/// </summary>
public class PathfindingPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("st1")]
    [InlineData("st1-2km")]
    public void P1_pathfinding_time_is_measured_and_recorded(string area)
    {
        var scenario = ScenarioOnDisk.Load(area).Definition;
        var world = scenario.World.World;
        var to = scenario.Corridor.Centerline[^1];
        var respecting = new PathOptions(Corridor: scenario.Corridor);
        var reference = respecting with { IgnoreHardBoundaries = true };
        Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, respecting); // warm-up: JIT, caches

        var respectingTime = Measure(() => Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, respecting), out var respectingPath);
        var referenceTime = Measure(() => Pathfinder.FindPath(world, scenario.Profile, scenario.Start, to, reference), out var referencePath);

        var size = $"{world.Bounds.MaxX - world.Bounds.MinX:0} x {world.Bounds.MaxY - world.Bounds.MinY:0} m";
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"P1 {area} ({size}): respecting hard boundaries {respectingTime.TotalMilliseconds:0.0} ms (found {respectingPath.Found}), " +
            $"reference {referenceTime.TotalMilliseconds:0.0} ms (found {referencePath.Found})"));
        Assert.True(referencePath.Found, "the reference route (hard boundaries ignored) should exist in the scenario");
    }

    private static TimeSpan Measure(Func<PathResult> find, out PathResult result)
    {
        var started = Stopwatch.GetTimestamp();
        result = find();
        return Stopwatch.GetElapsedTime(started);
    }
}
