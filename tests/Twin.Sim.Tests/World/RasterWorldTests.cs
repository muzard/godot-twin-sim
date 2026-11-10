using Twin.Sim.World;

namespace Twin.Sim.Tests.World;

/// <summary>
/// Spec v0.1 world rules: 2 m cells, a cell's value applies to its center, height between centers is bilinear,
/// row 0 is the southernmost row (W2).
/// </summary>
public class RasterWorldTests
{
    private const double Cell = 2.0;

    // 3 columns x 2 rows, row 0 = south. Values differ in every cell so a mix-up shows.
    private static readonly float[] Heights =
    [
        10, 20, 30, // row 0 (south)
        40, 60, 90, // row 1 (north)
    ];

    private static readonly TerrainClass[] Classes =
    [
        TerrainClass.Road, TerrainClass.Field, TerrainClass.Open,
        TerrainClass.Forest, TerrainClass.Bog, TerrainClass.Water,
    ];

    private static RasterWorld World() => new(3, 2, Cell, Heights, Classes);

    [Fact]
    public void W2_height_at_a_cell_center_is_the_cell_value()
    {
        var world = World();

        Assert.Equal(10, world.GroundHeight(1, 1), 9);
        Assert.Equal(60, world.GroundHeight(3, 3), 9);
        Assert.Equal(90, world.GroundHeight(5, 3), 9);
    }

    [Fact]
    public void W2_height_between_cell_centers_is_bilinear()
    {
        var world = World();
        // Between the centers (1,1) (3,1) (1,3) (3,3), a quarter of the way east and three quarters north.
        const double tx = 0.25, ty = 0.75;
        var south = 10 + ((20 - 10) * tx);
        var north = 40 + ((60 - 40) * tx);
        var expected = south + ((north - south) * ty);

        Assert.Equal(expected, world.GroundHeight(1.5, 2.5), 9);
    }

    [Fact]
    public void W2_terrain_class_is_the_class_of_the_containing_cell_with_row_0_in_the_south()
    {
        var world = World();

        Assert.Equal(TerrainClass.Road, world.ClassAt(0.2, 0.2));
        Assert.Equal(TerrainClass.Open, world.ClassAt(5.9, 1.9));
        Assert.Equal(TerrainClass.Forest, world.ClassAt(0.5, 2.1));
        Assert.Equal(TerrainClass.Water, world.ClassAt(5.5, 3.5));
    }

    [Fact]
    public void World_bounds_cover_all_cells_from_the_south_west_corner()
    {
        var world = World();

        Assert.Equal(Cell, world.CellSize);
        Assert.Equal(new WorldBounds(0, 0, 6, 4), world.Bounds);
        Assert.True(world.IsInside(0, 0));
        Assert.True(world.IsInside(6, 4));
        Assert.False(world.IsInside(-0.01, 2));
        Assert.False(world.IsInside(3, 4.01));
    }

    [Fact]
    public void Terrain_class_codes_match_the_preprocessing_contract()
    {
        // The uint8 codes in maastoluokka.bin, shared with prep/ (scenarios/README.md). Changing one breaks every world file.
        Assert.Equal(0, (byte)TerrainClass.Open);
        Assert.Equal(1, (byte)TerrainClass.Road);
        Assert.Equal(2, (byte)TerrainClass.Field);
        Assert.Equal(3, (byte)TerrainClass.Forest);
        Assert.Equal(4, (byte)TerrainClass.Bog);
        Assert.Equal(5, (byte)TerrainClass.Water);
        Assert.Equal(6, (byte)TerrainClass.Building);
    }
}
