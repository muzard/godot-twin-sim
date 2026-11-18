using System.Globalization;
using System.Text;
using Godot;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Scenarios;
using Twin.Sim.World;

namespace Twin.Client;

/// <summary>
/// Plays back a recorded event log on the scenario's map (scene <c>Replay.tscn</c>). The state at any moment is rebuilt
/// from the log with <see cref="EventReplay"/>, so the viewer shows exactly what was recorded and runs no simulation.
/// Start it with <c>./dev replay st1 --godot</c>, or pass <c>-- --scenario &lt;dir&gt; --log &lt;file.jsonl&gt;</c> to Godot.
/// </summary>
public partial class ReplayHost : Node2D
{
    private const string Help =
        "space: play/pause   left/right: -/+1 s (shift: 10 s)   up/down: speed   home/end: start/end   wheel: zoom   drag: pan";

    private const int EventLines = 14;
    private const float MinZoom = 0.05f;
    private const double MinFocusMeters = 300;
    private const float MaxZoom = 40f;

    private IReadOnlyList<SimEvent> _events = [];
    private CoordinateMapper _mapper = null!;
    private ReplayLayer _layer = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _log = null!;
    private HSlider _slider = null!;
    private double _time;
    private double _end;
    private double _speed = 10;
    private bool _playing = true;
    private bool _updatingSlider;
    private Rect2 _focus;

