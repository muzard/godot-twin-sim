using Twin.Sim.Events;

namespace Twin.Sim.Tests.Core;

/// <summary>The firing-position events of backlog 0005 survive the JSON-lines event log.</summary>
public class FiringPositionEventLogTests
{
    [Fact]
    public void Firing_position_events_round_trip_through_the_event_log()
    {
        SimEvent[] log =
        [
            new CannotFireReported(4, "k1", "pj"),
            new TeamMoveRequested(9, "pj", "point"),
            new TeamMoveAnswered(12, "rj", "point", Granted: false),
            new TeamMoveAnswered(42, "rj", "point", Granted: true),
        ];

        var back = EventLog.FromJsonLines(EventLog.ToJsonLines(log));

        Assert.Equal(log, back);
    }
}
