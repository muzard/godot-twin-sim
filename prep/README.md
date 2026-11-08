# Map preprocessing (Sim.Prep)

Turns MML open data (Elevation model 2 m GeoTIFFs and Topographic Database features) into the raw raster
layers and `manifest.json` under `scenarios/<name>/world/` (layout: `scenarios/README.md`). Runs offline, in
Docker, so nobody needs a local GDAL install. The simulation never reads GIS files.

```sh
./dev prep gdalinfo --version                 # check the toolbox works (builds the image on first use)
./dev prep python3 prep/prep.py ...           # the preprocessing script, once written
```

The repository is mounted at `/work` inside the container, so use repository-relative paths. Put downloaded
source data in `prep/data/` (ignored by Git); MML's API needs a personal API key, which must not be committed.

Image: `prep/Dockerfile` (GDAL 3.12 with Python bindings). Extra Python packages: `prep/requirements.txt`,
exact versions only.
