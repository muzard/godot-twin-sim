using Twin.Sim.Geometry;
using Twin.Sim.Orders;

namespace Twin.Sim.Squads;

/// <summary>
/// A fire area (tulialue, backlog 0004 S5b): a direction sector from a fighter, every map direction within
/// <c>WidthDegrees / 2</c> of <paramref name="Centre"/> (edges included). The squad's front is one too, seen from its leader.
/// </summary>
/// <param name="Centre">The sector's centre, a unit vector in map coordinates.</param>
/// <param name="WidthDegrees">The sector's full width in degrees.</param>
public sealed record FireArea(Vec2 Centre, double WidthDegrees)
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

/// <summary>
/// The squad leader's fire distribution as a squad command (backlog 0004 S5b): the front and each member's fire area,
/// worked out when the leader gives it. Logged as <see cref="Events.CommandIssued"/> with the text <c>DirectSquadFire</c>.
/// </summary>
internal sealed record SquadFireDistribution(FireArea Front, IReadOnlyDictionary<string, FireArea> Areas) : Order
{
    /// <inheritdoc />
    public override string ToString() => nameof(DirectSquadFire);
}
