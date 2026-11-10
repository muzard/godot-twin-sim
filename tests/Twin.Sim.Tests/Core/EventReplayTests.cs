using Twin.Sim.Events;
using Twin.Sim.Geometry;

namespace Twin.Sim.Tests.Core;

/// <summary>Replay finds the state at a time given as a literal, despite floating-point tick times.</summary>
public class EventReplayTests
{
    [Fact]
    public void Replay_at_a_literal_time_includes_the_tick_at_that_time()
    {
        var tick3 = 3 * (1.0 / 20); // 0.15000000000000002
        SimEvent[] log = [new AgentPlaced(0, "alpha", new Vec3(0, 0, 0)), new AgentMoved(tick3, "alpha", new Vec3(1, 0, 0))];

        var snapshot = EventReplay.SnapshotAt(log, 0.15);

        Assert.Equal(new Vec3(1, 0, 0), snapshot.Agents.Single().Position);
    }

    [Fact]
    public void A_malformed_log_line_is_reported_as_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => EventLog.FromJsonLines("{\"v\":1,\"type\":\"AgentMoved\"}\n"));
    }
}
