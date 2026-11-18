using Godot;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.World;
using Side = Twin.Sim.Combat.Side;

namespace Twin.Client;

/// <summary>
/// Draws one moment of a recorded run: the corridor, cliffs, each agent's planned route and the trail it has walked,
/// permission requests, recent shots, observation (watch sectors, fresh sightings and spot reports, where each side
/// believes the enemy is) and the agents themselves as dots in their side's colour. Everything comes from the event log
/// and the scenario; pure presentation.
/// </summary>
public partial class ReplayLayer : Node2D
{
    // As long as the rifle takes between shots (WeaponProfile.Rifle), so a firing agent keeps its line while playing fast.
    private const double ShotVisibleSeconds = 2.0;

    // A fresh sighting or spot report stays on screen this long.
    private const double SightingVisibleSeconds = 4.0;

    // How far a watch sector is drawn from its agent.
    private const double SectorMeters = 25;

    // The voice range a spot report is shown with: the simulation's own rule (backlog 0004 S5a1), not a copy of it.
    private static readonly double VoiceMeters = Twin.Sim.Squads.SquadDoctrine.Default.VoiceRangeMeters;

    private static readonly Color BlueSide = new(0.15f, 0.4f, 1f);
    private static readonly Color RedSide = new(0.9f, 0.12f, 0.12f);
    private static readonly Color NoSide = new(0.35f, 0.35f, 0.35f);

    private readonly IReadOnlyList<SimEvent> _events = [];
    private readonly Corridor? _corridor;
    private readonly RasterWorld _world = null!;
    private readonly CoordinateMapper _mapper = null!;

    /// <summary>Required by Godot. Not used directly.</summary>
    public ReplayLayer()
    {
    }

    /// <summary>Creates the layer for a log recorded on <paramref name="world"/>.</summary>
    public ReplayLayer(IReadOnlyList<SimEvent> events, Corridor? corridor, RasterWorld world, CoordinateMapper mapper)
    {
        _events = events;
        _corridor = corridor;
        _world = world;
        _mapper = mapper;
    }

