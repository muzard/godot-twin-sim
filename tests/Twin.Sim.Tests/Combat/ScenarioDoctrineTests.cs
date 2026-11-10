using Twin.Sim.Command;
using Twin.Sim.Scenarios;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.Combat;

/// <summary>The doctrine is read from scenario.json, so it can be changed without changing code.</summary>
public class ScenarioDoctrineTests
{
    private const string ScenarioWithoutDoctrine = """
        {
          "schemaVersion": 1,
          "agent": { "id": "alpha", "start": [385001.0, 6672001.0] },
          "corridor": { "centerline": [[385001.0, 6672001.0], [385005.0, 6672003.0]], "width": 10, "softMargin": 5, "softPenalty": 1 },
          "leader": { "answer": "grant", "delaySeconds": 30, "permitRadius": 25 }
        }
        """;

    private static ScenarioDefinition Load(string scenarioJson)
    {
        using var files = TestWorldFiles.Write(3, 2, [1, 2, 3, 4, 5, 6], Enumerable.Repeat(TerrainClass.Open, 6).ToArray());
        var directory = Path.Combine(Path.GetTempPath(), "twin-scenario-" + Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "world"));
            foreach (var file in Directory.GetFiles(files.Directory))
            {
                File.Copy(file, Path.Combine(directory, "world", Path.GetFileName(file)));
            }

            File.WriteAllText(Path.Combine(directory, "scenario.json"), scenarioJson);
            return ScenarioDefinition.Load(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Without_a_doctrine_section_the_default_doctrine_applies()
    {
        Assert.Equal(Doctrine.Default, Load(ScenarioWithoutDoctrine).Doctrine);
    }

    [Fact]
    public void The_doctrine_section_sets_every_rule()
    {
        var json = ScenarioWithoutDoctrine.TrimEnd().TrimEnd('}') +
            """, "doctrine": { "returnFireOverridesFireAt": false, "permissionAnswers": "immediately" } }""";

        var doctrine = Load(json).Doctrine;

        Assert.False(doctrine.ReturnFireOverridesFireAt);
        Assert.Equal(PermissionAnswerTiming.Immediately, doctrine.PermissionAnswers);
    }

    [Fact]
    public void The_scenario_doctrine_reaches_the_simulation()
    {
        var json = ScenarioWithoutDoctrine.TrimEnd().TrimEnd('}') + """, "doctrine": { "returnFireOverridesFireAt": false } }""";

        var simulation = Load(json).CreateSimulation(LeaderAnswer.Grant);

        Assert.False(simulation.Config.Combat.Doctrine.ReturnFireOverridesFireAt);
    }
}
