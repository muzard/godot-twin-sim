using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Scenarios;
using Twin.Sim.Squads;
using Twin.Sim.World;

namespace Twin.Headless;

/// <summary>
/// A built-in ambush for looking at a squad in a replay (<c>./dev replay st1-ambush</c>, backlog 0004). It is a demo, not a
/// scenario file: the scenario format holds one agent (the <c>squad</c> section is part S2d), so the fighters are placed
/// here with the simulation's own API.
/// <para>
/// A blue squad of nine (the reference squad: three teams of three, in a wedge) starts in the forest on the second leg of
/// the st1 corridor and is ordered along it to B, across the field beyond the forest. The platoon leader grants
/// forest-edge crossings after 30 s. Red, three fighters with a machine gun, lies prone just inside the west edge of the
/// copse where the route leaves the field, watching the field the squad must cross. Everyone is under free fire: a fighter
/// fires once it has spotted an enemy (S5a1). What follows is what the simulation does today: the squad deploys at forest
/// edges, halts when its leader learns of an enemy, and each member fights on its own (0003's drill). The squad leader does
/// not yet direct fire or manoeuvre (S5b, S6), so the squad does not move on after the halt.
/// </para>
/// </summary>
internal static class AmbushDemo
{
    /// <summary>Name of the demo for <c>./dev replay</c>. The part before the last '-' names its map in <c>scenarios/</c>.</summary>
    public const string Name = "st1-ambush";

    /// <summary>Once shots have been fired, the run ends when nobody has fired for this long.</summary>
    private const double QuietSeconds = 120;

    /// <summary>The squad's id.</summary>
    public const string SquadId = "blue";

    /// <summary>Machine gun 7.62 with the values of backlog 0003 (open, to be calibrated): bursts of 5, 800 m, 500 rounds.</summary>
    private static readonly WeaponProfile MachineGun = new(
        HitProbabilityAtZero: 0.6, HitFalloffMeters: 200, MaxRangeMeters: 800, SecondsPerShot: 2, Rounds: 500,
        SoundRangeMeters: 2000, FlashSeconds: 2, SuppressionPerRound: 1.5, RoundsPerBurst: 5, SnapSecondsPerShot: 1);

    private const string Command = "command";
    private const string Point = "point";
    private const string Support = "support";

    // The reference squad of backlog 0004 (SpecSquad.Reference): role, team, experience. Members stand in squad order.
    private static readonly (string Id, SquadRole Role, string Team, Experience Experience)[] Blue =
    [
        ("rj", SquadRole.SquadLeader, Command, Experience.Regular),
        ("j1", SquadRole.Rifleman, Command, Experience.Conscript),
        ("j2", SquadRole.Rifleman, Command, Experience.Regular),
        ("pj", SquadRole.TeamLeader, Point, Experience.Conscript),
        ("k1", SquadRole.Rifleman, Point, Experience.Conscript),
        ("k2", SquadRole.Rifleman, Point, Experience.Regular),
        ("vj", SquadRole.SecondInCommand, Support, Experience.Regular),
        ("kk", SquadRole.MachineGunner, Support, Experience.Conscript),
        ("t1", SquadRole.Rifleman, Support, Experience.Conscript),
    ];

    // Red in local metres on st1: in the copse at the field's north-east end, a few metres inside its west edge, just south
    // of where the route enters it.
    private static readonly (string Id, Vec2 At, bool MachineGun)[] Red =
    [
        ("red-1", new Vec2(757, 362), false),
        ("red-mg", new Vec2(748, 372), true),
        ("red-2", new Vec2(752, 366), false),
    ];

    /// <summary>Where red watches: back across the field toward where the squad leaves the forest.</summary>
    private static readonly Vec2 RedWatches = new(620, 330);

    /// <summary>
    /// The squad's corridor: the second leg of the st1 corridor (100 m wide, as in <c>scenario.json</c>), from a start in
    /// the forest before the field to B.
    /// </summary>
    private static readonly Vec2[] Centerline = [new(515, 279), new(900, 450)];

