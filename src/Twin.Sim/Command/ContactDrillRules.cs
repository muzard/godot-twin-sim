namespace Twin.Sim.Command;

/// <summary>
/// The values of the immediate action drill under fire (backlog 0003), kept in <see cref="Doctrine"/> so a scenario can
/// calibrate them without code changes. Reaction times are per experience level (<c>SuppressionRules</c>).
/// </summary>
/// <param name="SnapShots">Snap shots fired while taking cover.</param>
/// <param name="CoverSearchMeters">Cover and firing positions are looked for within this distance.</param>
/// <param name="SearchGridMeters">Spacing of the points looked at (the raster cells).</param>
/// <param name="FireSectorHalfAngleDegrees">No shot is fired with an own fighter within this angle of the muzzle direction.</param>
/// <param name="ReserveFraction">Fire without an order stops when this share of the rounds is left.</param>
public sealed record ContactDrillRules(
    int SnapShots, double CoverSearchMeters, double SearchGridMeters, double FireSectorHalfAngleDegrees, double ReserveFraction)
{
    /// <summary>The decisions of 2026-11-17: 3 snap shots, 20 m on a 2 m grid, a 60° sector, 20 % in reserve.</summary>
    public static ContactDrillRules Default { get; } = new(3, 20, 2, 30, 0.2);
}
