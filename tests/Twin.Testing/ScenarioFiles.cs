namespace Twin.Testing;

/// <summary>
/// Finds scenario data checked into the repository under <c>scenarios/</c>, for example the preprocessed
/// map area of Smoke test I in <c>scenarios/st1/</c>. Works from any test output directory, locally and in
/// the Docker test container, because it searches upward for <c>Twin.sln</c>.
/// </summary>
public static class ScenarioFiles
{
    private const string LfsPointerPrefix = "version https://git-lfs.github.com/spec/";

    /// <summary>The repository root (the directory that holds <c>Twin.sln</c>).</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>The <c>scenarios/</c> directory.</summary>
    public static string ScenariosRoot => Path.Combine(RepoRoot, "scenarios");

    /// <summary>The directory of one scenario, e.g. <c>Directory("st1")</c>. Fails if it does not exist.</summary>
    public static string Directory(string scenario)
    {
        var path = Path.Combine(ScenariosRoot, scenario);
        if (!System.IO.Directory.Exists(path))
        {
            throw new ScenarioAssertionException(
                $"Scenario directory '{Path.GetRelativePath(RepoRoot, path)}' does not exist. " +
                "See scenarios/README.md for the expected layout.");
        }

        return path;
    }

    /// <summary>
    /// The full path of a file inside a scenario, e.g. <c>Require("st1", "world/manifest.json")</c>.
    /// Fails with a clear message if the file is missing or is an unfetched Git LFS pointer,
    /// so a test never runs against placeholder data.
    /// </summary>
    public static string Require(string scenario, string relativePath)
    {
        var path = Path.Combine(Directory(scenario), relativePath);
        var shown = Path.GetRelativePath(RepoRoot, path);
        if (!File.Exists(path))
        {
            throw new ScenarioAssertionException(
                $"Scenario file '{shown}' does not exist. Run the map preprocessing (prep/README.md) or fetch the data.");
        }

        if (IsLfsPointer(path))
        {
            throw new ScenarioAssertionException(
                $"Scenario file '{shown}' is a Git LFS pointer, not the data. Run: git lfs install && git lfs pull");
        }

        return path;
    }

    private static bool IsLfsPointer(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 1024)
        {
            return false;
        }

        using var reader = new StreamReader(path);
        var buffer = new char[LfsPointerPrefix.Length];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read) == LfsPointerPrefix;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Twin.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find Twin.sln above " + AppContext.BaseDirectory);
    }
}
