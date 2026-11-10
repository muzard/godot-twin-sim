"""Builds scenarios/<name>/world/: the layers, manifest.json and samples.json (scenarios/README.md)."""
import json
from datetime import date
from pathlib import Path

import numpy as np
from osgeo import gdal

import hashlib

from simprep.cliffs import cliff_lines
from simprep.layers import write_layer
from simprep.terrain import classify

CELL_SIZE = 2.0


def build_world(km2_tif: str, mtk_gpkg: str, origin_e: float, origin_n: float, size_m: float, out_dir,
                sample_count: int = 20, seed: int = 1, source_date: str | None = None) -> dict:
    """Crops the elevation model and rasterises the Topographic Database for a square area.

    origin_e/origin_n is the south-west corner in ETRS-TM35FIN and must lie on the elevation model's 2 m grid.
    Returns the manifest.
    """
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    elevation = gdal.Open(km2_tif)
    if elevation is None:
        raise FileNotFoundError(km2_tif)
    gt = elevation.GetGeoTransform()
    if gt[1] != CELL_SIZE or gt[5] != -CELL_SIZE:
        raise ValueError(f"expected a {CELL_SIZE} m grid, got {gt[1]} x {gt[5]}")

    cells = int(round(size_m / CELL_SIZE))
    top_n = origin_n + cells * CELL_SIZE
    col0 = (origin_e - gt[0]) / CELL_SIZE
    row0 = (gt[3] - top_n) / CELL_SIZE
    if col0 != int(col0) or row0 != int(row0):
        raise ValueError("the origin is not on the elevation model's grid")
    col0, row0 = int(col0), int(row0)
    if col0 < 0 or row0 < 0 or col0 + cells > elevation.RasterXSize or row0 + cells > elevation.RasterYSize:
        raise ValueError("the area is not inside the elevation model")

    band = elevation.GetRasterBand(1)
    heights = band.ReadAsArray(col0, row0, cells, cells).astype(np.float32)
    nodata = band.GetNoDataValue()
    if nodata is not None and np.any(heights == nodata):
        raise ValueError("the area has cells without elevation data")

    crop_gt = (origin_e, CELL_SIZE, 0.0, top_n, 0.0, -CELL_SIZE)
    classes = classify(mtk_gpkg, crop_gt, cells, cells, elevation.GetProjection())

    manifest = {
        "schemaVersion": 1,
        "crs": "EPSG:3067",
        "origin": {"easting": origin_e, "northing": origin_n},
        "cellSize": CELL_SIZE,
        "width": cells,
        "height": cells,
        "layers": [
            {"name": "h_maa", "type": "float32", "file": "h_maa.bin",
             "sha256": write_layer(out / "h_maa.bin", heights)},
            {"name": "maastoluokka", "type": "uint8", "file": "maastoluokka.bin",
             "sha256": write_layer(out / "maastoluokka.bin", classes)},
        ],
        "sources": [
            {"name": "MML Korkeusmalli 2 m", "file": Path(km2_tif).name, "date": source_date or str(date.today())},
            {"name": "MML Maastotietokanta", "file": Path(mtk_gpkg).name, "date": source_date or str(date.today())},
        ],
    }
    cliffs = json.dumps({"lines": cliff_lines(mtk_gpkg, origin_e, origin_n, cells * CELL_SIZE)}, separators=(",", ":")) + "\n"
    (out / "jyrkanteet.json").write_text(cliffs)
    manifest["cliffs"] = {"file": "jyrkanteet.json", "sha256": hashlib.sha256(cliffs.encode()).hexdigest()}
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (out / "samples.json").write_text(json.dumps(sample_heights(km2_tif, origin_e, origin_n, cells, sample_count, seed),
                                                 indent=2) + "\n")
    return manifest


def sample_heights(km2_tif: str, origin_e: float, origin_n: float, cells: int, count: int, seed: int) -> list:
    """Random cell centres in the area, with the height GDAL reads at that ETRS point from the original GeoTIFF (W4)."""
    dataset = gdal.Open(km2_tif)
    gt = dataset.GetGeoTransform()
    band = dataset.GetRasterBand(1)
    rng = np.random.default_rng(seed)
    samples = []
    for column, row in zip(rng.integers(0, cells, count), rng.integers(0, cells, count)):
        easting = origin_e + (column + 0.5) * CELL_SIZE
        northing = origin_n + (row + 0.5) * CELL_SIZE  # row counted from the south
        pixel = int((easting - gt[0]) / gt[1])
        line = int((northing - gt[3]) / gt[5])
        value = float(band.ReadAsArray(pixel, line, 1, 1)[0, 0])
        samples.append({"easting": easting, "northing": northing, "h_maa": value})
    return samples
