using System.Diagnostics;
using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Events;
using Twin.Sim.Orders;
using Twin.Sim.Squads;
using Twin.Testing;
using Xunit.Abstractions;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// Squads on the real MML area (backlog 0004, S2d): the <c>scenarios/st1-ambush</c> scenario file (a squad of nine and
/// three red fighters on the st1 map, shared from <c>scenarios/st1/world</c>), and SM10, the time a squad takes to run
/// through the st1 corridor.
/// </summary>
public class St1AmbushTests(ITestOutputHelper output)
{
    [Fact]
    public void The_ambush_scenario_places_a_squad_of_nine_and_three_red_fighters_on_the_st1_map()
    {
        var ambush = ScenarioOnDisk.Load("st1-ambush");

        var sim = ambush.Start(LeaderAnswer.Grant);

        Assert.Equal(9, Assert.Single(sim.Simulation.Squads).Members.Count);
        Assert.Equal(3, sim.Simulation.Units.Count(u => u.Combatant?.Side == Side.Red));
        Assert.Equal(ScenarioOnDisk.Load("st1").Definition.World.Origin, ambush.Definition.World.Origin);
    }

    [Fact]
    public void In_the_ambush_the_squad_learns_of_red_and_halts_within_five_minutes()
    {
        var sim = ScenarioOnDisk.Load("st1-ambush").Start(LeaderAnswer.Grant);

        sim.RunUntil(s => s.Events.OfType<SquadHalted>().Any(), TimeSpan.FromMinutes(5), "the squad halts on a spotted enemy");

        Assert.StartsWith("red", sim.Events.OfType<SquadHalted>().First().EnemyId, StringComparison.Ordinal);
    }

    [Fact]
    public void SM10_a_squad_runs_through_the_st1_corridor_for_30_minutes_and_the_time_is_recorded()
    {
        var st1 = ScenarioOnDisk.Load("st1").Definition;
        var sim = Scenario.ForSquad()
            .WithTerrain(st1.World.World)
            .WithSquad(SpecSquad.Reference() with { Formation = SquadFormation.Wedge }, st1.Corridor.Centerline[0])
            .WithLeader(LeaderAnswer.Grant, after: TimeSpan.FromSeconds(30), permitRadiusMeters: 25)
            .WithPermissionRules(threshold: SpecV01.PermissionThreshold, timeout: TimeSpan.FromSeconds(120))
            .Build()
            .Order("rj", new MoveThroughCorridor(st1.Corridor));

        var clock = Stopwatch.StartNew();
        sim.RunFor(TimeSpan.FromMinutes(30));
        clock.Stop();

        output.WriteLine($"SM10: 30 min of a squad of nine on st1 took {clock.Elapsed.TotalSeconds:0.00} s " +
            $"({sim.Simulation.Tick} ticks); rj reached x = {sim.Unit("rj").Position.X:0} m.");
        Assert.True(sim.Unit("rj").Position.X > 330, "the squad should get past the forest road at x = 330 in 30 min");
    }
}
