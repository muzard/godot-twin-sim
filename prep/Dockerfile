# Map preprocessing toolbox (Sim.Prep in the Smoke test I spec): Python + GDAL, offline, never at simulation run time.
# The simulation core does not need GDAL; it reads the raw layers and manifest.json this tool writes.
#
#   ./dev prep gdalinfo --version          any command, with the repository mounted at /work
#   ./dev prep python prep/prep.py ...     the preprocessing script, once it exists
#
# The base image ships GDAL with its Python bindings and numpy. Extra Python packages go in requirements.txt.
FROM ghcr.io/osgeo/gdal:ubuntu-small-3.12.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends python3-pip \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt /tmp/requirements.txt
RUN pip install --no-cache-dir --break-system-packages -r /tmp/requirements.txt

WORKDIR /work
