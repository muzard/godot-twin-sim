#:property PublishAot=false
// Developer command line for the whole team. Run through ./dev (macOS/Linux) or dev.cmd (Windows).
// It is a .NET file-based app, so the .NET SDK is the only tool anyone needs to install.
//
//   ./dev setup       check prerequisites, build, run all tests
//   ./dev test        run the fast test suite (extra args go to dotnet test, e.g. --filter MoveTo)
//   ./dev check       everything CI runs: format check, build, tests, headless smoke run
//   ./dev format      apply code formatting
//   ./dev phase       show the TDD phase; ./dev phase spec | implement to switch it
//   ./dev run         run the headless simulation demo
//   ./dev replay <scenario> run a scenario (e.g. st1), write its event log to replays/ and replay the log;
//                     options --answer Grant|Deny|NoAnswer, --every <s>, --out <file>, --max-seconds <s>;
//                     a .jsonl path only replays that log; --godot opens the replay in the Godot client
//                     (with a .jsonl path, --scenario <name> names the map if the file name does not start with it);
//                     ./dev replay st1-combat runs a built-in fire fight on st1 instead of a scenario,
//                     ./dev replay st1-ambush a built-in ambush (a squad through the st1 corridor)
//   ./dev smoke       run only the acceptance tests on real map data (Smoke test I), with test output
//   ./dev docker-test run the core test suite in a clean Linux container and report the time (needs Docker)
//   ./dev prep <cmd>  run a command in the map preprocessing container (Python + GDAL, needs Docker)
using System.Diagnostics;

var root = FindRepoRoot();
var command = args.Length > 0 ? args[0] : "help";
var rest = args.Skip(1).ToArray();

return command switch
{
    "setup" => Setup(),
    "test" => Run("dotnet", ["test", "Twin.Core.slnf", "--nologo", .. rest]),
    "check" => Check(),
    "format" => Run("dotnet", ["format", "Twin.Core.slnf"]),
    "phase" => Phase(rest),
    "run" => Run("dotnet", ["run", "--project", "src/Twin.Headless", "--", .. rest]),
    "replay" => Replay(rest),
    "smoke" => Run("dotnet", ["test", "tests/Twin.Acceptance.Tests", "--nologo", "--logger", "console;verbosity=detailed", .. rest]),
    "docker-test" => DockerTest(),
    "prep" => Prep(rest),
    _ => Help(),
};

int Setup()
{
    Console.WriteLine("== Checking prerequisites");
    var ok = Run("dotnet", ["--version"]) == 0;
    if (Run("git", ["lfs", "version"], quiet: true) != 0)
    {
        Console.WriteLine("!! Git LFS not found. Install it (https://git-lfs.com), then run: git lfs install");
        ok = false;
    }

    CheckGodot();

    Console.WriteLine("== Restoring, building and testing");
    ok &= Run("dotnet", ["restore", "Twin.sln"]) == 0;
    ok &= Run("dotnet", ["build", "Twin.sln", "--no-restore", "--nologo"]) == 0;
    ok &= Run("dotnet", ["test", "Twin.Core.slnf", "--no-build", "--nologo"]) == 0;

    Console.WriteLine(ok
        ? "\nSetup complete. Next: read docs/workflow.md, then open src/Twin.Client in Godot."
        : "\nSetup found problems (see above). Fix them and run ./dev setup again.");
    return ok ? 0 : 1;
}