    /// <inheritdoc />
    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color(0.9f, 0.9f, 0.88f));
        var hud = new CanvasLayer();
        AddChild(hud);
        _status = HudLabel(hud, new Vector2(12, 8));

        if (!TryLoad(out var world, out var corridor, out var title, out var error))
        {
            _status.Text = $"{error}\n\nUsage: ./dev replay <scenario> --godot\n   or: godot --path src/Twin.Client res://Replay.tscn -- --scenario <dir> --log <file.jsonl>";
            return;
        }

        GetWindow().Title = $"Twin replay: {title}";
        _end = _events.Count > 0 ? _events[^1].Time : 0;

        // One pixel per meter; the camera zoom fits the map to the window (LayoutHud).
        _mapper = new CoordinateMapper(world.WidthMeters, world.DepthMeters, pixelsPerMeter: 1f);
        AddChild(new MapView(world, _mapper));
        _layer = new ReplayLayer(_events, corridor, world, _mapper);
        AddChild(_layer);

        _focus = Focus(world, corridor);
        _camera = new Camera2D { Position = _focus.GetCenter() };
        AddChild(_camera);

        _log = HudLabel(hud, Vector2.Zero);
        _log.HorizontalAlignment = HorizontalAlignment.Right;
        // No focus: a focused slider would take the arrow keys and home/end from the playback controls.
        _slider = new HSlider { MinValue = 0, MaxValue = Math.Max(_end, 0.001), Step = 0, FocusMode = Control.FocusModeEnum.None };
        _slider.ValueChanged += value =>
        {
            if (!_updatingSlider)
            {
                _time = value;
            }
        };
        hud.AddChild(_slider);
        GetViewport().SizeChanged += LayoutHud;
        LayoutHud();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_layer is null)
        {
            return;
        }

        if (_playing)
        {
            _time = Math.Min(_end, _time + (delta * _speed));
            _playing = _time < _end;
        }

        var snapshot = EventReplay.SnapshotAt(_events, _time);
        _layer.Snapshot = snapshot;
        _layer.QueueRedraw();

        _updatingSlider = true;
        _slider.Value = _time;
        _updatingSlider = false;

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"t = {_time:0.0} / {_end:0.0} s   {(_playing ? "playing" : "paused")} x{_speed:0.##}\n");
        foreach (var a in snapshot.Agents)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{a.Id}: {a.Activity}, {a.Health}, {a.Posture}{(a.Suppressed ? ", suppressed" : string.Empty)}  ({a.Position.X:0}, {a.Position.Y:0}, {a.Position.Z:0.0} m)\n");
        }

        text.Append(Help);
        _status.Text = text.ToString();
        _log.Text = RecentEvents();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_layer is null)
        {
            return;
        }

        switch (@event)
        {
            case InputEventKey { Pressed: true } key:
                var step = key.ShiftPressed ? 10 : 1;
                switch (key.Keycode)
                {
                    case Key.Space:
                        _playing = !_playing;
                        if (_playing && _time >= _end)
                        {
                            _time = 0;
                        }

                        break;
                    case Key.Right:
                        _time = Math.Min(_end, _time + step);
                        break;
                    case Key.Left:
                        _time = Math.Max(0, _time - step);
                        break;
                    case Key.Up:
                        _speed = Math.Min(256, _speed * 2);
                        break;
                    case Key.Down:
                        _speed = Math.Max(0.25, _speed / 2);
                        break;
                    case Key.Home:
                        _time = 0;
                        break;
                    case Key.End:
                        _time = _end;
                        break;
                }

                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _camera.Zoom = ClampZoom(_camera.Zoom * 1.1f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _camera.Zoom = ClampZoom(_camera.Zoom / 1.1f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle)) != 0:
                _camera.Position -= motion.Relative / _camera.Zoom;
                break;
        }
    }

    private bool TryLoad(out RasterWorld world, out Corridor? corridor, out string title, out string error)
    {
        world = null!;
        corridor = null;
        title = string.Empty;
        var args = OS.GetCmdlineUserArgs();
        var scenarioDir = Option(args, "--scenario");
        var logFile = Option(args, "--log");
        if (scenarioDir is null || logFile is null)
        {
            error = "No replay given.";
            return false;
        }

        try
        {
            var scenario = ScenarioDefinition.Load(scenarioDir);
            _events = EventLog.FromJsonLines(File.ReadAllText(logFile));
            world = scenario.World.World;
            // A log of another run on the same map (such as the combat demo) does not have this scenario's agent or squad, nor
            // its corridor.
            var ofThisScenario = scenario.Squad is { } squad
                ? _events.OfType<SquadFormed>().Any(f => f.SquadId == squad.Id)
                : _events.Any(e => e.AgentId == scenario.AgentId);
            corridor = ofThisScenario ? scenario.Corridor : null;
            title = $"{Path.GetFileName(logFile)} on {Path.GetFileName(Path.TrimEndingDirectorySeparator(scenarioDir))}";
            error = string.Empty;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
        {
            error = $"Could not load the replay: {e.Message}";
            return false;
        }
    }

    private string RecentEvents()
    {
        var lines = new List<string>();
        for (var i = _events.Count - 1; i >= 0 && lines.Count < EventLines; i--)
        {
            var e = _events[i];
            if (e.Time > _time + EventReplay.TimeTolerance || e is AgentMoved or WatchChanged)
            {
                continue;
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{e.Time,7:0.0} s  {e.AgentId}  {Describe(e)}"));
        }

        lines.Reverse();
        return string.Join('\n', lines);
    }

    private static string Describe(SimEvent e) => e switch
    {
        AgentPlaced => "placed",
        ActivityChanged a => $"-> {a.Activity}",
        PathPlanned p => string.Create(CultureInfo.InvariantCulture, $"route planned: {p.Waypoints.Count} points, {p.CostSeconds:0} s"),
        PermissionRequested r => $"asks to cross {r.BoundaryId}",
        PermissionAnswered a => $"leader: {a.Answer} ({a.BoundaryId})",
        PermissionTimedOut t => $"no answer ({t.BoundaryId})",
        Arrived => "arrived",
        Blocked b => $"blocked: {b.Reason}",
        ShotFired s => $"fires at {s.TargetId}: {(s.Hit ? "hit" : "miss")}",
        ShotHeard h => $"hears {h.ShooterId}",
        HealthChanged h => $"health {h.Health}",
        CasualtyOccurred c => $"casualty ({c.Health})",
        SuppressionStarted => "suppressed",
        SuppressionEnded => "no longer suppressed",
        PostureChanged p => $"posture {p.Posture}",
        EnemySpotted s => $"spots {s.EnemyId}",
        SpotReported r => $"reports {r.EnemyId}",
        SpotReceived r => $"hears of {r.EnemyId} from {r.ReporterId}",
        SquadHalted h => $"squad {h.SquadId} halts ({h.EnemyId})",
        SquadResumed r => $"squad {r.SquadId} moves on",
        EdgeDeployed d => $"squad {d.SquadId} deployed at the forest edge",
        ContactReported c => $"contact: {c.AttackerId}",
        _ => e.GetType().Name,
    };

    private void LayoutHud()
    {
        var size = GetViewportRect().Size;
        _slider.Position = new Vector2(12, size.Y - 32);
        _slider.Size = new Vector2(size.X - 24, 24);
        _log.Position = new Vector2(size.X - 432, 8);
        _log.Size = new Vector2(420, 0);

        // Fit the whole map between the status text and the timeline.
        var fit = Math.Max(MinZoom, Math.Min((size.X - 40) / _focus.Size.X, (size.Y - 200) / _focus.Size.Y));
        _camera.Zoom = new Vector2(fit, fit);
        _camera.Position = _focus.GetCenter() + new Vector2(0, -40 / fit);
    }

    /// <summary>
    /// What the camera frames at the start: where the agents start and the corridor, with a margin, at least
    /// <see cref="MinFocusMeters"/> across and never more than the whole map.
    /// </summary>
    private Rect2 Focus(RasterWorld world, Corridor? corridor)
    {
        var points = _events.OfType<AgentPlaced>().Select(p => p.Position.Xy)
            .Concat(corridor?.Centerline ?? [])
            .Select(_mapper.ToScreen)
            .ToList();
        var map = new Rect2(Vector2.Zero, _mapper.ToScreenLength(world.WidthMeters), _mapper.ToScreenLength(world.DepthMeters));
        if (points.Count == 0)
        {
            return map;
        }

        var box = new Rect2(points[0], Vector2.Zero);
        foreach (var p in points)
        {
            box = box.Expand(p);
        }

        var minSize = _mapper.ToScreenLength(MinFocusMeters);
        var size = new Vector2(Math.Max(box.Size.X, minSize), Math.Max(box.Size.Y, minSize)) * 1.3f;
        var focus = new Rect2(box.GetCenter() - (size / 2), size);
        return focus.Size.X >= map.Size.X || focus.Size.Y >= map.Size.Y ? map : focus;
    }

    private static Vector2 ClampZoom(Vector2 zoom)
    {
        var z = Math.Clamp(zoom.X, MinZoom, MaxZoom);
        return new Vector2(z, z);
    }

    private static Label HudLabel(CanvasLayer hud, Vector2 position)
    {
        var label = new Label { Position = position };
        label.AddThemeColorOverride("font_color", Colors.Black);
        label.AddThemeColorOverride("font_outline_color", Colors.White);
        label.AddThemeConstantOverride("outline_size", 4);
        hud.AddChild(label);
        return label;
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
