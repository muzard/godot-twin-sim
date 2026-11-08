using Twin.Sim.Command;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// Smoke test I on the real MML area in <c>scenarios/st1</c>: one agent goes from A (in forest) to B about 800 m away,
/// through a corridor, past buildings, water, a steep slope, bog and a field whose forest edge is a hard boundary.
/// The scenario is built so that at least one hard boundary makes the detour long enough to ask for permission.
/// Run in three variants: the leader grants, denies, or does not answer. A1–A4.
/// </summary>
public class SmokeTest1Tests
{
    private const string Area = "st1";
    private static readonly TimeSpan Generous = TimeSpan.FromHours(2);

    private static bool Finished(ScenarioRun s, string agentId) =>
        s.Unit(agentId).IsIdle || s.Unit(agentId).Activity == UnitActivity.Blocked;

    private static ScenarioRun RunToEnd(ScenarioOnDisk scenario, LeaderAnswer answer)
    {
        var id = scenario.Definition.AgentId;
        return scenario.Start(answer).RunUntil(s => Finished(s, id), Generous, $"{id} arrived or blocked");
    }

    private static void AssertNoViolations(ScenarioOnDisk scenario, ScenarioRun sim)
    {
        var definition = scenario.Definition;
        var permits = RouteAudit.GrantedPermits(sim.Events, definition.Leader.PermitRadiusMeters);
        var world = definition.World.World;
        var violations = RouteAudit.Violations(world, definition.Corridor, sim.Trail(definition.AgentId), permits).ToList();
        foreach (var planned in sim.Events.OfType<PathPlanned>())
        {
            violations.AddRange(RouteAudit.Violations(world, definition.Corridor, planned.Waypoints, permits)
                .Select(v => $"path planned at t={planned.Time}: {v}"));
        }

        Assert.True(violations.Count == 0, "Route violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void A1_with_permission_the_agent_reaches_B_after_asking_and_without_violations()
    {
        var scenario = ScenarioOnDisk.Load(Area);

        var sim = RunToEnd(scenario, LeaderAnswer.Grant);

        var id = scenario.Definition.AgentId;
        sim.Unit(id).Should().BeAt(scenario.Definition.Corridor.Centerline[^1]).BeIdle();
        Assert.Contains(sim.Events.OfType<Arrived>(), e => e.AgentId == id);
        Assert.NotEmpty(sim.Events.OfType<PermissionRequested>());
        Assert.Contains(sim.Events.OfType<PermissionAnswered>(), e => e.Answer == LeaderAnswer.Grant);
        AssertNoViolations(scenario, sim);
    }

    [Theory]
    [InlineData(LeaderAnswer.Deny)]
    [InlineData(LeaderAnswer.NoAnswer)]
    public void A2_without_permission_the_agent_detours_or_reports_blocked_as_the_area_allows(LeaderAnswer answer)
    {
        var scenario = ScenarioOnDisk.Load(Area);
        var detourExists = scenario.DetourWithoutCrossing
            ?? throw new ScenarioAssertionException("scenarios/st1/scenario.json must set expect.detourWithoutCrossing.");

        var sim = RunToEnd(scenario, answer);

        var id = scenario.Definition.AgentId;
        Assert.NotEmpty(sim.Events.OfType<PermissionRequested>());
        if (detourExists)
        {
            sim.Unit(id).Should().BeAt(scenario.Definition.Corridor.Centerline[^1]).BeIdle();
        }
        else
        {
            Assert.Equal(UnitActivity.Blocked, sim.Unit(id).Activity);
            Assert.Contains(sim.Events.OfType<Blocked>(), e => e.AgentId == id);
        }

        AssertNoViolations(scenario, sim);
    }

    [Fact]
    public void A3_two_runs_with_the_same_seed_produce_byte_identical_event_logs()
    {
        var scenario = ScenarioOnDisk.Load(Area);

        var first = EventLog.ToJsonLines(RunToEnd(scenario, LeaderAnswer.Grant).Events);
        var second = EventLog.ToJsonLines(RunToEnd(ScenarioOnDisk.Load(Area), LeaderAnswer.Grant).Events);

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void A4_state_rebuilt_from_the_event_log_matches_the_live_state_at_every_step()
    {
        var scenario = ScenarioOnDisk.Load(Area);
        var id = scenario.Definition.AgentId;
        var sim = scenario.Start(LeaderAnswer.Grant);
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };

        sim.RunUntil(s =>
        {
            live.Add(s.Simulation.Snapshot());
            return Finished(s, id);
        }, Generous, $"{id} arrived or blocked");

        // Replay works from the written log, as the viewer does.
        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.Equal(expected.TimeSeconds, replayed.TimeSeconds);
            Assert.True(expected.Agents.SequenceEqual(replayed.Agents),
                $"At t={expected.TimeSeconds} s live state {Describe(expected)} but replayed {Describe(replayed)}");
        }
    }

    private static string Describe(WorldSnapshot snapshot) =>
        string.Join("; ", snapshot.Agents.Select(a => $"{a.Id} {a.Activity} at {a.Position}"));
}
