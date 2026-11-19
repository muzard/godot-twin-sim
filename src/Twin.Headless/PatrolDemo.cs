using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Scenarios;
using Twin.Sim.Squads;

namespace Twin.Headless;

/// <summary>
/// A built-in patrol for showing what squads do (<c>./dev replay st1-patrol</c>, backlog 0004). It is a demo, not a scenario
/// file: the scenario format cannot yet change formation on the way, so the run is scripted here.
/// <para>
/// The reference squad of nine walks east across the big field in the middle of <c>scenarios/st1</c> in file, crossing the
/// road there; past x = 480 m its leader commands a wedge, and the squad walks on in it. Three red fighters, one with a
/// machine gun, lie in the copse on the field's north side, about 20 m from the route; when the leader is past x = 560 m
/// they open fire on the squad's flank. The simulation has no concealment model yet (prone fighters at a forest edge are
/// spotted across the open field from far off), so the demo places red only at that moment: red appears when it opens
/// fire. From then on the simulation decides: spotting and spot reports, the squad halting, each fighter's drill under
/// fire, and the leader distributing the squad's fire.
/// </para>
/// <para>
/// <c>./dev replay st1-hill</c> is the same patrol with the ambush farther off: red lies at the south edge of the wooded rise
/// north-west of the field, about 125 m from the route and 8 m above it, where it can see the route between x = 440 and
/// 520 m (from lower ground nearer the route a low swell in the field hides it). The leader commands the wedge past
/// x = 450 m, after the road, and red opens fire when it is past x = 520 m.
/// </para>
/// </summary>
internal static class PatrolDemo
{
    /// <summary>Name of the demo for <c>./dev replay</c>. The part before the last '-' names its map in <c>scenarios/</c>.</summary>
    public const string Name = "st1-patrol";

    /// <summary>Name of the variant with the ambush from the hill.</summary>
    public const string HillName = "st1-hill";

    private const string SquadId = "blue";

    /// <summary>One version of the patrol: where the leader commands the wedge, where red opens fire, and where red lies.</summary>
    internal sealed record Variant(string Name, double WedgeAtX, double AmbushAtX, IReadOnlyList<(string Id, Vec2 At, bool MachineGun)> Red);

    /// <summary>The ambush from the copse 20 m from the route.</summary>
    public static Variant Copse { get; } = new(Name, 480, 560,
    [
        ("red-1", new Vec2(540, 592), false),
        ("red-mg", new Vec2(548, 596), true),
        ("red-2", new Vec2(532, 600), false),
    ]);

    /// <summary>The ambush from the wooded rise about 125 m north of the route.</summary>
    public static Variant Hill { get; } = new(HillName, 450, 520,
    [
        ("red-1", new Vec2(448, 698), false),
        ("red-mg", new Vec2(456, 698), true),
        ("red-2", new Vec2(464, 698), false),
    ]);

    /// <summary>Machine gun 7.62 with backlog 0003's values (open, to be calibrated).</summary>
    private static WeaponProfile MachineGun => WeaponProfile.MachineGun;

    // The reference squad of backlog 0004 (SpecSquad.Reference): role, team, experience, in squad order.
    private static readonly (string Id, SquadRole Role, string Team, Experience Experience)[] Blue =
    [
        ("rj", SquadRole.SquadLeader, "command", Experience.Regular),
        ("j1", SquadRole.Rifleman, "command", Experience.Conscript),
        ("j2", SquadRole.Rifleman, "command", Experience.Regular),
        ("pj", SquadRole.TeamLeader, "point", Experience.Conscript),
        ("k1", SquadRole.Rifleman, "point", Experience.Conscript),
        ("k2", SquadRole.Rifleman, "point", Experience.Regular),
        ("vj", SquadRole.SecondInCommand, "support", Experience.Regular),
        ("kk", SquadRole.MachineGunner, "support", Experience.Conscript),
        ("t1", SquadRole.Rifleman, "support", Experience.Conscript),
    ];

