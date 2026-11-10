using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Twin.Sim.Geometry;

namespace Twin.Sim.World;

/// <summary>
/// Reads a preprocessed world: <c>manifest.json</c> plus raw little-endian layers with row 0 the southernmost row
/// (format: scenarios/README.md). Every layer's sha256 is checked against the manifest.
/// </summary>
public static class WorldFiles
{
    private const string HeightLayer = "h_maa";
    private const string ClassLayer = "maastoluokka";

    /// <summary>Loads the world in <paramref name="directory"/>.</summary>
    /// <exception cref="InvalidDataException">The manifest is invalid or a layer does not match it.</exception>
    public static LoadedWorld Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var root = manifest.RootElement;
        var schema = root.GetProperty("schemaVersion").GetInt32();
        if (schema != 1)
        {
            throw new InvalidDataException($"Unsupported world schema version {schema}.");
        }

        var columns = root.GetProperty("width").GetInt32();
        var rows = root.GetProperty("height").GetInt32();
        var cellSize = root.GetProperty("cellSize").GetDouble();
        var origin = root.GetProperty("origin");
        var geoOrigin = new GeoOrigin(origin.GetProperty("easting").GetDouble(), origin.GetProperty("northing").GetDouble());
        var cells = columns * rows;

        var heightBytes = ReadLayer(directory, root, HeightLayer, "float32", cells * sizeof(float));
        var heights = new float[cells];
        for (var i = 0; i < cells; i++)
        {
            heights[i] = BinaryPrimitives.ReadSingleLittleEndian(heightBytes.AsSpan(i * sizeof(float)));
        }

        var classBytes = ReadLayer(directory, root, ClassLayer, "uint8", cells);
        var classes = new TerrainClass[cells];
        for (var i = 0; i < cells; i++)
        {
            if (classBytes[i] > (byte)TerrainClass.Building)
            {
                throw new InvalidDataException($"Layer '{ClassLayer}' has an unknown terrain class code {classBytes[i]}.");
            }

            classes[i] = (TerrainClass)classBytes[i];
        }

        return new LoadedWorld(new RasterWorld(columns, rows, cellSize, heights, classes, ReadCliffs(directory, root)), geoOrigin);
    }

    /// <summary>Cliff lines from the manifest's optional <c>cliffs</c> entry (docs/adr/0010); none without it.</summary>
    private static List<IReadOnlyList<Vec2>> ReadCliffs(string directory, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("cliffs", out var entry))
        {
            return [];
        }

        var file = entry.GetProperty("file").GetString() ?? "jyrkanteet.json";
        var bytes = File.ReadAllBytes(Path.Combine(directory, file));
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(entry.GetProperty("sha256").GetString(), actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Cliff file '{file}' does not match its checksum in the manifest (sha256 {actual}).");
        }

        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.GetProperty("lines").EnumerateArray()
            .Select(line => (IReadOnlyList<Vec2>)line.EnumerateArray().Select(p => new Vec2(p[0].GetDouble(), p[1].GetDouble())).ToArray())
            .ToList();
    }

    private static byte[] ReadLayer(string directory, JsonElement manifest, string name, string type, int expectedBytes)
    {
        var layer = manifest.GetProperty("layers").EnumerateArray()
            .FirstOrDefault(l => l.GetProperty("name").GetString() == name);
        if (layer.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The manifest has no layer '{name}'.");
        }

        if (layer.GetProperty("type").GetString() != type)
        {
            throw new InvalidDataException($"Layer '{name}' should be {type}.");
        }

        var bytes = File.ReadAllBytes(Path.Combine(directory, layer.GetProperty("file").GetString() ?? name + ".bin"));
        if (bytes.Length != expectedBytes)
        {
            throw new InvalidDataException($"Layer '{name}' has {bytes.Length} bytes, expected {expectedBytes}.");
        }

        var expected = layer.GetProperty("sha256").GetString();
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Layer '{name}' does not match its checksum in the manifest (sha256 {actual}).");
        }

        return bytes;
    }
}
