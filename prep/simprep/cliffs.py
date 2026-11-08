"""Cliff lines (Maastotietokanta "jyrkanne") for the world's jyrkanteet.json (docs/adr/0010)."""
from osgeo import ogr

ogr.UseExceptions()


def cliff_lines(gpkg_path: str, origin_e: float, origin_n: float, size_m: float) -> list:
    """Every cliff line clipped to the square area, as polylines of [x, y] in local meters (east and north of the corner).

    Coordinates are rounded to millimeters so the file is stable across GDAL versions.
    """
    source = ogr.Open(gpkg_path)
    layer = source.GetLayerByName("jyrkanne")
    if layer is None:
        return []

    area = ogr.CreateGeometryFromWkt(
        f"POLYGON(({origin_e} {origin_n},{origin_e + size_m} {origin_n},{origin_e + size_m} {origin_n + size_m},"
        f"{origin_e} {origin_n + size_m},{origin_e} {origin_n}))")
    layer.SetSpatialFilter(area)
    lines = []
    for feature in layer:
        geometry = feature.GetGeometryRef()
        if geometry is None:
            continue
        clipped = geometry.Intersection(area)
        if clipped is None or clipped.IsEmpty():
            continue
        clipped.FlattenTo2D()
        parts = [clipped] if clipped.GetGeometryCount() == 0 else \
            [clipped.GetGeometryRef(i) for i in range(clipped.GetGeometryCount())]
        for part in parts:
            if part.GetPointCount() < 2:
                continue
            lines.append([[round(part.GetX(i) - origin_e, 3), round(part.GetY(i) - origin_n, 3)]
                          for i in range(part.GetPointCount())])
    return lines
