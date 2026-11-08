using Twin.Testing;

namespace Twin.Acceptance.Tests.Environment;

/// <summary>
/// Checks the plumbing the Smoke test I acceptance tests rely on: the test run can see the repository's
/// scenarios directory, locally and inside the Docker test container.
/// </summary>
public class ScenarioDataTests
{
    [Fact]
    public void Test_run_finds_the_smoke_test_scenario_directory()
    {
        var directory = ScenarioFiles.Directory("st1");

        Assert.True(File.Exists(Path.Combine(directory, "README.md")), $"Expected a README.md in {directory}.");
    }

    [Fact]
    public void Missing_scenario_file_fails_with_a_clear_message()
    {
        var error = Assert.Throws<ScenarioAssertionException>(() => ScenarioFiles.Require("st1", "no-such-file.bin"));

        Assert.Contains("scenarios/st1/no-such-file.bin", error.Message.Replace('\\', '/'), StringComparison.Ordinal);
    }
}