    /// <summary>Directory of the map the demo runs on.</summary>
    public static string MapDirectory(string scenariosRoot) => Path.Combine(scenariosRoot, "st1");

    /// <summary>Creates the simulation with both sides placed and the squad ordered off. No ticks have run yet.</summary>
    public static Simulation Create(string scenariosRoot)
    {
        var map = ScenarioDefinition.Load(MapDirectory(scenariosRoot));
        var config = new SimulationConfig(map.TickRateHz, map.Seed)
        {
            Leader = new ScriptedLeader(LeaderAnswer.Grant, DelaySeconds: 30, PermitRadiusMeters: 25),
            Permission = map.Permission,
            Combat = CombatRules.Default with { Doctrine = map.Doctrine },
        };
        var simulation = new Simulation(map.World.World, config);

        // The squad stands in a row back along the corridor from its start, the leader first.
        var corridor = new Corridor(Centerline, map.Corridor.Width, map.Corridor.SoftMargin, map.Corridor.SoftPenalty, HardBoundaries: []);
        var start = corridor.Centerline[0];
        var back = (start - corridor.Centerline[1]).Normalized();
        for (var i = 0; i < Blue.Length; i++)
        {
            var member = Blue[i];
            var weapon = member.Role == SquadRole.MachineGunner ? MachineGun : WeaponProfile.Rifle;
            simulation.AddUnit(member.Id, start + (back * (3.0 * i)), MovementProfile.Infantry,
                new Combatant(Side.Blue, RulesOfEngagement.FreeFire, weapon, SquadId, member.Experience));
        }

        simulation.AddSquad(new SquadDefinition(
            SquadId, [Command, Point, Support], [.. Blue.Select(m => new SquadMember(m.Id, m.Role, m.Team))], SquadFormation.Wedge));

        foreach (var (id, at, machineGun) in Red)
        {
            simulation.AddUnit(id, at, MovementProfile.Infantry,
                new Combatant(Side.Red, RulesOfEngagement.FreeFire, machineGun ? MachineGun : WeaponProfile.Rifle, "red"));
            simulation.Issue(id, new SetPosture(Posture.Prone));
            simulation.Issue(id, new Watch(RedWatches - at));
        }

        simulation.Issue("rj", new MoveThroughCorridor(corridor));
        return simulation;
    }

    /// <summary>
    /// True when one side has nobody left in action, the squad's leader has arrived, or shots have been fired and nobody
    /// has fired for <see cref="QuietSeconds"/>.
    /// </summary>
    public static bool IsOver(Simulation simulation)
    {
        var sidesInAction = simulation.Units
            .Where(u => u.Combatant is not null && u.Health != Health.OutOfAction)
            .Select(u => u.Combatant!.Side)
            .Distinct()
            .Count();
        if (sidesInAction < 2)
        {
            return true;
        }

        if (simulation.EventLog.OfType<Arrived>().Any(e => e.AgentId == "rj"))
        {
            return true;
        }

        var lastShot = simulation.EventLog.OfType<ShotFired>().LastOrDefault()?.Time;
        return lastShot is { } time && simulation.TimeSeconds - time >= QuietSeconds;
    }

    /// <summary>A one-line summary of the run for the console.</summary>
    public static string Outcome(Simulation simulation)
    {
        int OutOfAction(Side side) => simulation.Units.Count(u => u.Combatant?.Side == side && u.Health == Health.OutOfAction);
        var spotted = simulation.EventLog.OfType<EnemySpotted>().Count();
        var halted = simulation.EventLog.OfType<SquadHalted>().Any() ? "squad halted" : "squad not halted";
        return $"{spotted} spotted, {halted}, {simulation.EventLog.OfType<ShotFired>().Count()} shots, " +
            $"out of action blue {OutOfAction(Side.Blue)}/9 red {OutOfAction(Side.Red)}/3";
    }
}
