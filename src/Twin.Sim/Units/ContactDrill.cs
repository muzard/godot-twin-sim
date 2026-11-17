using Twin.Sim.Combat;
using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim.Units;

/// <summary>An agent's drill under fire: when it started and what it is doing.</summary>
/// <param name="startedAt">When the agent came under fire.</param>
/// <param name="suspendedOrder">The order it dropped; its hard boundaries still bound where it may go.</param>
/// <param name="postureBefore">The posture it wanted before; restored when the drill ends.</param>
internal sealed class ContactDrill(double startedAt, Order? suspendedOrder, Posture postureBefore)
{
    public double StartedAt { get; } = startedAt;

    public Order? SuspendedOrder { get; } = suspendedOrder;

    public Posture PostureBefore { get; } = postureBefore;

    public int SnapShotsFired { get; set; }

    public List<Vec2> Route { get; set; } = [];

    public int Next { get; set; }

    public bool HeadingForFiringPosition { get; set; }

    public bool AtFiringPosition { get; set; }
}
