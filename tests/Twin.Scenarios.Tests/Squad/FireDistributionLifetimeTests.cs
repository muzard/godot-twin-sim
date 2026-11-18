using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// How long a fire distribution lasts (backlog 0004 S5b, decided with the product owner in review, 2026-11-19): a
/// successor that knows of enemies distributes fire its reaction time after it takes command, and the squad moving on
/// (<see cref="ResumeMovement"/> or a new movement order to the leader) drops the fire areas at once. The layout is
/// <see cref="FireControlTests"/>'s: the squad in a row facing north from rj at (100, 61), red-left and red-right 100 m
/// north, holding fire; here red-killer, 60 m west of rj, holds fire until ordered to fire at rj with a rifle that hits.
/// </summary>
public class FireDistributionLifetimeTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);

    private static WeaponProfile NeverHits(SquadMemberSpec member) =>
        SpecSuppression.NeverHits(member.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);

    private static ScenarioRun Squad() => Scenario.ForSquadSpotting()
        .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 200).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(100, 61), rules: RulesOfEngagement.ReturnFire, weapons: NeverHits)
        .WithFighter("red-left", new Vec2(60, 161), Side.Red, RulesOfEngagement.HoldFire)
        .WithFighter("red-right", new Vec2(140, 161), Side.Red, RulesOfEngagement.HoldFire)
        .WithFighter("red-killer", new Vec2(40, 61), Side.Red, RulesOfEngagement.HoldFire, experience: Experience.SpecialForces)
        .Build()
        .Order("red-killer", new Watch(new Vec2(1, 0)));

    private static bool IsFireCommand(CommandIssued e) => e.Command.StartsWith(SpecSquad.DirectSquadFireCommand, StringComparison.Ordinal);

    private static ScenarioRun Distributed(ScenarioRun sim) =>
        sim.RunUntil(s => s.Unit("k1").FireArea is not null, TwoMinutes, "k1 has its fire area").RunFor(TimeSpan.FromSeconds(5));

    [Fact]
    public void A_successor_that_knows_of_enemies_distributes_fire_its_reaction_time_after_taking_command()
    {
        var sim = Distributed(Squad());
        sim.RunUntilKnows("red-killer", "rj", TwoMinutes);
        sim.RunUntilOutOfAction("rj", "red-killer", TimeSpan.FromMinutes(5));

        sim.RunUntil(s => s.Events.OfType<CommandIssued>().Any(e => e.AgentId == "vj" && IsFireCommand(e)), TwoMinutes, "vj distributes fire");

        var change = sim.Events.OfType<LeaderChanged>().Single();
        Assert.Equal("vj", change.AgentId);
        sim.ShouldBeAt(sim.Events.OfType<CommandIssued>().First(e => e.AgentId == "vj" && IsFireCommand(e)),
            SpecSquad.ExperiencedReactionSeconds, change.Time, "vj's fire command");
    }

    [Fact]
    public void Resuming_the_movement_drops_the_fire_areas_at_once()
    {
        var sim = Distributed(Squad());

        sim.Order("rj", new ResumeMovement()).Step();

        Assert.Null(sim.Squad("r1").Front);
        Assert.All(SpecSquad.Reference().Members, m => Assert.Null(sim.Unit(m.UnitId).FireArea));
    }

    [Fact]
    public void A_new_movement_order_drops_the_fire_areas_at_once()
    {
        var sim = Distributed(Squad());

        sim.Order("rj", new MoveTo(new Vec2(100, 20))).Step();

        Assert.All(SpecSquad.Reference().Members, m => Assert.Null(sim.Unit(m.UnitId).FireArea));
    }
}
