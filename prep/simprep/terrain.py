"""Rasterises MML Topographic Database (Maastotietokanta) features into the terrain class layer.

The Topographic Database has no forest polygon: forest is the land not covered by any other feature, so the layer
starts as forest. Features are burned from the lowest to the highest priority, so the spec's overlap order holds:
building, water, road, bog, field, forest, open (scenarios/README.md). The mapping is listed in prep/README.md.
"""
from dataclasses import dataclass

import numpy as np
from osgeo import gdal, ogr

gdal.UseExceptions()

OPEN, ROAD, FIELD, FOREST, BOG, WATER, BUILDING = 0, 1, 2, 3, 4, 5, 6

# Road centre lines (tieviiva) by class: width in meters. Footpaths (12313 polku) and tracks (12316 ajopolku) are
# not roads: they run under the canopy and would turn every forest path into a forest edge (ADR 0009).
ROAD_WIDTHS = {
    12111: 12.0, 12112: 10.0,  # autotie Ia, Ib
    12121: 8.0, 12122: 7.0,  # autotie IIa, IIb
    12131: 6.0, 12132: 5.0,  # autotie IIIa, IIIb
    12141: 4.0,  # ajotie
    12314: 3.0,  # kävely- ja pyörätie
}

# Narrow watercourses (virtavesikapea): 36312 is 2-5 m wide and is water. 36311 (under 2 m, mostly ditches) is left
# out: it can be stepped over, and as impassable 2 m cells it would cut fields into closed pockets.
STREAM_WIDTHS = {36312: 3.0}


@dataclass(frozen=True)
class Burn:
    """One Topographic Database layer burned into the class raster."""

    layer: str
    value: int
    buffer_by_class: dict | None = None  # line layers: kohdeluokka -> width in meters


# Lowest priority first; later burns overwrite earlier ones.
BURNS = [
    Burn("muuavoinalue", OPEN),
    Burn("urheilujavirkistysalue", OPEN),
    Burn("taajaanrakennettualue", OPEN),
    Burn("autoliikennealue", OPEN),
    Burn("maatalousmaa", FIELD),
    Burn("niitty", FIELD),
    Burn("suo", BOG),
    Burn("tieviiva", ROAD, ROAD_WIDTHS),
    Burn("jarvi", WATER),
    Burn("virtavesialue", WATER),
    Burn("allas", WATER),
    Burn("virtavesikapea", WATER, STREAM_WIDTHS),
    Burn("rakennus", BUILDING),
]


def classify(gpkg_path: str, geotransform, width: int, height: int, srs_wkt: str) -> np.ndarray:
    """Returns the terrain classes for the grid, north-up (row 0 = north, as GDAL reads it), uint8.

    A cell takes a polygon's class when the polygon covers the cell centre (GDAL's default rasterisation).
    """
    target = gdal.GetDriverByName("MEM").Create("", width, height, 1, gdal.GDT_Byte)
    target.SetGeoTransform(geotransform)
    target.SetProjection(srs_wkt)
    target.GetRasterBand(1).Fill(FOREST)

    source = ogr.Open(gpkg_path)
    if source is None:
        raise FileNotFoundError(gpkg_path)
    min_x = geotransform[0]
    max_y = geotransform[3]
    max_x = min_x + width * geotransform[1]
    min_y = max_y + height * geotransform[5]

    for burn in BURNS:
        layer = source.GetLayerByName(burn.layer)
        if layer is None:
            continue
        layer.SetSpatialFilterRect(min_x - 20, min_y - 20, max_x + 20, max_y + 20)
        if burn.buffer_by_class is None:
            gdal.RasterizeLayer(target, [1], layer, burn_values=[burn.value])
        else:
            _burn_buffered_lines(target, layer, burn)

    return target.GetRasterBand(1).ReadAsArray().astype(np.uint8)


def _burn_buffered_lines(target, layer, burn: Burn) -> None:
    memory = ogr.GetDriverByName("MEM").CreateDataSource("")
    buffered = memory.CreateLayer("buffered", layer.GetSpatialRef(), ogr.wkbPolygon)
    definition = buffered.GetLayerDefn()
    layer.ResetReading()
    for feature in layer:
        width = burn.buffer_by_class.get(feature.GetField("kohdeluokka"))
        geometry = feature.GetGeometryRef()
        if width is None or geometry is None:
            continue
        flat = geometry.Clone()
        flat.FlattenTo2D()
        polygon = ogr.Feature(definition)
        polygon.SetGeometry(flat.Buffer(width / 2.0))
        buffered.CreateFeature(polygon)
    gdal.RasterizeLayer(target, [1], buffered, burn_values=[burn.value])
