# Map preprocessing (Sim.Prep)

Turns MML open data (Elevation model 2 m GeoTIFFs and Topographic Database features) into the raw raster
layers and `manifest.json` under `scenarios/<name>/world/` (layout: `scenarios/README.md`). Runs offline, in
Docker, so nobody needs a local GDAL install. The simulation never reads GIS files.

```sh
./dev prep gdalinfo --version                                       # check the toolbox works
./dev prep python3 -m pytest -p no:cacheprovider prep/tests         # tests (W1)
./dev prep python3 prep/prep.py world --km2 <tif> --mtk <gpkg> --origin <E> <N> --size <m> --date <YYYY-MM-DD> \
    --out scenarios/<name>/world                                    # crop, rasterise, write layers and samples
./dev prep python3 prep/prep.py preview --km2 <tif> --mtk <gpkg> --origin <E> <N> --size <m> --out prep/data/x.png
```

The repository is mounted at `/work` inside the container, so use repository-relative paths. Put downloaded
source data in `prep/data/` (ignored by Git); MML's API needs a personal API key, which must not be committed.
`world` also writes `jyrkanteet.json`, the Topographic Database cliff lines in local meters (ADR 0010), and `samples.json`: 20 random cell centers with the height GDAL reads from the original GeoTIFF (W4).
The origin must lie on the elevation model's 2 m grid. Preprocessing a 1 x 1 km area takes about 0.1 s.

Image: `prep/Dockerfile` (GDAL 3.12 with Python bindings). Extra Python packages: `prep/requirements.txt`,
exact versions only. Code: `prep/simprep/` (`layers.py` writes the files, `terrain.py` rasterises, `world.py` builds).

## Terrain classes from the Topographic Database

The Topographic Database has no forest polygon: forest is land no other feature covers, so the class layer starts
as forest. A polygon sets a cell when it covers the cell's center. Overlaps follow the spec's order: building, water,
road, bog, field, forest, open.

| Class | Topographic Database layers (kohdeluokka) |
|---|---|
| building | `rakennus` |
| water | `jarvi`, `virtavesialue`, `allas`; `virtavesikapea` 36312 (2–5 m stream) buffered to 3 m |
| road | `tieviiva` buffered by class: Ia 12 m, Ib 10 m, IIa 8 m, IIb 7 m, IIIa 6 m, IIIb 5 m, ajotie 4 m, kävely- ja pyörätie 3 m |
| bog | `suo` (all bog classes) |
| field | `maatalousmaa`, `niitty` |
| open | `muuavoinalue`, `urheilujavirkistysalue`, `taajaanrakennettualue`, `autoliikennealue` |
| forest | everything else |

Choices to revisit:

- Footpaths (`polku` 12313) and tracks (`ajopolku` 12316) are not roads: they run under the canopy, and as roads every
  forest path would become a forest edge (ADR 0009).
- Ditches and streams under 2 m (`virtavesikapea` 36311) are not water: they can be stepped over, and as impassable
  cells they would cut fields into closed pockets.
- Rock outcrops (`kallioalue`) stay forest. As open land, Sipoonkorpi's hundreds of small outcrops would each be a
  forest edge.
- Water outranks road, so bridges are lost: a river is crossed only where a stream is narrower than 2 m.
