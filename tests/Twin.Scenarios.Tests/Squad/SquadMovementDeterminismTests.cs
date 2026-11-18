using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Determinism and the event log with squad movement (backlog 0004, S2a, as SQ6 for S1). The run: the squad moves off
/// in file, changes to a wedge on the way and arrives. The same seed gives the same log byte for byte, and the squads'
/// formations, leaders and team leaders, and the agents, can be rebuilt from the log at every step.
/// </summary>
public class SquadMovementDeterminismTests
{
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(300, 101);

    private static ScenarioRun SquadOnTheMove(ulong seed) => Scenario.ForSquad()
        .WithSeed(seed)
        .WithTerrain(TestMap.Create(widthMeters: 400, heightMeters: 200).Build())
        .WithSquad(SpecSquad.Reference(), Start)
        .Build()
        .Order("rj", new MoveThroughCorridor(new Corridor([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));

    private static ScenarioRun Run(ScenarioRun sim)
    {
        sim.RunUntil(s => s.Unit("rj").Position.X > 150, TimeSpan.FromMinutes(5), "rj past x = 150");
        sim.Command("rj", new SetSquadFormation(SquadFormation.Wedge));
        return sim.RunUntilSquadSettled("r1", TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void SQ6_a_squad_moving_with_the_same_seed_gives_byte_identical_logs()
    {
        var first = EventLog.ToJsonLines(Run(SquadOnTheMove(seed: 1)).Events);
        var second = EventLog.ToJsonLines(Run(SquadOnTheMove(seed: 1)).Events);

        Assert.Contains("FormationChanged", first, StringComparison.Ordinal);
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(first), System.Text.Encoding.UTF8.GetBytes(second));
    }

    [Fact]
    public void SQ6_formation_events_survive_a_round_trip_through_the_log()
    {
        var text = EventLog.ToJsonLines(Run(SquadOnTheMove(seed: 1)).Events);

        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }

    [Fact]
    public void SQ6_squads_and_agents_rebuilt_from_the_log_match_the_live_state_while_moving()
    {
        var sim = SquadOnTheMove(seed: 1);
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };
        var commanded = false;
        while (!sim.SquadSettled("r1") || !commanded)
        {
            if (!commanded && sim.Unit("rj").Position.X > 150)
            {
                sim.Order("rj", new SetSquadFormation(SquadFormation.Wedge));
                commanded = true;
            }

            sim.Step();
            live.Add(sim.Simulation.Snapshot());
            Assert.True(sim.TimeSeconds < 600, "the squad should settle within 10 minutes");
        }

        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.True(expected.Agents.SequenceEqual(replayed.Agents), $"At t={expected.TimeSeconds} s the agents differ.");
            Assert.True(SameSquads(expected, replayed), $"At t={expected.TimeSeconds} s the squads differ.");
        }

        Assert.Equal(SquadFormation.Wedge, live[^1].Squads.Single().Formation);
        Assert.Equal(SquadFormation.File, live[0].Squads.Single().Formation);
    }

    private static bool SameSquads(WorldSnapshot a, WorldSnapshot b) =>
        a.Squads.Count == b.Squads.Count && a.Squads.Zip(b.Squads).All(p =>
            p.First.Id == p.Second.Id && p.First.LeaderId == p.Second.LeaderId && p.First.Formation == p.Second.Formation &&
            p.First.TeamLeaderIds.SequenceEqual(p.Second.TeamLeaderIds));
}
