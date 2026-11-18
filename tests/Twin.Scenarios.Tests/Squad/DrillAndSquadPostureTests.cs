using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// The drill wins over a squad posture command (backlog 0004 S5a2; the two limitations S1 left with 0003's drill). A
/// member in the drill keeps the drill's posture when a squad posture command reaches it, and takes the commanded posture
/// when the drill ends. <see cref="SetSquadPosture"/> does not end the squad leader's own drill. Red fires a rifle that
/// never hits; the squad returns fire, so a member shot at goes into the drill.
/// </summary>
public class DrillAndSquadPostureTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    // The reference squad in a row, 3 m apart, from rj at (21, 61) to t1 at (45, 61), returning fire; red 40 m south of
    // the row, holding fire until ordered.
    private static ScenarioRun Squad() => Scenario.ForSquad()
        .WithTerrain(TestMap.Create(widthMeters: 100, heightMeters: 120).Build())
        .WithSquad(SpecSquad.Reference(), new Vec2(21, 61), rules: RulesOfEngagement.ReturnFire)
        .WithFighter("red", new Vec2(36, 21), Side.Red, RulesOfEngagement.HoldFire, SpecSuppression.NeverHits(SpecShooting.Rifle))
        .Build();

    private static ScenarioRun InTheDrill(ScenarioRun sim, string id) =>
        sim.Order("red", new FireAt(id)).RunUntil(s => s.Unit(id).IsAwaitingOrders, OneMinute, $"{id} has reported contact");

    [Fact]
    public void A_member_in_the_drill_keeps_the_drills_posture_when_a_squad_posture_command_reaches_it()
    {
        var sim = InTheDrill(Squad(), "k2");

        var command = sim.Command("rj", new SetSquadPosture(Posture.Standing));
        sim.RunFor(TimeSpan.FromSeconds(SpecSquad.ExperiencedReactionSeconds + SpecShooting.Rules.PostureChangeSeconds + 1));

        sim.ShouldHaveActedOn("k2", command, SpecSquad.ExperiencedReactionSeconds);
        Assert.NotEqual(ContactDrillStage.None, sim.Unit("k2").DrillStage);
        sim.Unit("k2").Should().NotBeIn(Posture.Standing);
    }

    [Fact]
    public void When_the_drill_ends_the_member_takes_the_posture_the_squad_command_gave_it()
    {
        var sim = InTheDrill(Squad(), "k2");
        sim.Order("red", new CeaseFire()).RunUntil(s => !s.Unit("k2").IsSuppressed, OneMinute, "k2 no longer suppressed");
        sim.Command("rj", new SetSquadPosture(Posture.Crouching));
        sim.RunFor(TimeSpan.FromSeconds(5));

        sim.Order("k2", new Stop()).RunFor(TimeSpan.FromSeconds(SpecShooting.Rules.PostureChangeSeconds + 1));

        Assert.Equal(ContactDrillStage.None, sim.Unit("k2").DrillStage);
        sim.Unit("k2").Should().BeIn(Posture.Crouching); // not standing, as before the drill
    }

    [Fact]
    public void A_squad_posture_command_does_not_end_the_leaders_own_drill()
    {
        var sim = InTheDrill(Squad(), "rj");
        var stage = sim.Unit("rj").DrillStage;

        sim.Order("rj", new SetSquadPosture(Posture.Prone)).Step();

        Assert.Equal(stage, sim.Unit("rj").DrillStage);
        Assert.True(sim.Unit("rj").IsAwaitingOrders, "rj still waits for orders in its drill");
    }
}
