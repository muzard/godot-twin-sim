"""Cliff lines (jyrkänne) from the Topographic Database, in local meters and clipped to the area (docs/adr/0010)."""
from osgeo import ogr, osr

from simprep.cliffs import cliff_lines


def _gpkg_with_cliffs(path, lines):
    srs = osr.SpatialReference()
    srs.ImportFromEPSG(3067)
    source = ogr.GetDriverByName("GPKG").CreateDataSource(str(path))
    layer = source.CreateLayer("jyrkanne", srs, ogr.wkbLineString25D)
    for coords in lines:
        line = ogr.Geometry(ogr.wkbLineString25D)
        for x, y in coords:
            line.AddPoint(x, y, 10.0)
        feature = ogr.Feature(layer.GetLayerDefn())
        feature.SetGeometry(line)
        layer.CreateFeature(feature)
    source = None


def test_cliff_lines_are_in_local_meters_from_the_south_west_corner(tmp_path):
    path = tmp_path / "mtk.gpkg"
    _gpkg_with_cliffs(path, [[(400010.0, 6686020.0), (400030.0, 6686025.0)]])

    lines = cliff_lines(str(path), 400000.0, 6686000.0, 100.0)

    assert lines == [[[10.0, 20.0], [30.0, 25.0]]]


def test_cliff_lines_are_clipped_to_the_area(tmp_path):
    path = tmp_path / "mtk.gpkg"
    _gpkg_with_cliffs(path, [[(399950.0, 6686050.0), (400050.0, 6686050.0)],  # crosses the west edge
                             [(400500.0, 6686500.0), (400600.0, 6686600.0)]])  # outside

    lines = cliff_lines(str(path), 400000.0, 6686000.0, 100.0)

    assert lines == [[[0.0, 50.0], [50.0, 50.0]]]
