using System.Globalization;
using System.Text;
using System.Text.Json;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
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
                if (placed.Side is { } side)
                {
                    w.WriteString("side", side.ToString());
                }

                break;
            case WatchChanged watch:
                WritePoint(w, "direction", watch.Direction);
                w.WriteStartArray("sectors");
                foreach (var sector in watch.Sectors)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(sector.Centre.X);
                    w.WriteNumberValue(sector.Centre.Y);
                    w.WriteNumberValue(sector.WidthDegrees);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteBoolean("air", watch.WatchesAir);
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
                w.WriteString("mode", shot.Mode.ToString());
                w.WriteNumber("rounds", shot.Rounds);
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
                w.WriteBoolean("heavy", suppression.Heavy);
                break;
            case SuppressionEnded:
                break;
            case PostureChanged posture:
                w.WriteString("posture", posture.Posture.ToString());
                break;
            case SquadFormed formed:
                w.WriteString("squad", formed.SquadId);
                WriteStrings(w, "teams", formed.Teams);
                WriteStrings(w, "teamLeaders", formed.TeamLeaderIds);
                w.WriteString("formation", formed.Formation.ToString());
                break;
            case FormationChanged formation:
                w.WriteString("squad", formation.SquadId);
                w.WriteString("formation", formation.Formation.ToString());
                break;
            case OrderRequested requested:
                w.WriteString("squad", requested.SquadId);
                break;
            case OrderRepeated repeated:
                w.WriteString("squad", repeated.SquadId);
                break;
            case EdgeDeployed deployed:
                w.WriteString("squad", deployed.SquadId);
                break;
            case CommandIssued issued:
                w.WriteString("squad", issued.SquadId);
                w.WriteNumber("commandId", issued.CommandId);
                w.WriteString("command", issued.Command);
                break;
            case CommandReceived received:
                w.WriteNumber("commandId", received.CommandId);
                break;
            case LeaderChanged leader:
                w.WriteString("squad", leader.SquadId);
                break;
            case TeamLeaderChanged teamLeader:
                w.WriteString("squad", teamLeader.SquadId);
                w.WriteString("team", teamLeader.Team);
                break;
            case ContactDrillStageChanged stage:
                w.WriteString("stage", stage.Stage.ToString());
                break;
            case ContactReported report:
                w.WriteString("attacker", report.AttackerId);
                WritePoint(w, "attackerPosition", report.AttackerPosition);
                break;
            case EnemySpotted spotted:
                w.WriteString("enemy", spotted.EnemyId);
                WritePoint(w, "enemyPosition", spotted.EnemyPosition);
                break;
            case SpotReported spot:
                w.WriteString("enemy", spot.EnemyId);
                WritePoint(w, "enemyPosition", spot.EnemyPosition);
                w.WriteString("method", spot.Method.ToString());
                break;
            case SpotReceived received:
                w.WriteString("reporter", received.ReporterId);
                w.WriteString("enemy", received.EnemyId);
                WritePoint(w, "enemyPosition", received.EnemyPosition);
                break;
            case SquadHalted halted:
                w.WriteString("squad", halted.SquadId);
                w.WriteString("enemy", halted.EnemyId);
                break;
            case SquadResumed resumed:
                w.WriteString("squad", resumed.SquadId);
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
            nameof(AgentPlaced) => new AgentPlaced(time, agent, Point3(j.GetProperty("position")),
                j.TryGetProperty("side", out var side) ? Enum.Parse<Side>(side.GetString() ?? string.Empty) : null),
            nameof(WatchChanged) => new WatchChanged(time, agent, Point2(j.GetProperty("direction")),
                j.GetProperty("sectors").EnumerateArray()
                    .Select(s => new ObservationSector(new Vec2(s[0].GetDouble(), s[1].GetDouble()), s[2].GetDouble())).ToList(),
                j.GetProperty("air").GetBoolean()),
            nameof(AgentMoved) => new AgentMoved(time, agent, Point3(j.GetProperty("position"))),
            nameof(ActivityChanged) => new ActivityChanged(time, agent, Enum.Parse<UnitActivity>(Text(j, "activity"))),
            nameof(PathPlanned) => new PathPlanned(time, agent,
                j.GetProperty("waypoints").EnumerateArray().Select(Point2).ToList(), j.GetProperty("cost").GetDouble()),
            nameof(PermissionRequested) => new PermissionRequested(time, agent, Text(j, "boundary"), Point2(j.GetProperty("at"))),
            nameof(PermissionAnswered) => new PermissionAnswered(time, agent, Text(j, "boundary"), Enum.Parse<LeaderAnswer>(Text(j, "answer"))),
            nameof(PermissionTimedOut) => new PermissionTimedOut(time, agent, Text(j, "boundary")),
            nameof(Arrived) => new Arrived(time, agent, Point2(j.GetProperty("position"))),
            nameof(Blocked) => new Blocked(time, agent, Text(j, "reason")),
            nameof(ShotFired) => new ShotFired(time, agent, Text(j, "target"), j.GetProperty("hit").GetBoolean(),
                j.TryGetProperty("mode", out var mode) ? Enum.Parse<FireMode>(mode.GetString() ?? string.Empty) : FireMode.Aimed,
                j.TryGetProperty("rounds", out var rounds) ? rounds.GetInt32() : 1),
            nameof(ShotHeard) => new ShotHeard(time, agent, Text(j, "shooter"), Point2(j.GetProperty("shooterPosition"))),
            nameof(HealthChanged) => new HealthChanged(time, agent, Enum.Parse<Health>(Text(j, "health"))),
            nameof(CasualtyOccurred) => new CasualtyOccurred(time, agent, Point2(j.GetProperty("position")), Enum.Parse<Health>(Text(j, "health"))),
            nameof(SuppressionStarted) => new SuppressionStarted(time, agent, j.TryGetProperty("heavy", out var heavy) && heavy.GetBoolean()),
            nameof(SuppressionEnded) => new SuppressionEnded(time, agent),
            nameof(PostureChanged) => new PostureChanged(time, agent, Enum.Parse<Posture>(Text(j, "posture"))),
            nameof(SquadFormed) => new SquadFormed(time, agent, Text(j, "squad"), Strings(j, "teams"), Strings(j, "teamLeaders"),
                j.TryGetProperty("formation", out var formed) ? Enum.Parse<SquadFormation>(formed.GetString() ?? string.Empty) : SquadFormation.File),
            nameof(OrderRequested) => new OrderRequested(time, agent, Text(j, "squad")),
            nameof(OrderRepeated) => new OrderRepeated(time, agent, Text(j, "squad")),
            nameof(EdgeDeployed) => new EdgeDeployed(time, agent, Text(j, "squad")),
            nameof(FormationChanged) => new FormationChanged(time, agent, Text(j, "squad"), Enum.Parse<SquadFormation>(Text(j, "formation"))),
            nameof(CommandIssued) => new CommandIssued(time, agent, Text(j, "squad"), j.GetProperty("commandId").GetInt32(), Text(j, "command")),
            nameof(CommandReceived) => new CommandReceived(time, agent, j.GetProperty("commandId").GetInt32()),
            nameof(LeaderChanged) => new LeaderChanged(time, agent, Text(j, "squad")),
            nameof(TeamLeaderChanged) => new TeamLeaderChanged(time, agent, Text(j, "squad"), Text(j, "team")),
            nameof(ContactDrillStageChanged) => new ContactDrillStageChanged(time, agent, Enum.Parse<ContactDrillStage>(Text(j, "stage"))),
            nameof(ContactReported) => new ContactReported(time, agent, Text(j, "attacker"), Point2(j.GetProperty("attackerPosition"))),
            nameof(EnemySpotted) => new EnemySpotted(time, agent, Text(j, "enemy"), Point2(j.GetProperty("enemyPosition"))),
            nameof(SpotReported) => new SpotReported(time, agent, Text(j, "enemy"), Point2(j.GetProperty("enemyPosition")),
                Enum.Parse<ReportMethod>(Text(j, "method"))),
            nameof(SpotReceived) => new SpotReceived(time, agent, Text(j, "reporter"), Text(j, "enemy"), Point2(j.GetProperty("enemyPosition"))),
            nameof(SquadHalted) => new SquadHalted(time, agent, Text(j, "squad"), Text(j, "enemy")),
            nameof(SquadResumed) => new SquadResumed(time, agent, Text(j, "squad")),
            _ => throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unknown event type '{type}'.")),
        };
    }

    private static string Text(JsonElement j, string name) => j.GetProperty(name).GetString() ?? string.Empty;

    private static List<string> Strings(JsonElement j, string name) =>
        j.GetProperty(name).EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();

    private static void WriteStrings(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartArray(name);
        foreach (var value in values)
        {
            w.WriteStringValue(value);
        }

        w.WriteEndArray();
    }

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
    /// Every agent's position, activity, health, posture, suppression and drill stage, and every squad's leaders,
    /// after all events up to <paramref name="timeSeconds"/>. A leader out of action leaves its place empty until a
    /// successor takes over.
    /// </summary>
    public static WorldSnapshot SnapshotAt(IReadOnlyList<SimEvent> events, double timeSeconds)
    {
        ArgumentNullException.ThrowIfNull(events);
        var agents = new SortedDictionary<string, AgentSnapshot>(StringComparer.Ordinal);
        var squads = new SortedDictionary<string, SquadSnapshot>(StringComparer.Ordinal);
        var teams = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var known = new SortedDictionary<string, SortedDictionary<string, Vec2>>(StringComparer.Ordinal);
        var watches = new SortedDictionary<string, WatchSnapshot>(StringComparer.Ordinal);
        foreach (var e in events)
        {
            if (e.Time > timeSeconds + TimeTolerance)
            {
                break;
            }

            ApplySquadEvent(squads, teams, e);
            switch (e)
            {
                case EnemySpotted spotted:
                    Know(known, e.AgentId, spotted.EnemyId, spotted.EnemyPosition);
                    break;
                case SpotReceived received:
                    Know(known, e.AgentId, received.EnemyId, received.EnemyPosition);
                    break;
                case AgentPlaced placed:
                    agents[e.AgentId] = new AgentSnapshot(e.AgentId, placed.Position, UnitActivity.Idle, Side: placed.Side);
                    watches[e.AgentId] = new WatchSnapshot(e.AgentId, Units.Unit.InitialWatchDirection, [], false);
                    break;
                case WatchChanged watch:
                    watches[e.AgentId] = new WatchSnapshot(e.AgentId, watch.Direction, watch.Sectors, watch.WatchesAir);
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
                case SuppressionStarted started when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Suppressed = true, HeavilySuppressed = started.Heavy };
                    break;
                case SuppressionEnded when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { Suppressed = false, HeavilySuppressed = false };
                    break;
                case ContactDrillStageChanged stage when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with
                    {
                        DrillStage = stage.Stage,
                        AwaitingOrders = stage.Stage != ContactDrillStage.None && agent.AwaitingOrders,
                    };
                    break;
                case ContactReported when agents.TryGetValue(e.AgentId, out var agent):
                    agents[e.AgentId] = agent with { AwaitingOrders = true };
                    break;
                default:
                    break;
            }
        }

        return new WorldSnapshot(timeSeconds, agents.Values.ToList())
        {
            Squads = squads.Values.ToList(),
            KnownEnemies = known.SelectMany(a => a.Value.Select(k => new KnownEnemySnapshot(a.Key, k.Key, k.Value))).ToList(),
            Watches = watches.Values.ToList(),
        };
    }

    private static void Know(SortedDictionary<string, SortedDictionary<string, Vec2>> known, string agentId, string enemyId, Vec2 position)
    {
        if (!known.TryGetValue(agentId, out var enemies))
        {
            enemies = new SortedDictionary<string, Vec2>(StringComparer.Ordinal);
            known.Add(agentId, enemies);
        }

        enemies[enemyId] = position;
    }

    private static void ApplySquadEvent(
        SortedDictionary<string, SquadSnapshot> squads, SortedDictionary<string, List<string>> teams, SimEvent e)
    {
        switch (e)
        {
            case SquadFormed formed:
                squads[formed.SquadId] = new SquadSnapshot(formed.SquadId, formed.AgentId, [.. formed.TeamLeaderIds], formed.Formation);
                teams[formed.SquadId] = [.. formed.Teams];
                break;
            case FormationChanged formation when squads.TryGetValue(formation.SquadId, out var squad):
                squads[squad.Id] = squad with { Formation = formation.Formation };
                break;
            case SquadHalted halted when squads.TryGetValue(halted.SquadId, out var squad):
                squads[squad.Id] = squad with { Halted = true };
                break;
            case SquadResumed resumed when squads.TryGetValue(resumed.SquadId, out var squad):
                squads[squad.Id] = squad with { Halted = false };
                break;
            case LeaderChanged leader when squads.TryGetValue(leader.SquadId, out var squad):
                squads[squad.Id] = squad with { LeaderId = leader.AgentId };
                break;
            case TeamLeaderChanged teamLeader when squads.TryGetValue(teamLeader.SquadId, out var squad):
                var team = teams[squad.Id].IndexOf(teamLeader.Team);
                if (team >= 0)
                {
                    var leaders = squad.TeamLeaderIds.ToArray();
                    leaders[team] = teamLeader.AgentId;
                    squads[squad.Id] = squad with { TeamLeaderIds = leaders };
                }

                break;
            case HealthChanged { Health: Health.OutOfAction } casualty:
                foreach (var id in squads.Keys.ToList())
                {
                    var s = squads[id];
                    squads[id] = s with
                    {
                        LeaderId = s.LeaderId == casualty.AgentId ? null : s.LeaderId,
                        TeamLeaderIds = s.TeamLeaderIds.Select(t => t == casualty.AgentId ? null : t).ToArray(),
                    };
                }

                break;
            default:
                break;
        }
    }
}
