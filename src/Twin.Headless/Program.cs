using System.Globalization;
using Twin.Sim;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Terrain;

// Usage: dotnet run --project src/Twin.Headless -- [--seconds 120] [--seed 1]
//        dotnet run --project src/Twin.Headless -- replay <scenario | log.jsonl> [options]   (./dev replay, see ScenarioReplay)
if (args.Length > 0 && args[0] == "replay")
{
    return Twin.Headless.ScenarioReplay.Run(args[1..]);
}

var seconds = ReadOption(args, "--seconds", 120.0);
var seed = (ulong)ReadOption(args, "--seed", 1.0);

var terrain = HeightmapTerrain.Hill(1000, 1000, peak: new Vec2(600, 550), peakHeightMeters: 40, radiusMeters: 300);
var simulation = new Simulation(terrain, new SimulationConfig(TickRateHz: 20, Seed: seed));
simulation.AddUnit("alpha", new Vec2(150, 150), maxSpeedMps: 5);
simulation.AddUnit("bravo", new Vec2(250, 120), maxSpeedMps: 8);
simulation.Issue("alpha", new MoveTo(new Vec2(600, 550)));
simulation.Issue("bravo", new MoveTo(new Vec2(900, 900)));

var totalTicks = (long)Math.Ceiling(seconds * simulation.Config.TickRateHz);
for (long i = 0; i < totalTicks; i++)
{
    simulation.Step();
    if (simulation.Tick % (simulation.Config.TickRateHz * 10) == 0)
    {
        Report();
    }
}

if (simulation.Tick % (simulation.Config.TickRateHz * 10) != 0)
{
    Report();
}

Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"final state hash: {simulation.ComputeStateHash():X16}"));
return 0;

void Report()
{
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"t={simulation.TimeSeconds,7:0.0}s"));
    foreach (var unit in simulation.Units)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    {unit.Id,-6} {unit.Activity,-7} at {unit.Position}"));
    }
}

static double ReadOption(string[] args, string name, double fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length
        ? double.Parse(args[index + 1], CultureInfo.InvariantCulture)
        : fallback;
}
