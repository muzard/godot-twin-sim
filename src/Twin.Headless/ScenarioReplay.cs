using System.Globalization;
using Twin.Sim;
using Twin.Sim.Combat;
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
    /// <summary>Command line help, printed on any usage error.</summary>
    public const string Usage =
        "Usage: ./dev replay <scenario | log.jsonl> [--answer Grant|Deny|NoAnswer] [--every 60] [--out file.jsonl] [--max-seconds 7200]\n" +
        "  <scenario> is a directory or a name under scenarios/ (e.g. st1). The log goes to replays/<scenario>-<answer>.jsonl.\n" +
        "  st1-combat runs a built-in fire fight on st1 (two fire teams of three) to replays/st1-combat.jsonl.\n" +
        "  A squad scenario (e.g. st1-ambush) runs until its leader arrives or is blocked, one side has nobody in action, or nobody\n" +
        "  has fired for 120 s after the first shot.\n" +
        "  Exit code 2: the run was not over (agent idle or blocked, fight decided) when --max-seconds ran out.";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            Console.WriteLine(Usage);
            return 1;
        }

        var target = args[0];
        var options = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (Array.IndexOf(KnownOptions, args[i]) < 0 || i + 1 >= args.Length)
            {
                Console.WriteLine($"Unknown option or missing value: {args[i]}\n{Usage}");
                return 1;
            }

            options[args[i]] = args[i + 1];
        }

        if (!TryNumber(options, "--every", 60.0, out var every) || every <= 0 ||
            !TryNumber(options, "--max-seconds", TimeSpan.FromHours(2).TotalSeconds, out var maxSeconds) || maxSeconds <= 0)
        {
            Console.WriteLine($"--every and --max-seconds must be positive numbers.\n{Usage}");
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

        if (target == CombatDemo.Name)
        {
            if (options.ContainsKey("--answer"))
            {
                Console.WriteLine("--answer does not apply to the combat demo: it has no leader.");
                return 1;
            }

            var fight = CombatDemo.Create("scenarios");
            return RunAndReplay(
                fight, () => CombatDemo.IsOver(fight), CombatDemo.Name, options.GetValueOrDefault("--out") ?? Path.Combine("replays", $"{CombatDemo.Name}.jsonl"),
                () => string.Create(CultureInfo.InvariantCulture,
                    $"{fight.EventLog.OfType<ShotFired>().Count()} shots, {fight.Units.Count(u => u.Health == Health.OutOfAction)} out of action"),
                every, maxSeconds);
        }

        var dir = Directory.Exists(target) ? target : Path.Combine("scenarios", target);
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"Scenario not found: {target} (looked in {Path.GetFullPath(dir)})\n{Usage}");
            return 1;
        }

        var answerText = options.GetValueOrDefault("--answer") ?? nameof(LeaderAnswer.Grant);
        // Names only: Enum.TryParse would also take "1", which is easy to mistype and names the log differently.
        if (!Enum.GetNames<LeaderAnswer>().Contains(answerText, StringComparer.OrdinalIgnoreCase) ||
            !Enum.TryParse<LeaderAnswer>(answerText, ignoreCase: true, out var answer))
        {
            Console.WriteLine($"Unknown --answer '{answerText}'. Use Grant, Deny or NoAnswer.");
            return 1;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)));
        var scenario = ScenarioDefinition.Load(dir);
        var simulation = scenario.CreateSimulation(answer);
        var outFile = options.GetValueOrDefault("--out") ?? Path.Combine("replays", $"{name}-{answer}.jsonl");
        if (scenario.Squad is not null)
        {
            var end = new SquadRunEnd(simulation);
            return RunAndReplay(simulation, end.IsOver, $"{name} ({answer})", outFile, end.Outcome, every, maxSeconds);
        }

        var agent = simulation.GetUnit(scenario.AgentId);
        return RunAndReplay(
            simulation, () => agent.IsIdle || agent.Activity == UnitActivity.Blocked, $"{name} ({answer})",
            outFile, () => $"agent {agent.Id} {agent.Activity}", every, maxSeconds);
    }

    private static int RunAndReplay(
        Simulation simulation, Func<bool> isDone, string title, string outFile, Func<string> outcome, double every, double maxSeconds)
    {
        do
        {
            simulation.Step();
        }
        while (!isDone() && simulation.TimeSeconds < maxSeconds);

        var file = Path.GetFullPath(outFile);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, EventLog.ToJsonLines(simulation.EventLog));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Ran {title} to t={simulation.TimeSeconds:0.0} s, {outcome()}; {simulation.EventLog.Count} events -> {file}"));

        // Replay from the file alone, not from the simulation in memory.
        Print(EventLog.FromJsonLines(File.ReadAllText(file)), simulation.TimeSeconds, every);
        if (!isDone())
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"TIMED OUT: the run was not over after --max-seconds {maxSeconds:0.#}."));
            return 2;
        }

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

    private static readonly string[] KnownOptions = ["--answer", "--every", "--out", "--max-seconds"];

    private static bool TryNumber(SortedDictionary<string, string> options, string name, double fallback, out double value)
    {
        value = fallback;
        return !options.TryGetValue(name, out var text) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
