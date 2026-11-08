using System.Globalization;
using System.Text;
using System.Text.Json;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Units;

namespace Twin.Sim.Events;

/// <summary>
/// The event log as JSON Lines: one event per line, <c>{"v":1,"type":"AgentMoved","time":0.5,"agent":"alpha",...}</c>.
/// Points are arrays <c>[x, y]</c> or <c>[x, y, z]</c> in local meters. Numbers are written in their shortest
/// round-trip form, so the same events always give the same bytes and reading them back gives the same values.
/// </summary>
public static class EventLog
{
    /// <summary>Schema version written in every line.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes events as JSON Lines, each line ending in \n.</summary>
    public static string ToJsonLines(IReadOnlyList<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var text = new StringBuilder();
        using var buffer = new MemoryStream();
        foreach (var e in events)
        {
            buffer.SetLength(0);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                Write(writer, e);
            }

            text.Append(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Reads events written by <see cref="ToJsonLines"/>.</summary>
    /// <exception cref="InvalidDataException">A line is not a known event of this schema version.</exception>
    public static IReadOnlyList<SimEvent> FromJsonLines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var events = new List<SimEvent>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(line.TrimEnd('\r'));
                events.Add(Read(json.RootElement));
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or ArgumentException or FormatException or InvalidOperationException)
            {
                throw new InvalidDataException($"Invalid event log line {events.Count + 1}: {e.Message}", e);
            }
        }

        return events;
    }

    private static void Write(Utf8JsonWriter w, SimEvent e)
    {
        w.WriteStartObject();
        w.WriteNumber("v", SchemaVersion);
        w.WriteString("type", e.GetType().Name);
        w.WriteNumber("time", e.Time);
        w.WriteString("agent", e.AgentId);
        switch (e)
        {
            case AgentPlaced placed:
                WritePoint(w, "position", placed.Position);
                break;
            case AgentMoved moved:
                WritePoint(w, "position", moved.Position);
                break;
            case ActivityChanged changed:
                w.WriteString("activity", changed.Activity.ToString());
                break;
            case PathPlanned planned:
                w.WriteStartArray("waypoints");
                foreach (var p in planned.Waypoints)
                {
                    WritePoint(w, null, p);
                }

                w.WriteEndArray();
                w.WriteNumber("cost", planned.CostSeconds);
                break;
            case PermissionRequested requested:
                w.WriteString("boundary", requested.BoundaryId);
                WritePoint(w, "at", requested.At);
                break;
            case PermissionAnswered answered:
                w.WriteString("boundary", answered.BoundaryId);
                w.WriteString("answer", answered.Answer.ToString());
                break;
            case PermissionTimedOut timedOut:
                w.WriteString("boundary", timedOut.BoundaryId);
                break;
            case Arrived arrived:
                WritePoint(w, "position", arrived.Position);
                break;
            case Blocked blocked:
                w.WriteString("reason", blocked.Reason);
                break;
            case ShotFired shot:
                w.WriteString("target", shot.TargetId);
                w.WriteBoolean("hit", shot.Hit);
                break;
            case ShotHeard heard:
                w.WriteString("shooter", heard.ShooterId);
                WritePoint(w, "shooterPosition", heard.ShooterPosition);
                break;
            case HealthChanged health:
                w.WriteString("health", health.Health.ToString());
                break;
            case CasualtyOccurred casualty:
                WritePoint(w, "position", casualty.Position);
                w.WriteString("health", casualty.Health.ToString());
                break;
            case SuppressionStarted suppression:
                w.WriteNumber("until", suppression.Until);
                break;
            case SuppressionEnded:
                break;
            case PostureChanged posture:
                w.WriteString("posture", posture.Posture.ToString());
                break;
            default:
                throw new ArgumentException($"Unknown event type {e.GetType().Name}.", nameof(e));
        }

        w.WriteEndObject();
    }

    private static SimEvent Read(JsonElement j)
    {
        if (j.GetProperty("v").GetInt32() != SchemaVersion)
        {
            throw new InvalidDataException($"Unsupported event schema version {j.GetProperty("v")}.");
        }

        var time = j.GetProperty("time").GetDouble();
        var agent = j.GetProperty("agent").GetString() ?? string.Empty;
        var type = j.GetProperty("type").GetString();
        return type switch
        {
            nameof(AgentPlaced) => new AgentPlaced(time, agent, Point3(j.GetProperty("position"))),
            nameof(AgentMoved) => new AgentMoved(time, agent, Point3(j.GetProperty("position"))),
            nameof(ActivityChanged) => new ActivityChanged(time, agent, Enum.Parse<UnitActivity>(Text(j, "activity"))),
            nameof(PathPlanned) => new PathPlanned(time, agent,
                j.GetProperty("waypoints").EnumerateArray().Select(Point2).ToList(), j.GetProperty("cost").GetDouble()),
            nameof(PermissionRequested) => new PermissionRequested(time, agent, Text(j, "boundary"), Point2(j.GetProperty("at"))),
            nameof(PermissionAnswered) => new PermissionAnswered(time, agent, Text(j, "boundary"), Enum.Parse<LeaderAnswer>(Text(j, "answer"))),
            nameof(PermissionTimedOut) => new PermissionTimedOut(time, agent, Text(j, "boundary")),
            nameof(Arrived) => new Arrived(time, agent, Point2(j.GetProperty("position"))),
            nameof(Blocked) => new Blocked(time, agent, Text(j, "reason")),
            nameof(ShotFired) => new ShotFired(time, agent, Text(j, "target"), j.GetProperty("hit").GetBoolean()),
            nameof(ShotHeard) => new ShotHeard(time, agent, Text(j, "shooter"), Point2(j.GetProperty("shooterPosition"))),
            nameof(HealthChanged) => new HealthChanged(time, agent, Enum.Parse<Health>(Text(j, "health"))),
            nameof(CasualtyOccurred) => new CasualtyOccurred(time, agent, Point2(j.GetProperty("position")), Enum.Parse<Health>(Text(j, "health"))),
            nameof(SuppressionStarted) => new SuppressionStarted(time, agent, j.GetProperty("until").GetDouble()),
            nameof(SuppressionEnded) => new SuppressionEnded(time, agent),
            nameof(PostureChanged) => new PostureChanged(time, agent, Enum.Parse<Posture>(Text(j, "posture"))),
            _ => throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unknown event type '{type}'.")),
        };
    }

    private static string Text(JsonElement j, string name) => j.GetProperty(name).GetString() ?? string.Empty;

    private static void WritePoint(Utf8JsonWriter w, string? name, Vec2 p)
    {
        if (name is null)
        {
            w.WriteStartArray();
        }
        else
        {
            w.WriteStartArray(name);
        }

        w.WriteNumberValue(p.X);
        w.WriteNumberValue(p.Y);
        w.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter w, string name, Vec3 p)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(p.X);
        w.WriteNumberValue(p.Y);
        w.WriteNumberValue(p.Z);
        w.WriteEndArray();
    }

    private static Vec2 Point2(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble());

    private static Vec3 Point3(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());
}

