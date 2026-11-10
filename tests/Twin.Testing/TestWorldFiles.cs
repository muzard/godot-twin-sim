using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Twin.Sim.World;

namespace Twin.Testing;

/// <summary>
/// Writes a preprocessed world in the on-disk format the preprocessing tool produces (scenarios/README.md),
/// into a fresh temporary directory: <c>manifest.json</c>, <c>h_maa.bin</c> (float32) and
/// <c>maastoluokka.bin</c> (uint8), little-endian, row 0 = southernmost row. Delete it with <see cref="Dispose"/>.
/// </summary>
public sealed class TestWorldFiles : IDisposable
{
    private TestWorldFiles(string directory) => Directory = directory;

    /// <summary>The directory that holds the manifest and the layers.</summary>
    public string Directory { get; }

    /// <summary>
    /// Writes a world. Arrays are row-major with row 0 the southernmost row: index = row * columns + column.
    /// </summary>
    public static TestWorldFiles Write(
        int columns, int rows, float[] heights, TerrainClass[] classes, double originEasting = 385000, double originNorthing = 6672000)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(classes);
        if (heights.Length != columns * rows || classes.Length != columns * rows)
        {
            throw new ArgumentException($"Expected {columns * rows} values per layer.");
        }

        var directory = Path.Combine(Path.GetTempPath(), "twin-world-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);

        var heightBytes = new byte[heights.Length * sizeof(float)];
        for (var i = 0; i < heights.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(heightBytes.AsSpan(i * sizeof(float)), heights[i]);
        }

        var classBytes = classes.Select(c => (byte)c).ToArray();
        File.WriteAllBytes(Path.Combine(directory, "h_maa.bin"), heightBytes);
        File.WriteAllBytes(Path.Combine(directory, "maastoluokka.bin"), classBytes);

        var manifest = new
        {
            schemaVersion = 1,
            crs = "EPSG:3067",
            origin = new { easting = originEasting, northing = originNorthing },
            cellSize = SpecV01.CellSize,
            width = columns,
            height = rows,
            layers = new[]
            {
                new { name = "h_maa", type = "float32", file = "h_maa.bin", sha256 = Sha256(heightBytes) },
                new { name = "maastoluokka", type = "uint8", file = "maastoluokka.bin", sha256 = Sha256(classBytes) },
            },
            sources = new[] { new { name = "synthetic test data", date = "2026-11-08" } },
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest));
        return new TestWorldFiles(directory);
    }

    /// <summary>Path of a file in the world directory.</summary>
    public string PathOf(string fileName) => Path.Combine(Directory, fileName);

    /// <inheritdoc />
    public void Dispose()
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
