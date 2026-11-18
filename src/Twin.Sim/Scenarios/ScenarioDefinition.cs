using System.Text.Json;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Sim.World;

namespace Twin.Sim.Scenarios;

/// <summary>
/// A scenario read from <c>scenarios/&lt;name&gt;/</c>: <c>scenario.json</c> plus the preprocessed world in
/// <c>world/</c>, or the directory its <c>world</c> key names (format: scenarios/README.md). Points in the file are
/// ETRS-TM35FIN; here they are local meters. A scenario has either one agent or a squad (backlog 0004, S2d), and
/// optionally enemies.
/// </summary>
public sealed class ScenarioDefinition
{
    private ScenarioDefinition(
        LoadedWorld world, string agentId, Vec2 start, Corridor corridor, ScriptedLeader leader, PermissionRules permission,
        MovementProfile profile, ulong seed, int tickRateHz, Doctrine doctrine, SquadDefinition? squad,
        IReadOnlyDictionary<string, Combatant> squadCombatants, IReadOnlyList<ScenarioEnemy> enemies)
    {
        Doctrine = doctrine;
        World = world;
        AgentId = agentId;
        Start = start;
        Squad = squad;
        SquadCombatants = squadCombatants;
        Enemies = enemies;
        Corridor = corridor;
        Leader = leader;
        Permission = permission;
        Profile = profile;
        Seed = seed;
        TickRateHz = tickRateHz;
    }

    /// <summary>The world and its ETRS origin.</summary>
    public LoadedWorld World { get; }

    /// <summary>The agent the corridor order goes to: the scenario's agent, or the squad's leader in a squad scenario.</summary>
    public string AgentId { get; }

    /// <summary>Where the agent, or the squad's leader, starts (A), local meters.</summary>
    public Vec2 Start { get; }

    /// <summary>The squad (<c>squad</c> in scenario.json, backlog 0004 S2d); null for an agent scenario.</summary>
    public SquadDefinition? Squad { get; }

    /// <summary>Where the squad's leader starts, local meters (the same as <see cref="Start"/>).</summary>
    public Vec2 SquadStart => Start;

    /// <summary>Each squad member's fighter, by member id; empty for an agent scenario.</summary>
    public IReadOnlyDictionary<string, Combatant> SquadCombatants { get; }