/// <summary>Rebuilds the simulation state from an event log, as the viewer's replay does.</summary>
public static class EventReplay
{
    /// <summary>Events this close after the requested time still count, so 0.15 s at 20 Hz includes tick 3 (0.15000000000000002 s).</summary>
    public const double TimeTolerance = 1e-9;

    /// <summary>
    /// Every agent's position, activity, health, posture and suppression after all events up to <paramref name="timeSeconds"/>.
    /// </summary>
    public static WorldSnapshot SnapshotAt(IReadOnlyList<SimEvent> events, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(events);
        var agents = new SortedDictionary<string, AgentSnapshot>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e.Time > timeSeconds + TimeTolerance)
            {
                break;
            }

            switch (e)
            {
                case AgentPlaced placed:
                    agents[e.AgentId] = new AgentSnapshot(e.AgentId, placed.Position, UnitActivity.Idle);
                    break;
                case AgentMoved moved when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Position = moved.Position };
                    break;
                case ActivityChanged changed when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Activity = changed.Activity };
                    break;
                case HealthChanged health when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Health = health.Health };
                    break;
                case PostureChanged posture when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Posture = posture.Posture };
                    break;
                case SuppressionStarted when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Suppressed = true };
                    break;
                case SuppressionEnded when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Suppressed = false };
                    break;
                default:
                    break;
            }
        }

        return new WorldSnapshot(timeSeconds, agents.Values.ToList());
    }
}