    /// <summary>The state to draw, rebuilt from the log by <see cref="EventReplay"/>.</summary>
    public WorldSnapshot? Snapshot { get; set; }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Snapshot is null)
        {
            return;
        }

        var time = Snapshot.TimeSeconds + EventReplay.TimeTolerance;
        DrawCorridor();
        DrawCliffs();

        var font = ThemeDB.FallbackFont;
        var routes = new SortedDictionary<string, IReadOnlyList<Vec2>>(StringComparer.Ordinal);
        var trails = new SortedDictionary<string, List<Vector2>>(StringComparer.Ordinal);
        var requests = new List<(string AgentId, string BoundaryId, Vec2 At, string State)>();
        var positions = Snapshot.Agents.ToDictionary(a => a.Id, a => a.Position.Xy, StringComparer.Ordinal);
        var sides = Snapshot.Agents.ToDictionary(a => a.Id, a => a.Side, StringComparer.Ordinal);
        DrawSectors(sides);
        foreach (var e in _events)
        {
            if (e.Time > time)
            {
                break;
            }

            switch (e)
            {
                case PathPlanned path:
                    routes[e.AgentId] = path.Waypoints;
                    break;
                case AgentPlaced placed:
                    trails[e.AgentId] = [_mapper.ToScreen(placed.Position.Xy)];
                    break;
                case AgentMoved moved when trails.TryGetValue(e.AgentId, out var trail):
                    trail.Add(_mapper.ToScreen(moved.Position.Xy));
                    break;
                case PermissionRequested request:
                    requests.Add((request.AgentId, request.BoundaryId, request.At, "?"));
                    break;
                case PermissionAnswered answered:
                    Settle(requests, answered.AgentId, answered.BoundaryId, answered.Answer.ToString());
                    break;
                case PermissionTimedOut timedOut:
                    Settle(requests, timedOut.AgentId, timedOut.BoundaryId, "no answer");
                    break;
                case ShotFired shot when time - e.Time <= ShotVisibleSeconds &&
                                         positions.TryGetValue(shot.AgentId, out var from) &&
                                         positions.TryGetValue(shot.TargetId, out var to):
                    DrawLine(_mapper.ToScreen(from), _mapper.ToScreen(to), shot.Hit ? Colors.Red : new Color(0.3f, 0.3f, 0.3f), 2f);
                    break;
                case EnemySpotted spotted when time - e.Time <= SightingVisibleSeconds && positions.TryGetValue(e.AgentId, out var eye):
                    DrawDashedLine(_mapper.ToScreen(eye), _mapper.ToScreen(spotted.EnemyPosition), SideColor(sides.GetValueOrDefault(e.AgentId)), 1.5f, 6f);
                    break;
                case SpotReported report when time - e.Time <= SightingVisibleSeconds && positions.TryGetValue(e.AgentId, out var speaker):
                    DrawArc(_mapper.ToScreen(speaker), _mapper.ToScreenLength(VoiceMeters), 0, Mathf.Tau, 48,
                        SideColor(sides.GetValueOrDefault(e.AgentId)) with { A = 0.6f }, 1.5f);
                    break;
                case CasualtyOccurred casualty:
                    var c = _mapper.ToScreen(casualty.Position);
                    DrawLine(c - new Vector2(6, 6), c + new Vector2(6, 6), Colors.DarkRed, 2.5f);
                    DrawLine(c - new Vector2(6, -6), c + new Vector2(6, -6), Colors.DarkRed, 2.5f);
                    break;
                default:
                    break;
            }
        }

        foreach (var request in requests)
        {
            var at = _mapper.ToScreen(request.At);
            var color = request.State switch
            {
                "?" => new Color(1f, 0.55f, 0f),
                nameof(LeaderAnswer.Grant) => new Color(0.1f, 0.6f, 0.2f),
                _ => new Color(0.8f, 0.1f, 0.1f),
            };
            DrawCircle(at, 7f, color);
            DrawString(font, at + new Vector2(9, -6), $"permission {request.BoundaryId}: {request.State}", fontSize: 12, modulate: Colors.Black);
        }

        foreach (var (_, route) in routes)
        {
            DrawDashedLine(route, new Color(0.1f, 0.2f, 0.7f, 0.85f));
        }

        foreach (var (_, trail) in trails.Where(t => t.Value.Count > 1))
        {
            DrawPolyline([.. trail], new Color(0.1f, 0.1f, 0.1f, 0.8f), 2f);
        }

        DrawKnownEnemies(sides, font);
        var watches = Snapshot.Watches.ToDictionary(w => w.AgentId, StringComparer.Ordinal);
        foreach (var agent in Snapshot.Agents)
        {
            DrawAgent(agent, watches.GetValueOrDefault(agent.Id), font);
        }

        DrawSquadLeaders(font);
    }

    /// <summary>
    /// Each squad's leader gets a second ring, and the squad's id and formation are written next to it (backlog 0004 S2d):
    /// <c>blue: wedge</c>, or <c>blue: halted</c> while the squad is halted on a spotted enemy.
    /// </summary>
    private void DrawSquadLeaders(Font font)
    {
        var agents = Snapshot!.Agents.ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var squad in Snapshot.Squads)
        {
            if (squad.LeaderId is not { } leaderId || !agents.TryGetValue(leaderId, out var leader))
            {
                continue;
            }

            var at = _mapper.ToScreen(leader.Position.Xy);
            var color = SideColor(leader.Side);
            DrawArc(at, 10f, 0, Mathf.Tau, 32, color, 2f);
            var state = squad.Halted ? "halted" : squad.Formation.ToString().ToLowerInvariant();
            DrawString(font, at + new Vector2(12, -10), $"{squad.Id}: {state}", fontSize: 12, modulate: color);
        }
    }

    private static Color SideColor(Side? side) => side switch
    {
        Side.Blue => BlueSide,
        Side.Red => RedSide,
        _ => NoSide,
    };

    /// <summary>Each agent's watch sectors as translucent wedges in its side's colour.</summary>
    private void DrawSectors(Dictionary<string, Side?> sides)
    {
        var radius = _mapper.ToScreenLength(SectorMeters);
        var agents = Snapshot!.Agents.ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var watch in Snapshot.Watches)
        {
            if (!agents.TryGetValue(watch.AgentId, out var agent) || agent.Health == Health.OutOfAction)
            {
                continue;
            }

            var at = _mapper.ToScreen(agent.Position.Xy);
            var color = SideColor(sides.GetValueOrDefault(watch.AgentId)) with { A = 0.13f };
            foreach (var sector in watch.Sectors)
            {
                var centre = ScreenAngle(at, agent.Position.Xy, sector.Centre);
                var half = Mathf.DegToRad((float)sector.WidthDegrees / 2);
                var points = new List<Vector2> { at };
                for (var i = 0; i <= 16; i++)
                {
                    var angle = centre - half + (2 * half * i / 16);
                    points.Add(at + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
                }

                DrawColoredPolygon([.. points], color);
            }
        }
    }

    /// <summary>The screen angle of a map direction seen from an agent (the map's Y axis points the other way on screen).</summary>
    private float ScreenAngle(Vector2 at, Vec2 position, Vec2 direction)
    {
        var ahead = _mapper.ToScreen(position + direction) - at;
        return Mathf.Atan2(ahead.Y, ahead.X);
    }

    /// <summary>Where each side believes an enemy is: a hollow diamond in the believing side's colour, with the enemy's id.</summary>
    private void DrawKnownEnemies(Dictionary<string, Side?> sides, Font font)
    {
        var believed = new SortedDictionary<string, (Vec2 Position, Side? Side)>(StringComparer.Ordinal);
        foreach (var known in Snapshot!.KnownEnemies)
        {
            var side = sides.GetValueOrDefault(known.AgentId);
            believed[$"{side}:{known.EnemyId}"] = (known.Position, side);
        }

        foreach (var (key, (position, side)) in believed)
        {
            var at = _mapper.ToScreen(position);
            var color = SideColor(side);
            Vector2[] diamond = [at + new Vector2(0, -9), at + new Vector2(9, 0), at + new Vector2(0, 9), at + new Vector2(-9, 0), at + new Vector2(0, -9)];
            DrawPolyline(diamond, color, 1.5f);
            DrawString(font, at + new Vector2(10, 14), $"? {key[(key.IndexOf(':') + 1)..]}", fontSize: 10, modulate: color);
        }
    }

    private static void Settle(List<(string AgentId, string BoundaryId, Vec2 At, string State)> requests, string agentId, string boundaryId, string state)
    {
        var index = requests.FindLastIndex(r => r.AgentId == agentId && r.BoundaryId == boundaryId && r.State == "?");
        if (index >= 0)
        {
            requests[index] = requests[index] with { State = state };
        }
    }

    private void DrawCorridor()
    {
        if (_corridor is null || _corridor.Centerline.Count < 2)
        {
            return;
        }

        Vector2[] line = [.. _corridor.Centerline.Select(_mapper.ToScreen)];
        DrawPolyline(line, new Color(1f, 0.85f, 0.2f, 0.18f), _mapper.ToScreenLength(2 * _corridor.OuterLimit));
        DrawPolyline(line, new Color(1f, 0.85f, 0.2f, 0.25f), _mapper.ToScreenLength(_corridor.Width));
        DrawDashedLine(_corridor.Centerline, new Color(0.6f, 0.45f, 0f, 0.9f));

        foreach (var boundary in _corridor.HardBoundaries.Where(b => b.Line.Count > 1))
        {
            DrawPolyline([.. boundary.Line.Select(_mapper.ToScreen)], Colors.Red, 3f);
            DrawString(ThemeDB.FallbackFont, _mapper.ToScreen(boundary.Line[0]) + new Vector2(4, -4), boundary.Id, fontSize: 12, modulate: Colors.DarkRed);
        }
    }

    private void DrawCliffs()
    {
        foreach (var cliff in _world.Cliffs.Where(c => c.Count > 1))
        {
            DrawPolyline([.. cliff.Select(_mapper.ToScreen)], new Color(0.35f, 0.18f, 0.08f), 2f);
        }
    }

    private void DrawDashedLine(IReadOnlyList<Vec2> points, Color color)
    {
        for (var i = 1; i < points.Count; i++)
        {
            DrawDashedLine(_mapper.ToScreen(points[i - 1]), _mapper.ToScreen(points[i]), color, 2f, 8f);
        }
    }

    /// <summary>
    /// An agent as a dot in its side's colour: smaller the lower its posture, ringed orange when pinned, red when wounded
    /// and yellow when suppressed; grey with a cross when out of action. A short line shows where it watches, and a small
    /// mark above it that it watches the air.
    /// </summary>
    private void DrawAgent(AgentSnapshot agent, WatchSnapshot? watch, Font font)
    {
        var at = _mapper.ToScreen(agent.Position.Xy);
        var radius = agent.Posture switch
        {
            Posture.Prone => 4f,
            Posture.Crouching => 5f,
            _ => 6f,
        };
        if (agent.Health == Health.OutOfAction)
        {
            DrawCircle(at, radius, NoSide);
            DrawLine(at - new Vector2(radius, radius), at + new Vector2(radius, radius), Colors.Black, 1.5f);
            DrawLine(at - new Vector2(radius, -radius), at + new Vector2(radius, -radius), Colors.Black, 1.5f);
            DrawString(font, at + new Vector2(radius + 3, 4), agent.Id, fontSize: 11, modulate: NoSide);
            return;
        }

        var color = SideColor(agent.Side);
        if (watch is not null)
        {
            var ahead = _mapper.ToScreen(agent.Position.Xy + (watch.Direction * (SectorMeters * 0.6))) - at;
            DrawLine(at, at + ahead, color, 1.5f);
            if (watch.WatchesAir)
            {
                DrawPolyline([at + new Vector2(-4, -radius - 3), at + new Vector2(0, -radius - 8), at + new Vector2(4, -radius - 3)], color, 1.5f);
            }
        }

        DrawCircle(at, radius, color);
        var ring = agent.Suppressed ? Colors.Yellow : agent.Health switch
        {
            Health.Pinned => new Color(1f, 0.6f, 0.1f),
            Health.Wounded => new Color(1f, 0.2f, 0.2f),
            _ => Colors.Black,
        };
        DrawArc(at, radius + 1, 0, Mathf.Tau, 24, ring, agent.Suppressed || agent.Health != Health.Unhurt ? 2.5f : 1f);
        DrawString(font, at + new Vector2(radius + 3, 4), agent.Id, fontSize: 11, modulate: Colors.Black);
    }
}
