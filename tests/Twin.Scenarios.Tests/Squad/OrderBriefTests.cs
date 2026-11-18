using Twin.Sim.Combat;
using Twin.Sim.Command;
using Twin.Sim.Corridors;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The order brief and succession on the move (backlog 0004, S2b: SU1–SU4). When the RJ gets a movement order it tells it
/// to the squad through the chain of command; each member knows it once it acts on it. A successor that heard the brief
/// carries on with the order; one that did not asks the platoon leader, who repeats it after the leader's delay.
/// </summary>
public class OrderBriefTests
{
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LeaderDelay = TimeSpan.FromSeconds(20);
    private static readonly Vec2 Start = new(80, 101);
    private static readonly Vec2 B = new(420, 101);
    private static readonly MoveThroughCorridor Order =
        new(new Corridor([Start, B], 60, SpecV01.SoftMargin, SpecV01.SoftPenalty, HardBoundaries: []));

    // The reference squad on open ground; red stands 5 m north of rj's start and holds fire until ordered.
    private static ScenarioRun SquadWithAnEnemyNextToItsLeader(
        LeaderAnswer answer = LeaderAnswer.Grant, Twin.Sim.Squads.SquadDoctrine? doctrine = null)
    {
        var scenario = Scenario.ForSquad()
            .WithTerrain(TestMap.Create(widthMeters: 500, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference(), Start)
            .WithFighter("red", new Vec2(80, 106), Side.Red, RulesOfEngagement.HoldFire)
            .WithLeader(answer, after: LeaderDelay, permitRadiusMeters: 10);
        if (doctrine is not null)
        {
            scenario.WithSquadDoctrine(doctrine);
        }

        return scenario.Build();
    }

    [Fact]
    public void SU1_the_leader_briefs_its_movement_order_to_the_squad_through_the_chain_of_command()
    {
        var sim = SquadWithAnEnemyNextToItsLeader();

        var brief = sim.Command("rj", Order);
        sim.RunFor(TimeSpan.FromSeconds(5));

        Assert.Contains(nameof(MoveThroughCorridor), brief.Command, StringComparison.Ordinal);
        Assert.True(sim.Squad("r1").KnowsOrder("rj"));
        Assert.True(sim.Squad("r1").KnowsOrder("k2"));  // experienced: 1 s
        Assert.False(sim.Squad("r1").KnowsOrder("k1")); // inexperienced, through pj: 8 s
        sim.RunFor(TimeSpan.FromSeconds(30));
        sim.ShouldHaveActedOn("k1", brief,
            SpecSquad.InexperiencedReactionSeconds + SpecSquad.RelaySeconds + SpecSquad.InexperiencedReactionSeconds);
        Assert.True(sim.Squad("r1").KnowsOrder("k1"));
    }

    [Fact]
    public void SU2_a_successor_that_heard_the_brief_carries_on_with_the_order()
    {
        var sim = SquadWithAnEnemyNextToItsLeader();
        sim.Command("rj", Order);
        sim.RunFor(TimeSpan.FromSeconds(5)); // vj (experienced) has heard it after 1 s

        sim.RunUntilOutOfAction("rj", "red", TenMinutes);
        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", TimeSpan.FromSeconds(30), "vj in command");

        Assert.Equal(Order, sim.Unit("vj").CurrentOrder);
        Assert.Empty(sim.Events.OfType<OrderRequested>());
        // rj lies out of action in the squad, so (S2a, provisional) the squad waits; vj holds the order meanwhile. vj stood
        // near red's fire at rj, so it is waiting for the squad once its suppression and getting up are over.
        sim.RunUntil(s => s.Unit("vj").Activity == UnitActivity.WaitingForSquad, TimeSpan.FromMinutes(2), "vj waiting for the squad");
        Assert.Equal(Order, sim.Unit("vj").CurrentOrder);
    }

    // Everyone reacts to a command only after 120 s, so vj has not heard the brief when rj falls a few seconds later.
    private static Twin.Sim.Squads.SquadDoctrine SlowToHear => SpecSquad.Doctrine with
    {
        ExperiencedReactionSeconds = 120,
        InexperiencedReactionSeconds = 120,
    };

    [Fact]
    public void SU3_a_successor_that_did_not_hear_the_brief_asks_the_platoon_leader_to_repeat_it()
    {
        var sim = SquadWithAnEnemyNextToItsLeader(doctrine: SlowToHear);
        sim.Command("rj", Order);
        sim.RunUntilOutOfAction("rj", "red", TenMinutes);
        Assert.False(sim.Squad("r1").KnowsOrder("vj"), "precondition: vj has not heard the brief");

        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", TimeSpan.FromSeconds(30), "vj in command");

        var request = Assert.Single(sim.Events.OfType<OrderRequested>());
        Assert.Equal(("vj", "r1"), (request.AgentId, request.SquadId));
        Assert.Null(sim.Unit("vj").CurrentOrder);
        sim.RunFor(LeaderDelay + TimeSpan.FromSeconds(1));
        var repeated = Assert.Single(sim.Events.OfType<OrderRepeated>());
        Assert.Equal(("vj", "r1"), (repeated.AgentId, repeated.SquadId));
        Assert.InRange(repeated.Time, request.Time + LeaderDelay.TotalSeconds - 1e-9, request.Time + LeaderDelay.TotalSeconds + SpecV01.DtSeconds + 1e-9);
        Assert.Equal(Order, sim.Unit("vj").CurrentOrder);
    }

    [Fact]
    public void SU3_the_successor_briefs_the_repeated_order_to_the_squad()
    {
        var sim = SquadWithAnEnemyNextToItsLeader(doctrine: SlowToHear);
        sim.Command("rj", Order);
        sim.RunUntilOutOfAction("rj", "red", TenMinutes);

        sim.RunUntil(s => s.Events.OfType<OrderRepeated>().Any(), TimeSpan.FromMinutes(2), "order repeated");
        sim.Step();

        Assert.Contains(sim.Events.OfType<CommandIssued>(), e => e.AgentId == "vj" && e.Command.Contains(nameof(MoveThroughCorridor), StringComparison.Ordinal));
    }

    [Fact]
    public void SU4_a_platoon_leader_that_never_answers_leaves_the_squad_holding_its_ground()
    {
        var sim = SquadWithAnEnemyNextToItsLeader(LeaderAnswer.NoAnswer, SlowToHear);
        sim.Command("rj", Order);
        sim.RunUntilOutOfAction("rj", "red", TenMinutes);
        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", TimeSpan.FromSeconds(30), "vj in command");
        var members = SpecSquad.Reference().Members.Select(m => m.UnitId).Where(id => id != "rj").ToList();
        var before = members.ToDictionary(id => id, id => sim.Unit(id).Position.Xy);

        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.Empty(sim.Events.OfType<OrderRepeated>());
        Assert.Null(sim.Unit("vj").CurrentOrder);
        foreach (var id in members)
        {
            Assert.True(sim.Unit(id).Position.Xy.DistanceTo(before[id]) < 0.01, $"{id} moved without an order");
        }
    }
}