    /// <summary>The enemies (<c>enemies</c> in scenario.json), in file order; empty if there are none.</summary>
    public IReadOnlyList<ScenarioEnemy> Enemies { get; }

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
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "scenario.json")));
        var root = json.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported scenario schema version.");
        }

        var worldPath = root.TryGetProperty("world", out var w) ? w.GetString() ?? "world" : "world";
        var world = WorldFiles.Load(Path.Combine(directory, worldPath));
        Vec2 Local(JsonElement point) => world.Origin.ToLocal(new EtrsPoint(point[0].GetDouble(), point[1].GetDouble()));
        Vec2[] Line(JsonElement points) => points.EnumerateArray().Select(Local).ToArray();

        var hasAgent = root.TryGetProperty("agent", out var agent);
        var hasSquad = root.TryGetProperty("squad", out var squadJson);
        if (hasAgent == hasSquad)
        {
            throw new InvalidDataException("A scenario needs exactly one of 'agent' and 'squad'.");
        }

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

        var enemies = root.TryGetProperty("enemies", out var enemiesJson)
            ? enemiesJson.EnumerateArray().Select(e => ReadEnemy(e, Local)).ToList()
            : [];
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var enemy in enemies)
        {
            if (!world.World.Contains(enemy.Position))
            {
                throw new InvalidDataException($"Enemy '{enemy.Id}' is off the map.");
            }
        }

        if (hasSquad)
        {
            var (squad, combatants) = ReadSquad(squadJson);
            var start = Local(squadJson.GetProperty("start"));
            CheckSquad(world, squad, combatants, start, corridor);
            CheckUnique(ids, squad.Members.Select(m => m.UnitId).Concat(enemies.Select(e => e.Id)));
            return new ScenarioDefinition(
                world, squad.Members.First(m => m.Role == SquadRole.SquadLeader).UnitId, start, corridor, leader, permission, MovementProfile.Infantry, seed, tickRate, ReadDoctrine(root),
                squad, combatants, enemies);
        }

        CheckUnique(ids, enemies.Select(e => e.Id).Prepend(agent.GetProperty("id").GetString() ?? "agent"));
        return new ScenarioDefinition(
            world, agent.GetProperty("id").GetString() ?? "agent", Local(agent.GetProperty("start")), corridor, leader,
            permission, MovementProfile.Infantry with { CanRappel = agent.TryGetProperty("canRappel", out var rappel) && rappel.GetBoolean() },
            seed, tickRate, ReadDoctrine(root), null, new SortedDictionary<string, Combatant>(StringComparer.Ordinal), enemies);
    }

    /// <summary>The <c>squad</c> section: the definition and each member's fighter (backlog 0004 S2d).</summary>
    private static (SquadDefinition Squad, SortedDictionary<string, Combatant> Combatants) ReadSquad(JsonElement json)
    {
        var id = json.GetProperty("id").GetString() ?? string.Empty;
        var rules = json.TryGetProperty("rules", out var r) ? Name<RulesOfEngagement>(r.GetString(), "rules") : RulesOfEngagement.ReturnFire;
        var formation = json.TryGetProperty("formation", out var f) ? Name<SquadFormation>(f.GetString(), "formation") : SquadFormation.File;
        var teams = json.GetProperty("teams").EnumerateArray().Select(t => t.GetString() ?? string.Empty).ToList();
        var members = new List<SquadMember>();
        var combatants = new SortedDictionary<string, Combatant>(StringComparer.Ordinal);
        foreach (var m in json.GetProperty("members").EnumerateArray())
        {
            var memberId = m.GetProperty("id").GetString() ?? string.Empty;
            members.Add(new SquadMember(memberId, Name<SquadRole>(m.GetProperty("role").GetString(), "role"), m.GetProperty("team").GetString() ?? string.Empty));
            var combatant = new Combatant(
                Side.Blue,
                m.TryGetProperty("rules", out var mr) ? Name<RulesOfEngagement>(mr.GetString(), "rules") : rules,
                m.TryGetProperty("weapon", out var weapon) ? Weapon(weapon.GetString()) : WeaponProfile.Rifle,
                id,
                m.TryGetProperty("experience", out var x) ? Name<Experience>(x.GetString(), "experience") : Experience.Regular);
            if (!combatants.TryAdd(memberId, combatant))
            {
                throw new InvalidDataException($"Squad '{id}': '{memberId}' is listed twice.");
            }
        }

        return (new SquadDefinition(id, teams, members, formation), combatants);
    }

    /// <summary>Every id is used once across the agent or the squad and the enemies.</summary>
    private static void CheckUnique(SortedSet<string> ids, IEnumerable<string> all)
    {
        foreach (var id in all)
        {
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"The id '{id}' is used twice.");
            }
        }
    }

    /// <summary>
    /// Checks that the squad can be placed (its start and every member's starting slot on the map, a corridor to lay it
    /// out along) and is valid as <see cref="Simulation.AddSquad"/> checks it, so an unusable squad fails when it is loaded.
    /// </summary>
    private static void CheckSquad(
        LoadedWorld world, SquadDefinition squad, SortedDictionary<string, Combatant> combatants, Vec2 start, Corridor corridor)
    {
        if (!world.World.Contains(start))
        {
            throw new InvalidDataException($"Squad '{squad.Id}': its start is off the map.");
        }

        if (corridor.Centerline.Count < 2 || corridor.Centerline[0].DistanceTo(corridor.Centerline[1]) < 1e-9)
        {
            throw new InvalidDataException(
                $"Squad '{squad.Id}': the corridor's first segment must have a length, to lay the squad out along it.");
        }

        try
        {
            var check = new Simulation(world.World, new SimulationConfig());
            foreach (var (id, combatant) in combatants)
            {
                check.AddUnit(id, start, MovementProfile.Infantry, combatant);
            }

            check.AddSquad(squad);
            foreach (var (id, at) in StartingPositions(squad, start, corridor))
            {
                if (!world.World.Contains(at))
                {
                    throw new InvalidDataException($"Squad '{squad.Id}': '{id}' would start off the map, at {at}.");
                }
            }
        }
        catch (ArgumentException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    /// <summary>
    /// Where each member starts: its slot of the starting formation around <paramref name="start"/>, laid out along the
    /// corridor's first segment (the squad leader at the start).
    /// </summary>
    private static IEnumerable<(string Id, Vec2 At)> StartingPositions(SquadDefinition squad, Vec2 start, Corridor corridor)
    {
        var forward = (corridor.Centerline[1] - corridor.Centerline[0]).Normalized();
        var left = new Vec2(-forward.Y, forward.X);
        return FormationSlots.For(squad.Formation, squad, SquadDoctrine.Default.IntervalMeters)
            .Select(slot => (slot.UnitId, start + (forward * slot.Along) + (left * slot.Across)));
    }

    /// <summary>One entry of <c>enemies</c> (backlog 0004 S2d).</summary>
    private static ScenarioEnemy ReadEnemy(JsonElement e, Func<JsonElement, Vec2> local) => new(
        e.GetProperty("id").GetString() ?? string.Empty,
        local(e.GetProperty("position")),
        new Combatant(
            e.TryGetProperty("side", out var side) ? Name<Side>(side.GetString(), "side") : Side.Red,
            e.TryGetProperty("rules", out var rules) ? Name<RulesOfEngagement>(rules.GetString(), "rules") : RulesOfEngagement.FreeFire,
            e.TryGetProperty("weapon", out var weapon) ? Weapon(weapon.GetString()) : WeaponProfile.Rifle,
            Experience: e.TryGetProperty("experience", out var x) ? Name<Experience>(x.GetString(), "experience") : Experience.Regular),
        e.TryGetProperty("posture", out var posture) ? Name<Posture>(posture.GetString(), "posture") : Posture.Standing,
        e.TryGetProperty("watchToward", out var watch) ? local(watch) : null);

    /// <summary>A weapon by its name in scenario.json.</summary>
    private static WeaponProfile Weapon(string? name) => name switch
    {
        "rifle" => WeaponProfile.Rifle,
        "machineGun" => WeaponProfile.MachineGun,
        "heavyMachineGun" => WeaponProfile.HeavyMachineGun,
        _ => throw new InvalidDataException($"Unknown weapon '{name}'; use rifle, machineGun or heavyMachineGun."),
    };

    /// <summary>An enum value by its name in scenario.json, in camel case (<c>squadLeader</c>, <c>freeFire</c>).</summary>
    private static T Name<T>(string? text, string what)
        where T : struct, Enum
    {
        foreach (var name in Enum.GetNames<T>())
        {
            if (string.Equals(char.ToLowerInvariant(name[0]) + name[1..], text, StringComparison.Ordinal))
            {
                return Enum.Parse<T>(name);
            }
        }

        var names = string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
        throw new InvalidDataException($"Unknown {what} '{text}'; use {names}.");
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
        if (Squad is { } squad)
        {
            foreach (var (id, at) in StartingPositions(squad, Start, Corridor))
            {
                simulation.AddUnit(id, at, MovementProfile.Infantry, SquadCombatants[id]);
            }

            simulation.AddSquad(squad);
        }
        else
        {
            simulation.AddUnit(AgentId, Start, Profile);
        }

        foreach (var enemy in Enemies)
        {
            simulation.AddUnit(enemy.Id, enemy.Position, MovementProfile.Infantry, enemy.Combatant);
            if (enemy.Posture != Posture.Standing)
            {
                simulation.Issue(enemy.Id, new SetPosture(enemy.Posture));
            }

            if (enemy.WatchToward is { } point && point.DistanceTo(enemy.Position) > 1e-9)
            {
                simulation.Issue(enemy.Id, new Watch(point - enemy.Position));
            }
        }

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

/// <summary>An enemy fighter in a scenario (<c>enemies</c> in scenario.json, backlog 0004 S2d). It gets no movement order.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Position">Where it is, local meters.</param>
/// <param name="Combatant">Its side, rules of engagement, weapon and experience.</param>
/// <param name="Posture">The posture it takes at the start.</param>
/// <param name="WatchToward">The point it watches, local meters; null to keep the default watch.</param>
public sealed record ScenarioEnemy(string Id, Vec2 Position, Combatant Combatant, Posture Posture, Vec2? WatchToward);
