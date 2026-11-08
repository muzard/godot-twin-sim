using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Testing;

/// <summary>
/// Small synthetic raster worlds for tests, where the expected result is known in advance.
/// Cells are <see cref="SpecV01.CellSize"/> (2 m); cell (column, row) has its center at
/// ((column + 0.5) * 2, (row + 0.5) * 2), and row 0 is the southernmost row.
/// <para>Build in meters:</para>
/// <code>
/// var world = TestMap.Create(widthMeters: 100, heightMeters: 60)
///     .Fill(TerrainClass.Water, fromX: 30, fromY: 46, toX: 70, toY: 92)
///     .WithHeights(p => 0.125 * p.X)
///     .Build();
/// </code>
/// <para>or draw one, north up, one character per cell (see <see cref="Parse"/>).</para>
/// </summary>
public sealed class TestMap
{
    private readonly int _columns;
    private readonly int _rows;
    private readonly TerrainClass[] _classes;
    private Func<Vec2, double> _height = _ => 0;

    private TestMap(int columns, int rows, TerrainClass fill)
    {
        _columns = columns;
        _rows = rows;
        _classes = Enumerable.Repeat(fill, columns * rows).ToArray();
    }

    /// <summary>Starts a flat map of one terrain class (open land by default). Sizes are whole cells.</summary>
    public static TestMap Create(double widthMeters, double heightMeters, TerrainClass fill = TerrainClass.Open)
    {
        var columns = (int)Math.Round(widthMeters / SpecV01.CellSize);
        var rows = (int)Math.Round(heightMeters / SpecV01.CellSize);
        if (columns < 1 || rows < 1 || Math.Abs((columns * SpecV01.CellSize) - widthMeters) > 1e-9 ||
            Math.Abs((rows * SpecV01.CellSize) - heightMeters) > 1e-9)
        {
            throw new ArgumentException($"Map size must be a positive whole number of {SpecV01.CellSize} m cells.");
        }

        return new TestMap(columns, rows, fill);
    }

    /// <summary>Sets every cell whose center lies inside the rectangle (edges included) to <paramref name="terrain"/>.</summary>
    public TestMap Fill(TerrainClass terrain, double fromX, double fromY, double toX, double toY)
    {
        for (var row = 0; row < _rows; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                var center = CellCenter(column, row);
                if (center.X >= fromX && center.X <= toX && center.Y >= fromY && center.Y <= toY)
                {
                    _classes[(row * _columns) + column] = terrain;
                }
            }
        }

        return this;
    }

    /// <summary>Sets the ground height of every cell from its center position. Heights are stored as float32.</summary>
    public TestMap WithHeights(Func<Vec2, double> heightAtCellCenter)
    {
        _height = heightAtCellCenter ?? throw new ArgumentNullException(nameof(heightAtCellCenter));
        return this;
    }

    /// <summary>Creates the world.</summary>
    public RasterWorld Build()
    {
        var heights = new float[_columns * _rows];
        for (var row = 0; row < _rows; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                heights[(row * _columns) + column] = (float)_height(CellCenter(column, row));
            }
        }

        return new RasterWorld(_columns, _rows, SpecV01.CellSize, heights, (TerrainClass[])_classes.Clone());
    }

    /// <summary>
    /// Draws a map, north up: the first line is the northernmost row. One character per cell:
    /// <c>.</c> open, <c>=</c> road, <c>,</c> field, <c>F</c> forest, <c>b</c> bog, <c>~</c> water, <c>#</c> building.
    /// Upper-case letters other than F mark named points on open land; read them with <see cref="DrawnMap.Point"/>.
    /// Leading and trailing whitespace on each line is ignored.
    /// </summary>
    public static DrawnMap Parse(string drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var lines = drawing.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0 || lines.Any(l => l.Length != lines[0].Length))
        {
            throw new ArgumentException("A drawn map needs at least one line, and all lines must be equally long.");
        }

        var map = new TestMap(lines[0].Length, lines.Count, TerrainClass.Open);
        var points = new SortedDictionary<char, Vec2>();
        for (var line = 0; line < lines.Count; line++)
        {
            var row = lines.Count - 1 - line;
            for (var column = 0; column < lines[line].Length; column++)
            {
                var symbol = lines[line][column];
                if (char.IsUpper(symbol) && symbol != 'F')
                {
                    points[symbol] = CellCenter(column, row);
                    symbol = '.';
                }

                map._classes[(row * map._columns) + column] = symbol switch
                {
                    '.' => TerrainClass.Open,
                    '=' => TerrainClass.Road,
                    ',' => TerrainClass.Field,
                    'F' => TerrainClass.Forest,
                    'b' => TerrainClass.Bog,
                    '~' => TerrainClass.Water,
                    '#' => TerrainClass.Building,
                    _ => throw new ArgumentException($"Unknown map symbol '{symbol}'."),
                };
            }
        }

        return new DrawnMap(map.Build(), points);
    }

    /// <summary>Center of a cell in meters.</summary>
    public static Vec2 CellCenter(int column, int row) =>
        new((column + 0.5) * SpecV01.CellSize, (row + 0.5) * SpecV01.CellSize);
}

/// <summary>A world drawn with <see cref="TestMap.Parse"/>, plus its named points.</summary>
public sealed class DrawnMap
{
    private readonly SortedDictionary<char, Vec2> _points;

    internal DrawnMap(RasterWorld world, SortedDictionary<char, Vec2> points)
    {
        World = world;
        _points = points;
    }

    /// <summary>The world.</summary>
    public RasterWorld World { get; }

    /// <summary>Center of the cell marked with <paramref name="name"/>.</summary>
    public Vec2 Point(char name) =>
        _points.TryGetValue(name, out var point) ? point : throw new ArgumentException($"The map has no point '{name}'.");
}
