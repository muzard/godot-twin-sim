using Twin.Sim.Geometry;

namespace Twin.Sim.World;

/// <summary>A point in ETRS-TM35FIN (EPSG:3067), meters.</summary>
/// <param name="Easting">Easting (E).</param>
/// <param name="Northing">Northing (N).</param>
public readonly record struct EtrsPoint(double Easting, double Northing);

/// <summary>
/// The south-west corner of a scenario area in ETRS-TM35FIN. The simulation works in local meters east and north of
/// it; ETRS coordinates are used only when reading scenarios and exporting results (spec v0.1, docs/adr/0003).
/// </summary>
/// <param name="Easting">Easting of the corner.</param>
/// <param name="Northing">Northing of the corner.</param>
public sealed record GeoOrigin(double Easting, double Northing)
{
    /// <summary>ETRS-TM35FIN to local meters.</summary>
    public Vec2 ToLocal(EtrsPoint point) => new(point.Easting - Easting, point.Northing - Northing);

    /// <summary>Local meters to ETRS-TM35FIN.</summary>
    public EtrsPoint ToEtrs(Vec2 point) => new(point.X + Easting, point.Y + Northing);
}

/// <summary>A world read from disk, with the origin that places it in ETRS-TM35FIN.</summary>
/// <param name="World">The world in local meters.</param>
/// <param name="Origin">Its south-west corner in ETRS-TM35FIN.</param>
public sealed record LoadedWorld(RasterWorld World, GeoOrigin Origin);
