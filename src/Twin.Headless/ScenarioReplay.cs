using System.Globalization;
using Twin.Sim.Command;
using Twin.Sim.Events;
using Twin.Sim.Scenarios;
using Twin.Sim.Units;

namespace Twin.Headless;

/// <summary>
/// Runs a scenario from <c>scenarios/</c>, writes its event log as JSON Lines, then rebuilds the state from the
/// log alone (<see cref="EventReplay"/>) and prints it at fixed steps of simulated time. Given a <c>.jsonl</c>
/// file instead of a scenario, it only replays that log.
/// </summary>
internal static class ScenarioReplay
{
    public const string Usage =
        "Usage: ./dev replay <scenario | log.jsonl> [--answer Grant|Deny|NoAnswer] [--every 60] [--out file.jsonl] [--max-seconds 7200]\n" +
        "  <scenario> is a directory or a name under scenarios/ (e.g. st1). The log goes to replays/<scenario>-<answer>.jsonl.";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.WriteLine(Usage);
            return 1;
        }

        var target = args[0];
        var every = Option(args, "--every", 60.0);
        if (every <= 0)
        {
            Console.WriteLine("--every must be positive.");
            return 1;
        }

        if (target.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(target))
            {
                Console.WriteLine($"Log file not found: {target}");
                return 1;
            }

            var events = EventLog.FromJsonLines(File.ReadAllText(target));
            var end = events.Count > 0 ? events[^1].Time : 0.0;
            Print(events, end, every);
            return 0;
        }

        var dir = Directory.Exists(target) ? target : Path.Combine("scenarios", target);
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"Scenario not found: {target} (looked in {Path.GetFullPath(dir)})\n{Usage}");
            return 1;
        }

        var answerText = OptionText(args, "--answer") ?? nameof(LeaderAnswer.Grant);
        if (!Enum.TryParse<LeaderAnswer>(answerText, ignoreCase: true, out var answer) || !Enum.IsDefined(answer))
        {
            Console.WriteLine($"Unknown --answer '{answerText}'. Use Grant, Deny or NoAnswer.");
            return 1;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)));
        var file = Path.GetFullPath(OptionText(args, "--out") ?? Path.Combine("replays", $"{name}-{answer}.jsonl"));
        var maxSeconds = Option(args, "--max-seconds", TimeSpan.FromHours(2).TotalSeconds);

        var scenario = ScenarioDefinition.Load(dir);
        var simulation = scenario.CreateSimulation(answer);
        var agent = simulation.GetUnit(scenario.AgentId);
        do
        {
            simulation.Step();
        }
        while (!(agent.IsIdle || agent.Activity == UnitActivity.Blocked) && simulation.TimeSeconds < maxSeconds);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, EventLog.ToJsonLines(simulation.EventLog));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Ran {name} ({answer}) to t={simulation.TimeSeconds:0.0} s, agent {agent.Id} {agent.Activity}; {simulation.EventLog.Count} events -> {file}"));

        // Replay from the file alone, not from the simulation in memory.
        Print(EventLog.FromJsonLines(File.ReadAllText(file)), simulation.TimeSeconds, every);
        return 0;
    }

    private static void Print(IReadOnlyList<SimEvent> events, double endSeconds, double every)
    {
        var steps = (long)Math.Floor((endSeconds / every) + EventReplay.TimeTolerance);
        for (long i = 0; i <= steps; i++)
        {
            Show(EventReplay.SnapshotAt(events, i * every));
        }

        if (endSeconds - (steps * every) > EventReplay.TimeTolerance)
        {
            Show(EventReplay.SnapshotAt(events, endSeconds));
        }
    }

    private static void Show(WorldSnapshot snapshot)
    {
        foreach (var a in snapshot.Agents)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"t={snapshot.TimeSeconds,7:0.0} s  {a.Id,-8} {a.Activity,-21} {a.Health,-10} {a.Posture,-9} " +
                $"{(a.Suppressed ? "suppressed" : string.Empty),-10} x={a.Position.X,7:0.0} y={a.Position.Y,7:0.0} z={a.Position.Z,6:0.0}"));
        }
    }

    private static string? OptionText(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Option(string[] args, string name, double fallback)
    {
        var text = OptionText(args, name);
        return text is null ? fallback : double.Parse(text, CultureInfo.InvariantCulture);
    }
}
