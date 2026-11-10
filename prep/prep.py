"""Sim.Prep command line. Run inside the toolbox image: ./dev prep python3 prep/prep.py <command> ...

  world    crop the elevation model and rasterise the Topographic Database into scenarios/<name>/world/
  preview  write a PNG of the terrain classes (and hillshade) for an area, to choose scenario points
"""
import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

import numpy as np  # noqa: E402
from osgeo import gdal  # noqa: E402

from simprep.terrain import classify  # noqa: E402
from simprep.world import CELL_SIZE, build_world  # noqa: E402

COLORS = np.array([
    [222, 214, 170],  # open
    [90, 90, 90],  # road
    [240, 220, 110],  # field
    [60, 120, 60],  # forest
    [150, 110, 160],  # bog
    [70, 130, 210],  # water
    [200, 40, 40],  # building
], dtype=np.uint8)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("world", "preview"):
        cmd = sub.add_parser(name)
        cmd.add_argument("--km2", required=True, help="MML Korkeusmalli 2 m GeoTIFF")
        cmd.add_argument("--mtk", required=True, help="MML Maastotietokanta GeoPackage")
        cmd.add_argument("--origin", required=True, nargs=2, type=float, metavar=("E", "N"),
                         help="south-west corner, ETRS-TM35FIN")
        cmd.add_argument("--size", required=True, type=float, help="side of the square area in meters")
        cmd.add_argument("--out", required=True, help="output directory (world) or PNG file (preview)")
    sub.choices["world"].add_argument("--date", required=True, help="source download date for the manifest (YYYY-MM-DD)")
    args = parser.parse_args(argv)

    started = time.perf_counter()
    if args.command == "world":
        manifest = build_world(args.km2, args.mtk, args.origin[0], args.origin[1], args.size, args.out,
                               source_date=args.date)
        print(f"wrote {args.out}: {manifest['width']} x {manifest['height']} cells")
    else:
        preview(args.km2, args.mtk, args.origin[0], args.origin[1], args.size, args.out)
        print(f"wrote {args.out}")
    print(f"preprocessing took {time.perf_counter() - started:.1f} s")
    return 0


def preview(km2_tif, mtk_gpkg, origin_e, origin_n, size_m, out_png):
    elevation = gdal.Open(km2_tif)
    gt = elevation.GetGeoTransform()
    cells = int(round(size_m / CELL_SIZE))
    top_n = origin_n + cells * CELL_SIZE
    col0, row0 = int((origin_e - gt[0]) / CELL_SIZE), int((gt[3] - top_n) / CELL_SIZE)
    heights = elevation.GetRasterBand(1).ReadAsArray(col0, row0, cells, cells).astype(np.float64)
    classes = classify(mtk_gpkg, (origin_e, CELL_SIZE, 0, top_n, 0, -CELL_SIZE), cells, cells,
                       elevation.GetProjection())

    dy, dx = np.gradient(heights, CELL_SIZE)
    shade = np.clip(0.75 + 0.5 * (-dx - dy) / np.sqrt(2) / (1 + np.hypot(dx, dy)), 0.3, 1.1)
    rgb = np.clip(COLORS[classes] * shade[..., None], 0, 255).astype(np.uint8)
    driver = gdal.GetDriverByName("MEM").Create("", cells, cells, 3, gdal.GDT_Byte)
    for i in range(3):
        driver.GetRasterBand(i + 1).WriteArray(rgb[..., i])
    gdal.GetDriverByName("PNG").CreateCopy(out_png, driver)


if __name__ == "__main__":
    sys.exit(main())