void CheckGodot()
{
    var wanted = File.ReadAllText(Path.Combine(root, ".godot-version")).Trim();
    var godot = FindGodot();
    var output = Capture(godot, "--version");
    if (output is null)
    {
        Console.WriteLine($"{GodotNotFound(godot, wanted)} Only needed for the visual client.");
    }
    else if (!output.StartsWith(wanted.Replace("-stable", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal) ||
             !output.Contains("mono", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"!! Found Godot {output.Trim()}, but this project uses {wanted} (.NET edition).");
    }
    else
    {
        Console.WriteLine($"Godot {output.Trim()} OK");
    }
}

int Check()
{
    string[][] steps =
    [
        ["format", "Twin.Core.slnf", "--verify-no-changes"],
        ["build", "Twin.sln", "--nologo"],
        ["test", "Twin.Core.slnf", "--no-build", "--nologo"],
        ["run", "--project", "src/Twin.Headless", "--no-build", "--", "--seconds", "60"],
    ];
    foreach (var step in steps)
    {
        if (Run("dotnet", step) != 0)
        {
            Console.WriteLine($"\nFAILED: dotnet {string.Join(' ', step)}");
            return 1;
        }
    }

    Console.WriteLine("\nAll checks passed.");
    return 0;
}

int Replay(string[] a)
{
    var options = a.ToList();
    if (!options.Remove("--godot"))
    {
        return Run("dotnet", ["run", "--project", "src/Twin.Headless", "--", "replay", .. options]);
    }

    // --scenario only names the map for the viewer; the headless runner does not take it.
    var scenarioIndex = options.IndexOf("--scenario");
    string? scenario = null;
    if (scenarioIndex >= 0 && scenarioIndex + 1 < options.Count)
    {
        scenario = options[scenarioIndex + 1];
        options.RemoveRange(scenarioIndex, 2);
    }

    var target = options.FirstOrDefault() ?? string.Empty;
    string log;
    if (target.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
    {
        log = target;
        if (!File.Exists(Path.Combine(root, log)))
        {
            Console.WriteLine($"Log file not found: {log}");
            return 1;
        }

        var name = Path.GetFileNameWithoutExtension(target);
        scenario ??= name.Contains('-') ? name[..name.LastIndexOf('-')] : name;
    }
    else
    {
        scenario = target;
        log = OptionValue(options, "--out") ?? string.Empty;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, root)));
        var isDemo = !Directory.Exists(Path.Combine(root, target)) && !Directory.Exists(Path.Combine(root, "scenarios", target));
        if (isDemo)
        {
            // A built-in demo such as st1-combat: the part before the last '-' names its map.
            scenario = name.Contains('-') ? name[..name.LastIndexOf('-')] : name;
        }

        if (log.Length == 0)
        {
            // Same names as the runner's own default (replays/<scenario>-<Answer>.jsonl, replays/<demo>.jsonl), passed
            // to it so both use the same file. The runner rejects any other answer.
            var answer = OptionValue(options, "--answer")?.ToLowerInvariant() switch
            {
                null or "grant" => "Grant",
                "deny" => "Deny",
                "noanswer" => "NoAnswer",
                var other => other,
            };
            log = Path.Combine("replays", isDemo ? $"{name}.jsonl" : $"{name}-{answer}.jsonl");
            options.AddRange(["--out", log]);
        }

        // Exit code 2 means the run timed out; its log is still worth looking at.
        var code = Run("dotnet", ["run", "--project", "src/Twin.Headless", "--", "replay", .. options]);
        if (code is not (0 or 2))
        {
            return code;
        }
    }

    var scenarioDir = Directory.Exists(Path.Combine(root, scenario)) ? scenario : Path.Combine("scenarios", scenario);
    if (!Directory.Exists(Path.Combine(root, scenarioDir)))
    {
        Console.WriteLine($"Scenario not found: {scenario}. Name the map with --scenario <name>.");
        return 1;
    }

    var godot = FindGodot();
    var wanted = File.ReadAllText(Path.Combine(root, ".godot-version")).Trim();
    if (Capture(godot, "--version") is null)
    {
        Console.WriteLine(GodotNotFound(godot, wanted));
        return 1;
    }

    Console.WriteLine("== Building the Godot client");
    if (Run("dotnet", ["build", "src/Twin.Client", "--nologo", "--verbosity", "quiet"]) != 0)
    {
        return 1;
    }

    Console.WriteLine($"== Opening {log} in Godot ({godot})");
    var exitCode = Run(godot, ["--path", Path.Combine(root, "src", "Twin.Client"), "res://Replay.tscn", "--",
        "--scenario", Path.GetFullPath(scenarioDir, root), "--log", Path.GetFullPath(log, root)]);
    if (exitCode != 0)
    {
        Console.WriteLine($"!! Godot exited with code {exitCode}.");
    }

    return exitCode;
}

static string? OptionValue(List<string> options, string name)
{
    var index = options.IndexOf(name);
    return index >= 0 && index + 1 < options.Count ? options[index + 1] : null;
}

string FindGodot()
{
    if (Environment.GetEnvironmentVariable("GODOT") is { Length: > 0 } fromEnvironment)
    {
        return ResolveGodotPath(fromEnvironment);
    }

    // macOS installs the app bundle without putting it on PATH.
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string[] bundles = ["/Applications/Godot_mono.app", Path.Combine(home, "Applications", "Godot_mono.app")];
    var bundle = bundles.FirstOrDefault(b => File.Exists(Path.Combine(b, "Contents", "MacOS", "Godot")));
    return bundle is null || Capture("godot", "--version") is not null ? "godot" : Path.Combine(bundle, "Contents", "MacOS", "Godot");
}

