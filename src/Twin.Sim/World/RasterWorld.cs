using Twin.Sim.Geometry;
using Twin.Sim.Terrain;

namespace Twin.Sim.World;

/// <summary>
/// A world held as raster layers: ground height (float32) and terrain class per cell. Arrays are row-major with row 0
/// the southernmost row: index = row * Columns + column. Cell (column, row) has its center at
/// ((column + 0.5) * CellSize, (row + 0.5) * CellSize).
/// </summary>
public sealed class RasterWorld : IWorldQuery, ITerrain
{
    private readonly float[] _heights;
    private readonly TerrainClass[] _classes;

    /// <summary>Creates a world from its layers, without cliffs.</summary>
    public RasterWorld(int columns, int rows, double cellSize, float[] heights, TerrainClass[] classes)
        : this(columns, rows, cellSize, heights, classes, [])
    {
    }

    /// <summary>Creates a world from its layers and cliff lines (local meters).</summary>
    public RasterWorld(
        int columns, int rows, double cellSize, float[] heights, TerrainClass[] classes, IReadOnlyList<IReadOnlyList<Vec2>> cliffs)
    {
        ArgumentNullException.ThrowIfNull(cliffs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(classes);
        if (heights.Length != columns * rows || classes.Length != columns * rows)
        {
            throw new ArgumentException($"Expected {columns * rows} values per layer.");
        }

        Columns = columns;
        Rows = rows;
        CellSize = cellSize;
        _heights = (float[])heights.Clone();
        _classes = (TerrainClass[])classes.Clone();
        Bounds = new WorldBounds(0, 0, columns * cellSize, rows * cellSize);
        Cliffs = cliffs.Select(line => (IReadOnlyList<Vec2>)line.ToArray()).ToArray();
    }

    /// <summary>Number of cells west to east.</summary>
    public int Columns { get; }

    /// <summary>Number of cells south to north.</summary>
    public int Rows { get; }

    /// <inheritdoc />
    public double CellSize { get; }

    /// <inheritdoc />
    public WorldBounds Bounds { get; }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<Vec2>> Cliffs { get; }

    /// <summary>The same world with these cliff lines (local meters) instead of its own.</summary>
    public RasterWorld WithCliffs(IReadOnlyList<IReadOnlyList<Vec2>> cliffs) =>
        new(Columns, Rows, CellSize, _heights, _classes, cliffs);

    /// <inheritdoc />
    public double WidthMeters => Bounds.MaxX;

    /// <inheritdoc />
    public double DepthMeters => Bounds.MaxY;

    /// <inheritdoc />
    public bool IsInside(double x, double y) => x >= 0 && y >= 0 && x <= Bounds.MaxX && y <= Bounds.MaxY;

    /// <inheritdoc />
    public bool Contains(Vec2 point) => IsInside(point.X, point.Y);

    /// <inheritdoc />
    public double HeightAt(Vec2 point) => GroundHeight(point.X, point.Y);

    /// <inheritdoc />
    public double GroundHeight(double x, double y)
    {
        EnsureInside(x, y);

        // Position in "cell center" coordinates: center of cell i is at i. Outside the outermost centers, clamp.
        var gx = Math.Clamp((x / CellSize) - 0.5, 0, Columns - 1);
        var gy = Math.Clamp((y / CellSize) - 0.5, 0, Rows - 1);
        var c0 = Math.Min((int)gx, Columns - 1);
        var r0 = Math.Min((int)gy, Rows - 1);
        var c1 = Math.Min(c0 + 1, Columns - 1);
        var r1 = Math.Min(r0 + 1, Rows - 1);
        var tx = gx - c0;
        var ty = gy - r0;

        double h00 = _heights[(r0 * Columns) + c0];
        double h10 = _heights[(r0 * Columns) + c1];
        double h01 = _heights[(r1 * Columns) + c0];
        double h11 = _heights[(r1 * Columns) + c1];
        var south = h00 + ((h10 - h00) * tx);
        var north = h01 + ((h11 - h01) * tx);
        return south + ((north - south) * ty);
    }

    /// <inheritdoc />
    public TerrainClass ClassAt(double x, double y)
    {
        EnsureInside(x, y);
        return _classes[(RowOf(y) * Columns) + ColumnOf(x)];
    }

    /// <summary>Terrain class of cell (column, row).</summary>
    public TerrainClass ClassOfCell(int column, int row) => _classes[(row * Columns) + column];

    /// <summary>Stored ground height of cell (column, row), at its center.</summary>
    public double HeightOfCell(int column, int row) => _heights[(row * Columns) + column];

    /// <summary>Column of the cell that contains x; the east edge belongs to the last column.</summary>
    public int ColumnOf(double x) => Math.Clamp((int)Math.Floor(x / CellSize), 0, Columns - 1);

    /// <summary>Row of the cell that contains y; the north edge belongs to the last row.</summary>
    public int RowOf(double y) => Math.Clamp((int)Math.Floor(y / CellSize), 0, Rows - 1);

    private void EnsureInside(double x, double y)
    {
        if (!IsInside(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside the world {Bounds}.");
        }
    }
}
