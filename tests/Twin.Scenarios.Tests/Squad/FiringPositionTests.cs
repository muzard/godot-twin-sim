using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Squads;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// A member in contact that cannot fire moves to a firing position of its own (backlog 0005, FP1, FP2, FP5). The layout
/// is <see cref="FireControlTests"/>'s: the reference squad in a row facing north from rj at (100, 61) to t1 at
/// (124, 61), its weapons never hitting; red-left at (60, 161) and red-right at (140, 161), holding fire. After the fire
/// distribution red-left lies in the support team's fire area. Here t1's rifle reaches only 100 m: red-left is 118.7 m
/// from it and red-right 101.3 m, so t1 can engage neither where it stands, but a point within its 30 m search radius
/// brings red-left into range.
/// </summary>
public class FiringPositionTests
{
    private static readonly TimeSpan TwoMinutes = TimeSpan.FromMinutes(2);
    private static readonly Vec2 Leader = new(100, 61);
    private static readonly Vec2 T1Start = new(124, 61);
    private static readonly Vec2 RedLeft = new(60, 161);
    private static readonly Vec2 RedRight = new(140, 161);
    private const double T1RangeMeters = 100;
    private const double Tolerance = 2;

    private static WeaponProfile NeverHits(SquadMemberSpec member) =>
        SpecSuppression.NeverHits(member.UnitId == "kk" ? SpecSuppression.MachineGun : SpecShooting.Rifle);

    private static WeaponProfile T1ShortOfRange(SquadMemberSpec member) => member.UnitId == "t1"
        ? SpecFiringPositions.ShortRange(SpecSuppression.NeverHits(SpecShooting.Rifle), T1RangeMeters)
        : NeverHits(member);

    private static ScenarioRun Squad(
        Func<SquadMemberSpec, WeaponProfile> weapons, RulesOfEngagement rules = RulesOfEngagement.ReturnFire, bool red = true)
    {
        var scenario = Scenario.ForSquadSpotting()
            .WithTerrain(TestMap.Create(widthMeters: 200, heightMeters: 200).Build())
            .WithSquad(SpecSquad.Reference(), Leader, rules: rules, weapons: weapons);
        if (red)
        {
            scenario.WithFighter("red-left", RedLeft, Side.Red, RulesOfEngagement.HoldFire)
                .WithFighter("red-right", RedRight, Side.Red, RulesOfEngagement.HoldFire);
        }

        return scenario.Build();
    }

    private static Vec2 StartOf(string unitId) =>
        Leader + new Vec2(SpecSquad.Reference().Members.Select(m => m.UnitId).ToList().IndexOf(unitId) * Scenario.SquadSpacingMeters, 0);

    /// <summary>Runs until the leader knows of both red fighters and has distributed fire, then until everyone acted on it.</summary>
    private static ScenarioRun Distributed(ScenarioRun sim)
    {
        sim.RunUntil(s => s.Simulation.KnowsOf("rj", "red-left") && s.Simulation.KnowsOf("rj", "red-right"), TwoMinutes, "rj knows of both");
        sim.RunUntil(s => s.Events.OfType<CommandIssued>().Any(e => e.AgentId == "rj" &&
            e.Command.StartsWith(SpecSquad.DirectSquadFireCommand, StringComparison.Ordinal)), TwoMinutes, "fire distributed");
        return sim.RunFor(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void FP1_a_member_that_cannot_engage_moves_to_a_firing_position_within_its_search_radius_and_fires_from_there()
    {
        var sim = Distributed(Squad(T1ShortOfRange));

        sim.RunUntil(s => s.ShotsBy("t1").Any(e => e.TargetId == "red-left"), TwoMinutes, "t1 fires at red-left");

        var at = sim.Unit("t1").Position.Xy;
        Assert.InRange(at.DistanceTo(T1Start), 1, SpecFiringPositions.FighterSearchMeters + Tolerance);
        Assert.True(at.DistanceTo(RedLeft) <= T1RangeMeters, $"t1 fired from {at}, {at.DistanceTo(RedLeft):0.0} m from red-left");
    }

    [Fact]
    public void FP2_a_member_that_can_engage_where_it_stands_does_not_move()
    {
        var sim = Distributed(Squad(NeverHits)).RunFor(TimeSpan.FromMinutes(1));

        Assert.All(SpecSquad.Reference().Members, m =>
            Assert.True(sim.Unit(m.UnitId).Position.Xy.DistanceTo(StartOf(m.UnitId)) <= 0.5,
                $"{m.UnitId} moved to {sim.Unit(m.UnitId).Position.Xy}"));
    }

    [Fact]
    public void FP2_a_member_under_hold_fire_does_not_move_to_a_firing_position()
    {
        var sim = Squad(T1ShortOfRange, rules: RulesOfEngagement.HoldFire);
        sim.RunUntil(s => s.Simulation.KnowsOf("t1", "red-left"), TwoMinutes, "t1 knows of red-left");

        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.True(sim.Unit("t1").Position.Xy.DistanceTo(T1Start) <= 0.5, $"t1 moved to {sim.Unit("t1").Position.Xy}");
    }

    [Fact]
    public void FP2_a_member_that_knows_of_no_enemy_does_not_move()
    {
        var sim = Squad(T1ShortOfRange, red: false);

        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.True(sim.Unit("t1").Position.Xy.DistanceTo(T1Start) <= 0.5, $"t1 moved to {sim.Unit("t1").Position.Xy}");
    }

    [Fact]
    public void FP5_a_member_not_suppressed_rushes_to_its_firing_position_crouching()
    {
        var sim = Distributed(Squad(T1ShortOfRange));

        sim.RunUntil(s => s.Unit("t1").Position.Xy.DistanceTo(T1Start) > 3, TwoMinutes, "t1 on its way");

        Assert.Equal(UnitActivity.MovingToFiringPosition, sim.Unit("t1").Activity);
        Assert.Equal(Posture.Crouching, sim.Unit("t1").Posture);
    }
}
