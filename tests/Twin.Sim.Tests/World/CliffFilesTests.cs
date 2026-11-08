using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Twin.Sim.Geometry;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Sim.Tests.World;

/// <summary>
/// Cliff lines in a preprocessed world (docs/adr/0010): <c>jyrkanteet.json</c> holds polylines in local meters and is
/// listed in the manifest with its sha256. Worlds without the entry have no cliffs.
/// </summary>
public class CliffFilesTests
{
    private static readonly float[] Heights = [1, 2, 3, 4, 5, 6];
    private static readonly TerrainClass[] Classes = Enumerable.Repeat(TerrainClass.Forest, 6).ToArray();

    private static void AddCliffs(TestWorldFiles files, string json, string? sha256 = null)
    {
        File.WriteAllText(files.PathOf("jyrkanteet.json"), json);
        var manifest = JsonNode.Parse(File.ReadAllText(files.PathOf("manifest.json")))!.AsObject();
        manifest["cliffs"] = new JsonObject
        {
            ["file"] = "jyrkanteet.json",
            ["sha256"] = sha256 ?? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(files.PathOf("jyrkanteet.json")))),
        };
        File.WriteAllText(files.PathOf("manifest.json"), manifest.ToJsonString());
    }

    [Fact]
    public void Cliff_lines_are_read_in_local_meters()
    {
        using var files = TestWorldFiles.Write(3, 2, Heights, Classes);
        AddCliffs(files, """{ "lines": [ [[0.5, 1.0], [5.5, 3.0]], [[1, 0], [1, 4], [2, 4]] ] }""");

        var world = WorldFiles.Load(files.Directory).World;

        Assert.Equal(2, world.Cliffs.Count);
        Assert.Equal([new Vec2(0.5, 1.0), new Vec2(5.5, 3.0)], world.Cliffs[0]);
        Assert.Equal(3, world.Cliffs[1].Count);
    }

    [Fact]
    public void A_world_without_a_cliff_file_has_no_cliffs()
    {
        using var files = TestWorldFiles.Write(3, 2, Heights, Classes);

        Assert.Empty(WorldFiles.Load(files.Directory).World.Cliffs);
    }

    [Fact]
    public void A_cliff_file_whose_checksum_does_not_match_is_rejected()
    {
        using var files = TestWorldFiles.Write(3, 2, Heights, Classes);
        AddCliffs(files, """{ "lines": [] }""", sha256: new string('0', 64));

        var error = Assert.Throws<InvalidDataException>(() => WorldFiles.Load(files.Directory));
        Assert.Contains("jyrkanteet", error.Message, StringComparison.Ordinal);
    }
}
