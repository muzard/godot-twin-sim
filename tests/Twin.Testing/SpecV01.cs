using Twin.Sim.Movement;

namespace Twin.Testing;

/// <summary>
/// The starting values and reference formulas of spec v0.1 ("Taistelusimulaattori: speksi v0.1 ja Smoke test I").
/// Tests pass these explicitly instead of relying on simulation defaults, so calibrating the defaults later
/// does not silently change what a test checks. Expected values in tests are computed from these.
/// </summary>
public static class SpecV01
{
    /// <summary>Simulation step dt = 0.5 s, i.e. 2 ticks per simulated second.</summary>
    public const int TickRateHz = 2;

    /// <summary>Duration of one step in seconds.</summary>
    public const double DtSeconds = 1.0 / TickRateHz;

    /// <summary>Raster cell size in meters.</summary>
    public const double CellSize = 2.0;

    /// <summary>Corridor soft-zone width m in meters.</summary>
    public const double SoftMargin = 25;

    /// <summary>Corridor soft-zone penalty coefficient α.</summary>
    public const double SoftPenalty = 1;

    /// <summary>Permission is requested when the boundary-respecting route costs more than this times the reference route.</summary>
    public const double PermissionThreshold = 1.5;

    /// <summary>Walking infantry in full kit: base speeds per terrain class, slope limit 0.8, eye height 1.6 m (standing).</summary>
    public static MovementProfile Infantry { get; } = new(
        RoadMps: 1.5,
        OpenMps: 1.3,
        FieldMps: 1.2,
        ForestMps: 0.9,
        BogMps: 0.6,
        MaxSlope: 0.8,
        EyeHeightMeters: 1.6);

    /// <summary>
    /// Relative slope factor from Tobler's hiking function, 1 on flat ground.
    /// <paramref name="slope"/> is dz/ds in the direction of travel, uphill positive.
    /// </summary>
    public static double SlopeFactor(double slope) =>
        Math.Exp(-3.5 * Math.Abs(slope + 0.05)) / Math.Exp(-3.5 * 0.05);

    /// <summary>Corridor cost factor k(d) in the soft zone, for distance <paramref name="d"/> from the centerline.</summary>
    public static double SoftZoneFactor(double d, double width, double softMargin, double alpha) =>
        1 + (alpha * Math.Pow((d - (width / 2)) / softMargin, 2));
}
