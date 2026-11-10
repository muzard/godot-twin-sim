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
    var godot = Environment.GetEnvironmentVariable("GODOT") ?? "godot";
    var output = Capture(godot, "--version");
    if (output is null)
    {
        Console.WriteLine($"!! Godot not found. Install Godot {wanted} (.NET edition) from " +
                          $"https://godotengine.org/download/archive/{wanted}/ and put it on PATH as 'godot' " +
                          "or set the GODOT environment variable. Only needed for the visual client.");
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
    Console.WriteLine("Usage: ./dev <setup|test|check|format|phase|run|smoke|docker-test|prep>  (see tools/dev.cs for details)");
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
