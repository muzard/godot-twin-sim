using Godot;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Client;

/// <summary>
/// Draws one moment of a recorded run: the corridor, cliffs, each agent's planned route and the trail it has walked,
/// permission requests, recent shots and the agents themselves. Everything comes from the event log and the scenario;
/// pure presentation.
/// </summary>
public partial class ReplayLayer : Node2D
{
    private const double ShotVisibleSeconds = 1.0;

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

        foreach (var agent in Snapshot.Agents)
        {
            DrawAgent(agent, font);
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

    private void DrawAgent(AgentSnapshot agent, Font font)
    {
        var at = _mapper.ToScreen(agent.Position.Xy);
        var fill = agent.Health switch
        {
            Health.Pinned => new Color(1f, 0.65f, 0.2f),
            Health.Wounded => new Color(0.9f, 0.3f, 0.3f),
            Health.OutOfAction => new Color(0.5f, 0.5f, 0.5f),
            _ => new Color(0.5f, 0.75f, 1f),
        };

        // Placeholder symbol: a friendly-force rectangle (NATO style comes later). Prone agents are drawn flatter.
        var size = agent.Posture switch
        {
            Posture.Prone => new Vector2(20, 8),
            Posture.Crouching => new Vector2(20, 11),
            _ => new Vector2(20, 14),
        };
        var box = new Rect2(at - (size / 2), size);
        DrawRect(box, fill);
        DrawRect(box, agent.Suppressed ? Colors.Yellow : Colors.Black, filled: false, width: agent.Suppressed ? 3f : 1.5f);
        DrawString(font, at + new Vector2(13, 5), $"{agent.Id}  {agent.Activity}  {agent.Position.Z:0} m", fontSize: 13, modulate: Colors.Black);
    }
}
