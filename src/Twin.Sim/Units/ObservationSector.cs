using Twin.Sim.Geometry;

namespace Twin.Sim.Units;

/// <summary>
/// A direction an agent observes (backlog 0004, S2c): every map direction within <c>WidthDegrees / 2</c> of
/// <paramref name="Centre"/>, edges included.
/// </summary>
/// <param name="Centre">The sector's centre, a unit vector in map coordinates.</param>
/// <param name="WidthDegrees">The sector's full width in degrees.</param>
public sealed record ObservationSector(Vec2 Centre, double WidthDegrees)
{
    /// <summary>True if the map direction <paramref name="direction"/> lies within the sector (edges included).</summary>
    public bool Covers(Vec2 direction)
    {
        if (direction.Length <= 1e-12)
        {
            return false;
        }

        var cos = Math.Clamp(Centre.Normalized().Dot(direction.Normalized()), -1, 1);
        return Math.Acos(cos) * 180 / Math.PI <= (WidthDegrees / 2) + 1e-6;
    }
}
