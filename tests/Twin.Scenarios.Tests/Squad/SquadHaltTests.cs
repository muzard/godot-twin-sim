using Twin.Sim.Combat;
using Twin.Sim.Events;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The squad halts on a spotted enemy (backlog 0004 S5a1: SP8, SP10). A member that knows of an enemy stops and
/// crouches at once. When the leader learns of an enemy it did not know of, the whole squad halts at once, with no
/// command delay, and every member crouches. The squad's movement order is suspended, not lost:
/// <see cref="ResumeMovement"/> to the leader takes the squad on in formation, and members take back their posture.
/// The wedge moves east; red stands up ahead and to the right when the leader passes x = 200.
/// </summary>
public class SquadHaltTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    /// <summary>Runs the moving wedge until its leader knows of red; returns that moment.</summary>
    private static (ScenarioRun Sim, double LeaderKnowsAt) UntilTheLeaderKnows()
    {
        var sim = SpottingLayouts.AheadOnTheMove().RedStandsUp().RunUntilKnows("rj", "red", OneMinute);
        return (sim, sim.TimeSeconds);
    }

    [Fact]
    public void SP8_a_moving_spotter_stops_at_once_before_its_leader_knows()
    {
        var sim = SpottingLayouts.AheadOnTheMove().RedStandsUp();
        var spotted = sim.RunUntilAnyoneSpots("red", OneMinute);
        Assert.NotEqual("rj", spotted.AgentId); // the leader watches ahead; red is off to the right

        sim.Step();

        sim.Unit(spotted.AgentId).Should().HaveGroundSpeed(0);
        Assert.False(sim.Simulation.KnowsOf("rj", "red"), "the spotter has not reported yet");
        Assert.False(sim.Squad("r1").IsHalted);
    }

    [Fact]
    public void SP10_when_the_leader_learns_of_an_enemy_the_whole_squad_halts_at_once()
    {
        var (sim, knowsAt) = UntilTheLeaderKnows();

        sim.Step();

        Assert.True(sim.Squad("r1").IsHalted);
        var halted = Assert.Single(sim.Events.OfType<SquadHalted>());
        Assert.Equal(("rj", "r1", "red"), (halted.AgentId, halted.SquadId, halted.EnemyId));
        Assert.Equal(knowsAt, halted.Time);
        foreach (var id in SpottingLayouts.Members)
        {
            sim.Unit(id).Should().HaveGroundSpeed(0);
            Assert.NotEqual(UnitActivity.Moving, sim.Unit(id).Activity);
        }
    }

    [Fact]
    public void SP10_every_member_crouches_when_the_squad_halts()
    {
        var (sim, knowsAt) = UntilTheLeaderKnows();

        sim.RunUntil(s => s.TimeSeconds >= knowsAt + SpecShooting.Rules.PostureChangeSeconds + s.Simulation.Config.TickSeconds - 1e-9,
            OneMinute, "a posture change later");

        foreach (var id in SpottingLayouts.Members)
        {
            sim.Unit(id).Should().BeIn(Posture.Crouching);
        }
    }

    [Fact]
    public void SP10_the_halted_squad_stays_where_it_is_until_its_leader_is_ordered_on()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.Step();
        var where = SpottingLayouts.Members.ToDictionary(id => id, id => sim.Unit(id).Position.Xy);

        sim.RunFor(TimeSpan.FromMinutes(2));

        Assert.True(sim.Squad("r1").IsHalted);
        foreach (var (id, position) in where)
        {
            sim.Unit(id).Should().BeAt(position);
        }
    }

    [Fact]
    public void SP10_resume_movement_takes_the_squad_on_to_its_destination_in_formation()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.RunFor(TimeSpan.FromSeconds(10));

        sim.Order("rj", new ResumeMovement()).RunUntilSquadSettled("r1", SpottingLayouts.TenMinutes);

        Assert.False(sim.Squad("r1").IsHalted);
        Assert.Single(sim.Events.OfType<SquadResumed>(), e => e.AgentId == "rj" && e.SquadId == "r1");
        sim.Unit("rj").Should().BeAt(SpottingLayouts.Destination).BeIdle();
        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
    }

    [Fact]
    public void SP10_after_resuming_members_take_back_their_posture_and_the_known_enemy_does_not_halt_the_squad_again()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.RunFor(TimeSpan.FromSeconds(10));

        sim.Order("rj", new ResumeMovement()).RunUntilSquadSettled("r1", SpottingLayouts.TenMinutes);

        Assert.Single(sim.Events.OfType<SquadHalted>());
        foreach (var id in SpottingLayouts.Members)
        {
            sim.Unit(id).Should().BeIn(Posture.Standing);
        }
    }

    [Fact]
    public void SP10_a_new_movement_order_to_the_leader_ends_the_halt_and_the_squad_follows_it()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.RunFor(TimeSpan.FromSeconds(5));
        var elsewhere = new Vec2(300, 150);

        sim.Order("rj", new MoveTo(elsewhere)).RunUntilSquadSettled("r1", SpottingLayouts.TenMinutes);

        Assert.False(sim.Squad("r1").IsHalted);
        Assert.Single(sim.Events.OfType<SquadResumed>());
        sim.Unit("rj").Should().BeAt(elsewhere).BeIdle();
        sim.ShouldAllBeInSlots("r1", toleranceMeters: 1);
        foreach (var id in SpottingLayouts.Members)
        {
            sim.Unit(id).Should().BeIn(Posture.Standing);
        }
    }

    [Fact]
    public void SP10_resume_movement_after_a_new_order_does_not_bring_back_the_suspended_one()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.RunFor(TimeSpan.FromSeconds(5));
        var elsewhere = new Vec2(300, 150);
        sim.Order("rj", new MoveTo(elsewhere)).RunUntilSquadSettled("r1", SpottingLayouts.TenMinutes);

        sim.Order("rj", new ResumeMovement()).RunFor(TimeSpan.FromSeconds(30));

        sim.Unit("rj").Should().BeAt(elsewhere).BeIdle().HaveNoOrder();
    }

    [Fact]
    public void SP10_a_squad_halted_while_crawling_moves_on_crawling()
    {
        // "Maahan!" on the move: the squad crawls on. It halts on red (crouching), and moves on prone again.
        var sim = SpottingLayouts.AheadOnTheMove();
        sim.Command("rj", new SetSquadPosture(Posture.Prone));
        sim.RunFor(TimeSpan.FromSeconds(10));
        sim.RedStandsUp().RunUntilKnows("rj", "red", OneMinute).RunFor(TimeSpan.FromSeconds(10));

        sim.Order("rj", new ResumeMovement()).RunFor(TimeSpan.FromSeconds(20));

        foreach (var id in SpottingLayouts.Members)
        {
            sim.Unit(id).Should().BeIn(Posture.Prone);
        }
    }

    [Fact]
    public void SP10_a_successor_who_knows_of_an_enemy_halts_the_squad_when_it_takes_command()
    {
        // Nobody hears anybody (voice range 11 m): vj spots red, but rj never learns of it. Red shoots rj out of action,
        // and vj, the second-in-command, takes command knowing of red.
        var sim = SpottingLayouts.RightOfFile(voiceRangeMeters: 11);
        sim.RedStandsUp().RunUntilKnows("vj", "red", OneMinute);
        Assert.False(sim.Squad("r1").IsHalted);
        sim.Order("red", new Watch(sim.Unit("rj").Position.Xy - sim.Unit("red").Position.Xy)).Step();

        sim.RunUntilOutOfAction("rj", "red", TimeSpan.FromMinutes(5));
        sim.RunUntil(s => s.Squad("r1").LeaderId == "vj", OneMinute, "vj takes command");
        sim.Step();

        Assert.True(sim.Squad("r1").IsHalted);
        var halted = Assert.Single(sim.Events.OfType<SquadHalted>());
        Assert.Equal(("vj", "red"), (halted.AgentId, halted.EnemyId));
    }

    [Fact]
    public void SP10_a_member_ordered_directly_during_the_halt_carries_out_its_order()
    {
        var (sim, _) = UntilTheLeaderKnows();
        sim.RunFor(TimeSpan.FromSeconds(5));
        var there = sim.Unit("k1").Position.Xy + new Vec2(0, 20);

        sim.Order("k1", new MoveTo(there)).RunUntil(s => s.Unit("k1").IsIdle && s.Unit("k1").CurrentOrder is null, OneMinute, "k1 arrives");

        sim.Unit("k1").Should().BeAt(there);
        Assert.True(sim.Squad("r1").IsHalted, "the rest of the squad stays halted");
    }

    [Fact]
    public void SP10_resume_movement_is_rejected_unless_it_goes_to_a_squad_leader()
    {
        var (sim, _) = UntilTheLeaderKnows();

        Assert.Throws<OrderRejectedException>(() => sim.Order("k1", new ResumeMovement()));
        Assert.Throws<OrderRejectedException>(() => sim.Order("red", new ResumeMovement()));
        Assert.Throws<OrderRejectedException>(() => sim.Order("nobody", new ResumeMovement()));
    }
}
