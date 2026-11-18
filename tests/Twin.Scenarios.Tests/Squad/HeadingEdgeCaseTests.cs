using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Testing;

namespace Twin.Scenarios.Tests.Squad;

/// <summary>
/// Headings of agents outside a squad, found in review (backlog 0004, S2c): an agent faces the way it moves (any way, not
/// only north, where every agent starts facing) and keeps its heading when it stops.
/// </summary>
public class HeadingEdgeCaseTests
{
    [Fact]
    public void A_unit_outside_a_squad_faces_east_when_it_moves_east()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: new Vec2(100, 100)).Build();

        sim.Order("alpha", new MoveTo(new Vec2(300, 100))).RunFor(TimeSpan.FromSeconds(5));

        sim.Unit("alpha").Should().Face(new Vec2(1, 0));
    }

    [Fact]
    public void A_unit_keeps_its_heading_when_it_stops()
    {
        var sim = Scenario.Create().WithUnit("alpha", at: new Vec2(100, 100)).Build();
        sim.Order("alpha", new MoveTo(new Vec2(90, 100))).RunUntil(s => s.Unit("alpha").IsIdle, TimeSpan.FromMinutes(1), "alpha idle");

        sim.RunFor(TimeSpan.FromSeconds(5));

        sim.Unit("alpha").Should().BeIdle().Face(new Vec2(-1, 0));
    }
}
