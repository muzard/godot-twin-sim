using Twin.Sim.Geometry;
using Twin.Sim.Orders;
using Twin.Sim.Units;

namespace Twin.Sim.Systems;

/// <summary>
/// Moves units that have a <see cref="MoveTo"/> order in a straight line at their maximum ground speed times their
/// posture and wound factor, keeping them on the ground. Pinned, suppressed or posture-changing units do not move.
/// Slope effects, terrain cost and pathfinding are in <see cref="CorridorMovementSystem"/>.
/// </summary>
public sealed class MovementSystem : ISimSystem
{
    /// <inheritdoc />
    public void Update(Simulation simulation, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        foreach (var unit in simulation.Units)
        {
            if (unit.CurrentOrder is not MoveTo move || unit.IsImmobilized)
            {
                continue;
            }

            var previous = unit.Position;
            var toTarget = move.Destination - previous.Xy;
            var stepLength = unit.MaxSpeedMps * unit.SpeedFactor * dtSeconds;
            var arrived = toTarget.Length <= stepLength;
            var next = arrived ? move.Destination : previous.Xy + (toTarget.Normalized() * stepLength);

            unit.Position = next.WithZ(simulation.Terrain.HeightAt(next));

            if (arrived)
            {
                unit.Velocity = Vec3.Zero;
                unit.CurrentOrder = null;
                unit.Activity = UnitActivity.Idle;
            }
            else
            {
                unit.Velocity = (unit.Position - previous) * (1 / dtSeconds);
                unit.Activity = UnitActivity.Moving;
            }
        }
    }
}
