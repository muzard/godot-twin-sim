using Twin.Sim.Scenarios;
using Twin.Sim.World;

namespace Twin.Testing;

/// <summary>
/// Writes a scenario directory (scenario.json plus a flat world of open land, origin ETRS (385000, 6672000)) into a fresh
/// temporary directory, loads it with <see cref="ScenarioDefinition.Load"/> and deletes it again. For tests of the
/// scenario file format (backlog 0004 S2d): local coordinates are ETRS minus the origin, so ETRS (385080, 6672101) is
/// local (80, 101).
/// </summary>
public static class TestScenarioFolder
{
    /// <summary>The world's south-west corner, ETRS-TM35FIN easting.</summary>
    public const double OriginEasting = 385000;

    /// <summary>The world's south-west corner, ETRS-TM35FIN northing.</summary>
    public const double OriginNorthing = 6672000;

    /// <summary>
    /// Loads <paramref name="scenarioJson"/> with a world of <paramref name="widthMeters"/> × <paramref name="heightMeters"/>
    /// of open land. The world is written at <paramref name="worldPath"/>, relative to the scenario directory (for example
    /// <c>../shared/world</c> to check a world shared between scenarios).
    /// </summary>
    /// <exception cref="InvalidDataException">The scenario is not valid (as <see cref="ScenarioDefinition.Load"/>).</exception>
    public static ScenarioDefinition Load(string scenarioJson, double widthMeters = 500, double heightMeters = 200, string worldPath = "world")
    {
        ArgumentNullException.ThrowIfNull(scenarioJson);
        var columns = (int)Math.Round(widthMeters / SpecV01.CellSize);
        var rows = (int)Math.Round(heightMeters / SpecV01.CellSize);
        using var files = TestWorldFiles.Write(
            columns, rows, new float[columns * rows], Enumerable.Repeat(TerrainClass.Open, columns * rows).ToArray(), OriginEasting, OriginNorthing);
        var root = Path.Combine(Path.GetTempPath(), "twin-scenario-" + Path.GetRandomFileName());
        var directory = Path.Combine(root, "scenario");
        try
        {
            var world = Path.GetFullPath(Path.Combine(directory, worldPath));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(world);
            foreach (var file in Directory.GetFiles(files.Directory))
            {
                File.Copy(file, Path.Combine(world, Path.GetFileName(file)));
            }

            File.WriteAllText(Path.Combine(directory, "scenario.json"), scenarioJson);
            return ScenarioDefinition.Load(directory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
