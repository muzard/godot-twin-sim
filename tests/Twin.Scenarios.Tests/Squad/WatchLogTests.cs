using Twin.Sim.Combat;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// What a replay viewer needs to show observation and sides (for the ambush demo, backlog 0004): each agent's side is in
/// its <see cref="AgentPlaced"/>, and its watch direction, observation sectors and air watch are logged when they change
/// (<see cref="WatchChanged"/>), so <see cref="EventReplay"/> rebuilds them at every step from the log alone.
/// </summary>
public class WatchLogTests
{
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(300, 101);

    // The reference squad walks east in a wedge and turns to a file on the way; red stands well off the route.
    private static ScenarioRun SquadOnTheMove() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 400, heightMeters: 200).Build())
        .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, Start)
        .WithFighter("red", new Vec2(390, 190), Side.Red, RulesOfEngagement.HoldFire)
        .Build()
        .Order("rj", new MoveThroughCorridor(new Corridor([Start, B, new Vec2(300, 30)], 80, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: [])));

    private static bool Same(WatchSnapshot a, WatchSnapshot b) =>
        a.AgentId == b.AgentId && a.Direction == b.Direction && a.WatchesAir == b.WatchesAir &&
        a.Sectors.SequenceEqual(b.Sectors);

    [Fact]
    public void Watch_directions_and_sectors_rebuilt_from_the_log_match_the_live_state_at_every_step()
    {
        var sim = SquadOnTheMove();
        var live = new List<WorldSnapshot> { sim.Simulation.Snapshot() };
        do // the order takes effect on the first step, so the squad counts as settled before it
        {
            sim.Step();
            live.Add(sim.Simulation.Snapshot());
            Assert.True(sim.TimeSeconds < 900, "the squad should settle within 15 minutes");
        }
        while (!sim.SquadSettled("r1"));

        var log = EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events));
        Assert.Contains(log, e => e is WatchChanged { Sectors.Count: > 0 });
        foreach (var expected in live)
        {
            var replayed = EventReplay.SnapshotAt(log, expected.TimeSeconds);
            Assert.Equal(expected.Watches.Count, replayed.Watches.Count);
            Assert.True(expected.Watches.Zip(replayed.Watches).All(p => Same(p.First, p.Second)),
                $"At t={expected.TimeSeconds} s the watch directions or sectors differ.");
        }
    }

    [Fact]
    public void An_agents_side_is_logged_when_it_is_placed_and_rebuilt_by_the_replay()
    {
        var sim = SquadOnTheMove();

        var replayed = EventReplay.SnapshotAt(EventLog.FromJsonLines(EventLog.ToJsonLines(sim.Events)), 0);

        Assert.Equal(Side.Red, replayed.Agents.Single(a => a.Id == "red").Side);
        Assert.Equal(Side.Blue, replayed.Agents.Single(a => a.Id == "rj").Side);
        Assert.True(sim.Simulation.Snapshot().Agents.SequenceEqual(replayed.Agents), "live and replayed agents differ");
    }

    [Fact]
    public void Watch_events_survive_a_round_trip_through_the_log()
    {
        var sim = SquadOnTheMove().RunFor(TimeSpan.FromSeconds(120));
        var text = EventLog.ToJsonLines(sim.Events);

        Assert.Contains(nameof(WatchChanged), text, StringComparison.Ordinal);
        Assert.Equal(text, EventLog.ToJsonLines(EventLog.FromJsonLines(text)));
    }
}
