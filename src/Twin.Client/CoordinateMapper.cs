using Godot;
using Twin.Sim.Geometry;

namespace Twin.Client;

/// <summary>
/// The only place where simulation coordinates (meters, X east, Y north) meet Godot's 2D screen
/// coordinates (pixels, X right, Y down). See docs/adr/0003-coordinates-and-units.md.
/// </summary>
public sealed class CoordinateMapper(double mapWidthMeters, double mapDepthMeters, float pixelsPerMeter)
{
    /// <summary>Pixels per simulated meter at zoom 1.</summary>
    public float PixelsPerMeter { get; } = pixelsPerMeter;

    /// <summary>Map width in meters.</summary>
    public double MapWidthMeters { get; } = mapWidthMeters;

    /// <summary>Map depth (north-south) in meters.</summary>
    public double MapDepthMeters { get; } = mapDepthMeters;

    /// <summary>Simulation map position to screen position.</summary>
    public Vector2 ToScreen(Vec2 map) =>
        new((float)(map.X * PixelsPerMeter), (float)((MapDepthMeters - map.Y) * PixelsPerMeter));

    /// <summary>A length in simulated meters as screen pixels at zoom 1.</summary>
    public float ToScreenLength(double meters) => (float)(meters * PixelsPerMeter);

    /// <summary>Screen position to simulation map position.</summary>
    public Vec2 ToMap(Vector2 screen) =>
        new(screen.X / PixelsPerMeter, MapDepthMeters - (screen.Y / PixelsPerMeter));
}
