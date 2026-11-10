using System.Text.Json;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.World;

namespace Twin.Sim.Scenarios;

/// <summary>
/// A scenario read from <c>scenarios/&lt;name&gt;/</c>: <c>scenario.json</c> plus the preprocessed world in
/// <c>world/</c> (format: scenarios/README.md). Points in the file are ETRS-TM35FIN; here they are local meters.
/// </summary>
public sealed class ScenarioDefinition
{
    private ScenarioDefinition(
        LoadedWorld world, string agentId, Vec2 start, Corridor corridor, ScriptedLeader leader, PermissionRules permission,
        MovementProfile profile, ulong seed, int tickRateHz, Doctrine doctrine)
    {
        Doctrine = doctrine;
        World = world;
        AgentId = agentId;
        Start = start;
        Corridor = corridor;
        Leader = leader;
        Permission = permission;
        Profile = profile;
        Seed = seed;
        TickRateHz = tickRateHz;
    }

    /// <summary>The world and its ETRS origin.</summary>
    public LoadedWorld World { get; }

    /// <summary>The agent's id.</summary>
    public string AgentId { get; }

    /// <summary>Where the agent starts (A), local meters.</summary>
    public Vec2 Start { get; }

    /// <summary>The corridor of the agent's order; its last centerline point is B.</summary>
    public Corridor Corridor { get; }

    /// <summary>The scripted leader as the file describes it.</summary>
    public ScriptedLeader Leader { get; }

    /// <summary>When the agent asks for permission and how long it waits.</summary>
    public PermissionRules Permission { get; }

    /// <summary>
    /// How the agent moves: <see cref="MovementProfile.Infantry"/>, able to rappel if <c>agent.canRappel</c> is true.
    /// </summary>
    public MovementProfile Profile { get; }

    /// <summary>The scenario's doctrine (<c>doctrine</c> in scenario.json), or the default doctrine.</summary>
    public Doctrine Doctrine { get; }

    /// <summary>Random seed.</summary>
    public ulong Seed { get; }

    /// <summary>Ticks per simulated second (1 / dtSeconds).</summary>
    public int TickRateHz { get; }

    /// <summary>Reads the scenario in <paramref name="directory"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid scenario.</exception>
    public static ScenarioDefinition Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        try
        {
            return Read(directory);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Invalid scenario in '{directory}': {e.Message}", e);
        }
    }

    private static ScenarioDefinition Read(string directory)
    {
        var world = WorldFiles.Load(Path.Combine(directory, "world"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.json")));
        var root = json.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported scenario schema version.");
        }

        Vec2 Local(JsonElement point) => world.Origin.ToLocal(new EtrsPoint(point[0].GetDouble(), point[1].GetDouble()));
        Vec2[] Line(JsonElement points) => points.EnumerateArray().Select(Local).ToArray();

        var agent = root.GetProperty("agent");
        var corridorJson = root.GetProperty("corridor");
        var boundaries = corridorJson.TryGetProperty("hardBoundaries", out var hb)
            ? hb.EnumerateArray()
                .Select(b => new HardBoundary(b.GetProperty("id").GetString() ?? "boundary", Line(b.GetProperty("line")), BoundarySource.Order))
                .ToArray()
            : [];
        var corridor = new Corridor(
            Line(corridorJson.GetProperty("centerline")),
            corridorJson.GetProperty("width").GetDouble(),
            corridorJson.GetProperty("softMargin").GetDouble(),
            corridorJson.GetProperty("softPenalty").GetDouble(),
            boundaries);

        var leaderJson = root.GetProperty("leader");
        var leader = new ScriptedLeader(
            ParseAnswer(leaderJson.GetProperty("answer").GetString()),
            leaderJson.GetProperty("delaySeconds").GetDouble(),
            leaderJson.GetProperty("permitRadius").GetDouble());

        var permission = root.TryGetProperty("permission", out var p)
            ? new PermissionRules(p.GetProperty("threshold").GetDouble(), p.GetProperty("timeoutSeconds").GetDouble())
            : PermissionRules.Default;
        var seed = root.TryGetProperty("seed", out var s) ? s.GetUInt64() : 1UL;
        var dt = root.TryGetProperty("dtSeconds", out var d) ? d.GetDouble() : 0.5;
        var tickRate = (int)Math.Round(1 / dt);
        if (Math.Abs((1.0 / tickRate) - dt) > 1e-12)
        {
            throw new InvalidDataException($"dtSeconds {dt} must be 1 / a whole number of ticks per second.");
        }

        return new ScenarioDefinition(
            world, agent.GetProperty("id").GetString() ?? "agent", Local(agent.GetProperty("start")), corridor, leader,
            permission, MovementProfile.Infantry with { CanRappel = agent.TryGetProperty("canRappel", out var rappel) && rappel.GetBoolean() },
            seed, tickRate, ReadDoctrine(root));
    }

    /// <summary>
    /// Creates the simulation with the agent placed at A and its corridor order issued, the leader answering
    /// <paramref name="answer"/> after the scenario's delay. No ticks have run yet.
    /// </summary>
    public Simulation CreateSimulation(LeaderAnswer answer)
    {
        var config = new SimulationConfig(TickRateHz, Seed)
        {
            Leader = Leader with { Answer = answer },
            Permission = Permission,
            Combat = CombatRules.Default with { Doctrine = Doctrine },
        };
        var simulation = new Simulation(World.World, config);
        simulation.AddUnit(AgentId, Start, Profile);
        simulation.Issue(AgentId, new MoveThroughCorridor(Corridor));
        return simulation;
    }

    private static Doctrine ReadDoctrine(JsonElement root)
    {
        if (!root.TryGetProperty("doctrine", out var d))
        {
            return Doctrine.Default;
        }

        var doctrine = Doctrine.Default;
        if (d.TryGetProperty("returnFireOverridesFireAt", out var returnFire))
        {
            doctrine = doctrine with { ReturnFireOverridesFireAt = returnFire.GetBoolean() };
        }

        if (d.TryGetProperty("permissionAnswers", out var answers))
        {
            doctrine = doctrine with
            {
                PermissionAnswers = answers.GetString() switch
                {
                    "whenFree" => PermissionAnswerTiming.WhenFree,
                    "immediately" => PermissionAnswerTiming.Immediately,
                    var other => throw new InvalidDataException($"Unknown permissionAnswers '{other}'; use whenFree or immediately."),
                },
            };
        }

        return doctrine;
    }

    private static LeaderAnswer ParseAnswer(string? text) => text switch
    {
        "grant" => LeaderAnswer.Grant,
        "deny" => LeaderAnswer.Deny,
        "noAnswer" => LeaderAnswer.NoAnswer,
        _ => throw new InvalidDataException($"Unknown leader answer '{text}'; use grant, deny or noAnswer."),
    };
}