    /// <summary>The squad's corridor: east along the field, 80 m wide.</summary>
    private static readonly Corridor Route = new([new Vec2(285, 574), new Vec2(650, 574)], 80, 25, 1, HardBoundaries: []);

    private static readonly Vec2 LeaderStart = new(340, 574);

    /// <summary>Directory of the map the demo runs on.</summary>
    public static string MapDirectory(string scenariosRoot) => Path.Combine(scenariosRoot, "st1");

    /// <summary>Creates the simulation with both sides placed and the squad ordered off in file. No ticks have run yet.</summary>
    public static Simulation Create(string scenariosRoot)
    {
        var map = ScenarioDefinition.Load(MapDirectory(scenariosRoot));
        var config = new SimulationConfig(map.TickRateHz, map.Seed)
        {
            Leader = new ScriptedLeader(LeaderAnswer.Grant, DelaySeconds: 10, PermitRadiusMeters: 25), // short, to keep the demo moving
            Permission = map.Permission,
            Combat = CombatRules.Default with { Doctrine = map.Doctrine },
        };
        var simulation = new Simulation(map.World.World, config);

        // Everyone starts in its file slot along the route, the point team ahead of the leader.
        var squad = new SquadDefinition(SquadId, ["command", "point", "support"], [.. Blue.Select(m => new SquadMember(m.Id, m.Role, m.Team))]);
        var forward = (Route.Centerline[1] - Route.Centerline[0]).Normalized();
        foreach (var slot in FormationSlots.For(SquadFormation.File, squad, config.SquadDoctrine.IntervalMeters))
        {
            var member = Blue.Single(m => m.Id == slot.UnitId);
            simulation.AddUnit(member.Id, LeaderStart + (forward * slot.Along), MovementProfile.Infantry,
                new Combatant(Side.Blue, RulesOfEngagement.ReturnFire, member.Role == SquadRole.MachineGunner ? MachineGun : WeaponProfile.Rifle,
                    SquadId, member.Experience));
        }

        simulation.AddSquad(squad);
        simulation.Issue("rj", new MoveThroughCorridor(Route));
        return simulation;
    }

    /// <summary>
    /// The script, run before every step: the wedge past the variant's <c>WedgeAtX</c>; at its <c>AmbushAtX</c> red is placed,
    /// and from then on each red fighter without a target fires at the nearest member of the squad it has spotted.
    /// </summary>
    public static Action Script(Simulation simulation, Variant variant)
    {
        var wedge = false;
        var ambush = false;
        return () =>
        {
            var leader = simulation.Squads.First().LeaderId is { } id ? simulation.GetUnit(id) : null;
            if (!wedge && leader is not null && leader.Position.X >= variant.WedgeAtX)
            {
                wedge = true;
                simulation.Issue(leader.Id, new SetSquadFormation(SquadFormation.Wedge));
            }

            if (!ambush && leader is not null && leader.Position.X >= variant.AmbushAtX)
            {
                ambush = true;
                foreach (var (redId, at, machineGun) in variant.Red)
                {
                    simulation.AddUnit(redId, at, MovementProfile.Infantry,
                        new Combatant(Side.Red, RulesOfEngagement.HoldFire, machineGun ? MachineGun : WeaponProfile.Rifle, "red"));
                    simulation.Issue(redId, new SetPosture(Posture.Prone));
                    simulation.Issue(redId, new Watch(new Vec2(0, -1))); // over the field
                }
            }

            if (!ambush)
            {
                return;
            }

            foreach (var (redId, _, _) in variant.Red)
            {
                var red = simulation.GetUnit(redId);
                if (red.Health == Health.OutOfAction || red.CurrentOrder is FireAt)
                {
                    continue;
                }

                var target = Blue.Select(b => simulation.GetUnit(b.Id))
                    .Where(b => b.Health != Health.OutOfAction && simulation.KnowsOf(redId, b.Id))
                    .OrderBy(b => b.Position.Xy.DistanceTo(red.Position.Xy)).ThenBy(b => b.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (target is not null)
                {
                    simulation.Issue(redId, new FireAt(target.Id));
                }
            }
        };
    }
}
