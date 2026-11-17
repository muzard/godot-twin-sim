using Godot;
using Twin.Sim.Geometry;
using Twin.Sim.Terrain;
using Twin.Sim.World;

namespace Twin.Client;

/// <summary>
/// Draws the terrain once as a shaded relief texture with contour lines, so height is visible
/// on a flat map. A real map (<see cref="RasterWorld"/>) is also colored by terrain class.
/// Pure presentation: it only reads the terrain.
/// </summary>
public partial class MapView : Sprite2D
{
    private const double SampleSpacingMeters = 2.0;
    private const double ContourIntervalMeters = 5.0;

    private readonly ITerrain _terrain = null!;
    private readonly CoordinateMapper _mapper = null!;

    /// <summary>Required by Godot. Not used directly.</summary>
    public MapView()
    {
    }

    /// <summary>Creates the view for a terrain.</summary>
    public MapView(ITerrain terrain, CoordinateMapper mapper)
    {
        _terrain = terrain;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        Centered = false;
        Texture = ImageTexture.CreateFromImage(RenderRelief());
        var scale = (float)(SampleSpacingMeters * _mapper.PixelsPerMeter);
        Scale = new Vector2(scale, scale);
    }

    private Image RenderRelief()
    {
        var width = (int)(_terrain.WidthMeters / SampleSpacingMeters);
        var height = (int)(_terrain.DepthMeters / SampleSpacingMeters);
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);

        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                // Sample the center of the pixel's square, the same cell-center convention as the raster.
                var map = new Vec2((px + 0.5) * SampleSpacingMeters, _terrain.DepthMeters - ((py + 0.5) * SampleSpacingMeters));
                image.SetPixel(px, py, ShadeAt(map));
            }
        }

        return image;
    }

    private Color ShadeAt(Vec2 p)
    {
        var h = _terrain.HeightAt(p);
        var east = _terrain.HeightAt(new Vec2(Math.Min(p.X + SampleSpacingMeters, _terrain.WidthMeters), p.Y));
        var north = _terrain.HeightAt(new Vec2(p.X, Math.Min(p.Y + SampleSpacingMeters, _terrain.DepthMeters)));

        // Light from the north-west, the cartographic convention.
        var slopeShade = 1.0 - (((east - h) - (north - h)) * 0.15);
        var baseColor = _terrain is RasterWorld raster
            ? ClassColor(raster.ClassAt(p.X, p.Y))
            : new Color(0.55f, 0.68f, 0.45f).Lerp(new Color(0.78f, 0.70f, 0.52f), Mathf.Clamp((float)(h / 60.0), 0f, 1f));
        var shaded = baseColor * Mathf.Clamp((float)slopeShade, 0.6f, 1.3f);

        var onContour = Math.Floor(h / ContourIntervalMeters) != Math.Floor(Math.Max(east, north) / ContourIntervalMeters);
        return onContour ? shaded.Darkened(0.35f) : shaded;
    }

    private static Color ClassColor(TerrainClass terrainClass) => terrainClass switch
    {
        TerrainClass.Road => new Color(0.48f, 0.45f, 0.42f),
        TerrainClass.Field => new Color(0.93f, 0.87f, 0.58f),
        TerrainClass.Forest => new Color(0.47f, 0.64f, 0.40f),
        TerrainClass.Bog => new Color(0.63f, 0.70f, 0.67f),
        TerrainClass.Water => new Color(0.48f, 0.68f, 0.88f),
        TerrainClass.Building => new Color(0.36f, 0.31f, 0.31f),
        _ => new Color(0.82f, 0.80f, 0.66f),
    };
}
