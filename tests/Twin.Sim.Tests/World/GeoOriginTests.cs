using Twin.Sim.Geometry;
using Twin.Sim.World;

namespace Twin.Sim.Tests.World;

/// <summary>
/// The simulation works in local meters from the area's south-west corner; ETRS-TM35FIN (EPSG:3067)
/// coordinates are used only at the edges (W3).
/// </summary>
public class GeoOriginTests
{
    private static readonly GeoOrigin Origin = new(Easting: 385000.0, Northing: 6672000.0);

    [Fact]
    public void W3_etrs_to_local_is_the_offset_from_the_south_west_corner()
    {
        var local = Origin.ToLocal(new EtrsPoint(385123.4567, 6672890.1234));

        Assert.Equal(123.4567, local.X, 6);
        Assert.Equal(890.1234, local.Y, 6);
    }

    [Theory]
    [InlineData(385000.0, 6672000.0)]
    [InlineData(385123.4567, 6672890.1234)]
    [InlineData(386999.9991, 6673999.9993)]
    public void W3_etrs_to_local_and_back_returns_the_same_point_within_a_millimeter(double easting, double northing)
    {
        var original = new EtrsPoint(easting, northing);

        var roundTrip = Origin.ToEtrs(Origin.ToLocal(original));

        Assert.True(Math.Abs(roundTrip.Easting - easting) < 0.001, $"easting off by {roundTrip.Easting - easting} m");
        Assert.True(Math.Abs(roundTrip.Northing - northing) < 0.001, $"northing off by {roundTrip.Northing - northing} m");
    }

    [Fact]
    public void W3_local_to_etrs_adds_the_origin()
    {
        var etrs = Origin.ToEtrs(new Vec2(10.5, 20.25));

        Assert.Equal(385010.5, etrs.Easting, 6);
        Assert.Equal(6672020.25, etrs.Northing, 6);
    }
}