// GODOT is read as a shell would read it: ~ is the home directory, and a relative path is relative to the directory
// ./dev was started from (the launcher passes it in DEV_CALLER_DIR before it changes to the repository root). A macOS
// app bundle means the executable inside it. A bare name such as "godot" is left for PATH lookup.
static string ResolveGodotPath(string path)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (path == "~")
    {
        path = home;
    }
    else if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
    {
        path = Path.Combine(home, path[2..]);
    }

    if (path.Contains('/') || path.Contains('\\'))
    {
        var caller = Environment.GetEnvironmentVariable("DEV_CALLER_DIR") is { Length: > 0 } dir ? dir : Directory.GetCurrentDirectory();
        path = Path.GetFullPath(path, caller);
    }

    var inBundle = Path.Combine(path, "Contents", "MacOS", "Godot");
    return Directory.Exists(path) && File.Exists(inBundle) ? inBundle : path;
}

// Says why Godot could not be started instead of failing silently.
static string GodotNotFound(string godot, string wanted) =>
    $"!! Could not start Godot as '{godot}'. Install Godot {wanted} (.NET edition) from " +
    $"https://godotengine.org/download/archive/{wanted}/ and put it on PATH as 'godot', or set the GODOT environment " +
    "variable to its executable (a full path, ~/..., a path relative to where you run ./dev, or on macOS the .app). See GODOT_README.md.";

int DockerTest()
{
    const double LimitSeconds = 60; // Smoke test I, acceptance criterion 4
    Console.WriteLine("== Building test image (restore is cached between runs)");
    if (Run("docker", ["build", "--file", "docker/test.Dockerfile", "--tag", "twin-test", "."]) != 0)
    {
        Console.WriteLine("\nFAILED: docker build. Is Docker running?");
        return 1;
    }

    Console.WriteLine("== Running tests in the container");
    var started = Stopwatch.GetTimestamp();
    var code = Run("docker", ["run", "--rm", "twin-test"]);
    var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
    var verdict = seconds < LimitSeconds ? "within" : "OVER";
    Console.WriteLine($"\nContainer test run: {(code == 0 ? "green" : "RED")}, {seconds:0.0} s ({verdict} the {LimitSeconds:0} s limit).");
    return code;
}

int Prep(string[] a)
{
    if (a.Length == 0)
    {
        Console.WriteLine("Usage: ./dev prep <command> [args]   e.g. ./dev prep gdalinfo --version (see prep/README.md)");
        return 1;
    }

    if (Run("docker", ["build", "--quiet", "--tag", "twin-prep", "prep"], quiet: true) != 0)
    {
        Console.WriteLine("FAILED: docker build prep. Is Docker running? Rebuild verbosely: docker build -t twin-prep prep");
        return 1;
    }

    return Run("docker", ["run", "--rm", "--volume", $"{root}:/work", "twin-prep", .. a]);
}

int Phase(string[] a)
{
    var file = Path.Combine(root, ".claude", "state", "phase");
    if (a.Length == 0)
    {
        var current = File.Exists(file) ? File.ReadAllText(file).Trim() : "implement";
        Console.WriteLine($"TDD phase: {current}");
        return 0;
    }

    if (a[0] is not ("spec" or "implement"))
    {
        Console.WriteLine("Usage: ./dev phase [spec|implement]");
        return 1;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, a[0]);
    Console.WriteLine(a[0] == "spec"
        ? "TDD phase: spec. Tests may be written and changed; src/ is locked."
        : "TDD phase: implement. src/ is open; existing tests are locked (new test files are allowed).");
    return 0;
}

int Help()
{
    Console.WriteLine("Usage: ./dev <setup|test|check|format|phase|run|replay|smoke|docker-test|prep>  (see tools/dev.cs for details)");
    return 1;
}

int Run(string file, string[] arguments, bool quiet = false)
{
    try
    {
        var info = new ProcessStartInfo(file) { WorkingDirectory = root, RedirectStandardOutput = quiet, RedirectStandardError = quiet };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return -1;
    }
}

string? Capture(string file, string argument)
{
    try
    {
        var info = new ProcessStartInfo(file, argument) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return null;
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Twin.sln")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Run this from inside the repository.");
}
