using Twin.Sim.Corridors;
using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Sim.Movement;

/// <summary>
/// How an agent type moves (spec v0.1, "Liikemalli"): base speed per terrain class, the steepest slope it can take and
/// its eye height above the ground. Water and buildings are impassable.
/// </summary>
/// <param name="RoadMps">Base speed on roads, m/s.</param>
/// <param name="OpenMps">Base speed on open land, m/s.</param>
/// <param name="FieldMps">Base speed on fields, m/s.</param>
/// <param name="ForestMps">Base speed in forest, m/s.</param>
/// <param name="BogMps">Base speed on bog, m/s.</param>
/// <param name="MaxSlope">Steepest passable slope, rise over run, up or down.</param>
/// <param name="EyeHeightMeters">Eye height above the ground.</param>
/// <param name="CanRappel">
/// True if the unit can rappel: it may cross cliff lines, up or down, whatever the slope there (docs/adr/0010).
/// </param>
public sealed record MovementProfile(
    double RoadMps, double OpenMps, double FieldMps, double ForestMps, double BogMps, double MaxSlope, double EyeHeightMeters,
    bool CanRappel = false)
{
    /// <summary>Spec v0.1 starting values for walking infantry in full kit, standing.</summary>
    public static MovementProfile Infantry { get; } = new(1.5, 1.3, 1.2, 0.9, 0.6, 0.8, 1.6);

    /// <summary>The fastest base speed of any class.</summary>
    public double MaxBaseSpeed => Math.Max(Math.Max(Math.Max(RoadMps, OpenMps), Math.Max(FieldMps, ForestMps)), BogMps);

    /// <summary>Base speed on a terrain class; 0 for impassable classes.</summary>
    public double BaseSpeed(TerrainClass terrain) => terrain switch
    {
        TerrainClass.Road => RoadMps,
        TerrainClass.Open => OpenMps,
        TerrainClass.Field => FieldMps,
        TerrainClass.Forest => ForestMps,
        TerrainClass.Bog => BogMps,
        _ => 0,
    };

    /// <summary>
    /// Relative speed on a slope from Tobler's hiking function, 1 on flat ground: exp(−3.5|S + 0.05|) / exp(−3.5 · 0.05),
    /// where S is dz/ds in the direction of travel, uphill positive.
    /// </summary>
    public static double SlopeFactor(double slope) => Math.Exp((-3.5 * Math.Abs(slope + 0.05)) + (3.5 * 0.05));

    /// <summary>
    /// Speed along a move with this slope, for this profile: the slope factor with the slope clamped to the limit, so a
    /// rappelled cliff costs as much as the steepest slope the unit could walk.
    /// </summary>
    public double SlopeSpeedFactor(double slope) => SlopeFactor(Math.Clamp(slope, -MaxSlope, MaxSlope));

    /// <summary>Largest value <see cref="SlopeFactor"/> can take (at a gentle 5 % descent).</summary>
    public static double MaxSlopeFactor => SlopeFactor(-0.05);
}

/// <summary>What a path search must respect.</summary>
/// <param name="Corridor">The corridor; null searches the whole world without corridor costs or drawn boundaries.</param>
/// <param name="Permits">Permits that open hard boundaries near given points.</param>
/// <param name="IgnoreHardBoundaries">True for the reference route: hard boundaries, including the forest edge, are ignored.</param>
public sealed record PathOptions(
    Corridor? Corridor = null, IReadOnlyList<CrossingPermit>? Permits = null, bool IgnoreHardBoundaries = false);

/// <summary>The result of a path search.</summary>
/// <param name="Found">True if a path exists.</param>
/// <param name="Waypoints">Centers of the cells visited, from the start cell to the goal cell; empty if none.</param>
/// <param name="CostSeconds">Travel time in seconds including corridor penalties; infinity if none.</param>
public sealed record PathResult(bool Found, IReadOnlyList<Vec2> Waypoints, double CostSeconds)
{
    /// <summary>No path.</summary>
    public static PathResult None { get; } = new(false, [], double.PositiveInfinity);
}

/// <summary>A place where a path crosses a hard boundary.</summary>
/// <param name="BoundaryId">The boundary.</param>
/// <param name="At">The crossing point (forest edge: the end of the move out of the forest).</param>
public sealed record BoundaryCrossing(string BoundaryId, Vec2 At);
