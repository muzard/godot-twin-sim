using Twin.Sim.Geometry;

namespace Twin.Sim.World;

/// <summary>
/// Terrain class of a raster cell. The numeric values are the uint8 codes in <c>maastoluokka.bin</c>, shared with the
/// preprocessing tool (scenarios/README.md): never renumber them.
/// </summary>
public enum TerrainClass : byte
{
    /// <summary>Open land with no other feature.</summary>
    Open = 0,

    /// <summary>Road.</summary>
    Road = 1,

    /// <summary>Field or meadow.</summary>
    Field = 2,

    /// <summary>Forest. Leaving it crosses a hard boundary (docs/adr/0009).</summary>
    Forest = 3,

    /// <summary>Bog.</summary>
    Bog = 4,

    /// <summary>Water: impassable.</summary>
    Water = 5,

    /// <summary>Building: impassable.</summary>
    Building = 6,
}

/// <summary>The rectangle a world covers, in local meters.</summary>
/// <param name="MinX">West edge.</param>
/// <param name="MinY">South edge.</param>
/// <param name="MaxX">East edge.</param>
/// <param name="MaxY">North edge.</param>
public readonly record struct WorldBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>
/// Read-only queries on the preprocessed world (spec v0.1, "Maailma"): a 2 m raster of layers, local meters from the
/// area's south-west corner. A cell's value applies to its center.
/// </summary>
public interface IWorldQuery
{
    /// <summary>The area the world covers.</summary>
    WorldBounds Bounds { get; }

    /// <summary>Raster cell size in meters.</summary>
    double CellSize { get; }

    /// <summary>Ground height in meters, bilinear between cell centers.</summary>
    double GroundHeight(double x, double y);

    /// <summary>Terrain class of the cell that contains the point.</summary>
    TerrainClass ClassAt(double x, double y);

    /// <summary>True if the point is inside the world (edges included).</summary>
    bool IsInside(double x, double y);

    /// <summary>
    /// Cliff lines (Topographic Database "jyrkänne") in local meters. A route may not cross one, up or down, unless
    /// the unit can rappel (docs/adr/0010).
    /// </summary>
    IReadOnlyList<IReadOnlyList<Vec2>> Cliffs { get; }
}
