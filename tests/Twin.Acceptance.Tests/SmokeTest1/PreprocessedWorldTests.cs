using System.Text.Json;
using Twin.Sim.World;
using Twin.Testing;

namespace Twin.Acceptance.Tests.SmokeTest1;

/// <summary>
/// The preprocessed MML area matches its source (W4). The preprocessing writes <c>world/samples.json</c>: points at
/// cell centers with the height read by GDAL directly from the original GeoTIFF, in ETRS-TM35FIN. Here the same points
/// are read from the preprocessed layer through the simulation's own loader.
/// </summary>
public class PreprocessedWorldTests
{
    private const string Area = "st1";

    [Fact]
    public void W4_preprocessed_heights_match_the_values_GDAL_reads_from_the_original_GeoTIFF()
    {
        var samplesFile = ScenarioFiles.Require(Area, Path.Combine("world", "samples.json"));
        var loaded = WorldFiles.Load(Path.Combine(ScenarioFiles.Directory(Area), "world"));
        using var json = JsonDocument.Parse(File.ReadAllText(samplesFile));
        var samples = json.RootElement.EnumerateArray().ToList();

        Assert.True(samples.Count >= 10, $"expected at least 10 sample points, found {samples.Count}");
        foreach (var sample in samples)
        {
            var etrs = new EtrsPoint(sample.GetProperty("easting").GetDouble(), sample.GetProperty("northing").GetDouble());
            var expected = sample.GetProperty("h_maa").GetDouble();
            var local = loaded.Origin.ToLocal(etrs);

            var actual = loaded.World.GroundHeight(local.X, local.Y);

            // At a cell center the height is the stored float32 value itself; only float32 rounding may differ.
            Assert.True(Math.Abs(actual - expected) < 1e-3,
                $"at ETRS ({etrs.Easting}, {etrs.Northing}) GDAL reads {expected} m but the world has {actual} m");
        }
    }
}
