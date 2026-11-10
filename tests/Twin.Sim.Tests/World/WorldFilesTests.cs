using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.World;

/// <summary>Reading a preprocessed world (manifest.json + raw little-endian layers, row 0 = south) (W1).</summary>
public class WorldFilesTests
{
    // Asymmetric on purpose: a flipped, mirrored or transposed read gives a different answer.
    private const int Columns = 3;
    private const int Rows = 2;

    private static readonly float[] Heights = [1, 2, 3, 4, 5, 6];

    private static readonly TerrainClass[] Classes =
    [
        TerrainClass.Road, TerrainClass.Open, TerrainClass.Open,       // row 0 (south)
        TerrainClass.Forest, TerrainClass.Forest, TerrainClass.Water,  // row 1 (north)
    ];

    [Fact]
    public void W1_row_0_of_a_layer_file_is_the_southernmost_row()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);

        var world = WorldFiles.Load(files.Directory).World;

        Assert.Equal(TerrainClass.Road, world.ClassAt(1, 1));     // south-west cell
        Assert.Equal(TerrainClass.Water, world.ClassAt(5, 3));    // north-east cell
        Assert.Equal(1, world.GroundHeight(1, 1), 6);
        Assert.Equal(6, world.GroundHeight(5, 3), 6);
    }

    [Fact]
    public void Loaded_world_has_the_size_and_origin_from_the_manifest()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes, originEasting: 385000, originNorthing: 6672000);

        var loaded = WorldFiles.Load(files.Directory);

        Assert.Equal(new WorldBounds(0, 0, Columns * 2.0, Rows * 2.0), loaded.World.Bounds);
        Assert.Equal(2.0, loaded.World.CellSize);
        Assert.Equal(new GeoOrigin(385000, 6672000), loaded.Origin);
    }

    [Fact]
    public void Layer_whose_checksum_does_not_match_the_manifest_is_rejected()
    {
        using var files = TestWorldFiles.Write(Columns, Rows, Heights, Classes);
        var layer = files.PathOf("maastoluokka.bin");
        var bytes = File.ReadAllBytes(layer);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(layer, bytes);

        var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(files.Directory));
        Assert.Contains("maastoluokka", error.Message, StringComparison.Ordinal);
    }
}
