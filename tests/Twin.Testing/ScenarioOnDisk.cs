using System.Text.Json;
using Twin.Sim.Command;
using Twin.Sim.Scenarios;

namespace Twin.Testing;

/// <summary>
/// A scenario stored under <c>scenarios/&lt;name&gt;/</c> (scenario.json + preprocessed world), for acceptance tests
/// on real map data. The simulation reads the scenario through <see cref="ScenarioDefinition"/>; this class adds what
/// only tests need, such as the <c>expect</c> section a person fills in after studying the area.
/// <code>
/// var st1 = ScenarioOnDisk.Load("st1");
/// var sim = st1.Start(LeaderAnswer.Grant);   // agent placed, corridor order issued
/// </code>
/// </summary>
public sealed class ScenarioOnDisk
{
    private ScenarioOnDisk(string name, ScenarioDefinition definition, bool? detourWithoutCrossing)
    {
        Name = name;
        Definition = definition;
        DetourWithoutCrossing = detourWithoutCrossing;
    }

    /// <summary>The scenario's directory name, e.g. <c>st1</c>.</summary>
    public string Name { get; }

    /// <summary>The scenario as the simulation reads it.</summary>
    public ScenarioDefinition Definition { get; }

    /// <summary>
    /// <c>expect.detourWithoutCrossing</c> in scenario.json: whether the agent can reach B without crossing any hard
    /// boundary, inside the corridor. Null if the scenario does not say.
    /// </summary>
    public bool? DetourWithoutCrossing { get; }

    /// <summary>Loads <c>scenarios/&lt;name&gt;/scenario.json</c> and its world. Fails clearly if files are missing.</summary>
    public static ScenarioOnDisk Load(string name)
    {
        var file = ScenarioFiles.Require(name, "scenario.json");
        ScenarioFiles.Require(name, Path.Combine("world", "manifest.json"));
        var definition = ScenarioDefinition.Load(ScenarioFiles.Directory(name));

        using var json = JsonDocument.Parse(File.ReadAllText(file));
        bool? detour = json.RootElement.TryGetProperty("expect", out var expect) &&
                       expect.TryGetProperty("detourWithoutCrossing", out var value)
            ? value.GetBoolean()
            : null;
        return new ScenarioOnDisk(name, definition, detour);
    }

    /// <summary>
    /// Creates the simulation with the scenario's agent and corridor order, the scripted leader answering
    /// <paramref name="answer"/> (delay and permit radius from the file). No ticks have run yet.
    /// </summary>
    public ScenarioRun Start(LeaderAnswer answer) => new(Definition.CreateSimulation(answer));
}
