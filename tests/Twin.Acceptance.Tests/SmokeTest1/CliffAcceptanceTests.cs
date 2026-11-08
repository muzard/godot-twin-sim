using Twin.Sim.Command;
using Twin.Sim.Events;
using Twin.Sim.Scenarios;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// On the real MML area the agent never crosses a Topographic Database cliff (docs/adr/0010): neither its trail nor
/// any route it planned. The st1 agent cannot rappel.
/// </summary>
public class CliffAcceptanceTests
{
    [Fact]
    public void St1_world_has_the_cliffs_of_the_area()
    {
        var world = ScenarioOnDisk.Load("st1").Definition.World.World;

        Assert.NotEmpty(world.Cliffs);
    }

    [Fact]
    public void St1_agent_cannot_rappel()
    {
        Assert.False(ScenarioOnDisk.Load("st1").Definition.Profile.CanRappel);
    }

    [Theory]
    [InlineData(LeaderAnswer.Grant)]
    [InlineData(LeaderAnswer.Deny)]
    public void The_agent_never_crosses_a_cliff_on_the_real_map(LeaderAnswer answer)
    {
        var scenario = ScenarioOnDisk.Load("st1");
        ScenarioDefinition definition = scenario.Definition;
        var id = definition.AgentId;

        var sim = scenario.Start(answer).RunUntil(
            s => s.Unit(id).IsIdle || s.Unit(id).Activity == UnitActivity.Blocked, TimeSpan.FromHours(2), "finished");

        var routes = sim.Events.OfType<PathPlanned>().Select(p => p.Waypoints).Append(sim.Trail(id));
        foreach (var route in routes)
        {
            foreach (var cliff in definition.World.World.Cliffs)
            {
                Assert.Empty(TestGeometry.Crossings(route, cliff));
            }
        }
    }
}
