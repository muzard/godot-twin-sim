using Twin.Sim;
using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Movement;
using Twin.Sim.Orders;
using Twin.Sim.Scenarios;
using Twin.Sim.World;

namespace Twin.Headless;

/// <summary>
/// A built-in fire fight for looking at shooting in a replay (<c>./dev replay st1-combat</c>). It is a demo, not a
/// scenario file: the scenario format holds one agent and a corridor, so the fighters are placed here with the
/// simulation's own API. Two fire teams of three face each other across the field of acceptance test K2 on
/// <c>scenarios/st1</c>, all under free fire with the backlog 0002 rifle.
/// </summary>
internal static class CombatDemo
{
    /// <summary>Name of the demo for <c>./dev replay</c>. The part before the last '-' names its map in <c>scenarios/</c>.</summary>
    public const string Name = "st1-combat";

    /// <summary>The run ends when nobody has fired for this long, for example because no one can see an enemy.</summary>
    private const double QuietSeconds = 120;

    // Fire team leaders on the K2 line across a field (EngagementOnSt1Tests), in ETRS-TM35FIN meters.
    private static readonly (string Prefix, Side Side, double Easting, double Northing)[] Teams =
    [
        ("blue", Side.Blue, 403871, 6687191),
        ("red", Side.Red, 404009, 6687083),
    ];

    // Members spread a few meters apart, as a fire team would lie.
    private static readonly Vec2[] Spread = [new(0, 0), new(-8, 6), new(8, -6)];

    /// <summary>Directory of the map the demo runs on.</summary>
    public static string MapDirectory(string scenariosRoot) => Path.Combine(scenariosRoot, "st1");

    /// <summary>Creates the simulation with both fire teams placed. No ticks have run yet.</summary>
    public static Simulation Create(string scenariosRoot)
    {
        var map = ScenarioDefinition.Load(MapDirectory(scenariosRoot));
        var config = new SimulationConfig(map.TickRateHz, map.Seed) { Combat = CombatRules.Default with { Doctrine = map.Doctrine } };
        var simulation = new Simulation(map.World.World, config);
        var leaders = Teams.Select(t => map.World.Origin.ToLocal(new EtrsPoint(t.Easting, t.Northing))).ToList();
        for (var t = 0; t < Teams.Length; t++)
        {
            var team = Teams[t];
            var enemy = leaders[(t + 1) % leaders.Count];
            for (var i = 0; i < Spread.Length; i++)
            {
                var id = $"{team.Prefix}-{i + 1}";
                var at = leaders[t] + Spread[i];
                simulation.AddUnit(id, at, MovementProfile.Infantry,
                    new Combatant(team.Side, RulesOfEngagement.FreeFire, WeaponProfile.Rifle, UnitId: team.Prefix));
                simulation.Issue(id, new Watch(enemy - at)); // each side watches toward the other (backlog 0004 S5a1)
            }
        }

        return simulation;
    }

    /// <summary>True when one side has nobody left in action, or nobody has fired for <see cref="QuietSeconds"/>.</summary>
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

        var lastShot = simulation.EventLog.OfType<ShotFired>().LastOrDefault()?.Time ?? 0;
        return simulation.TimeSeconds - lastShot >= QuietSeconds;
    }
}
