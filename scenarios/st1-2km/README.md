# st1-2km: pathfinding time on 2 x 2 km

Used only by P1 (pathfinding time is measured and recorded). 2 x 2 km in Sipoonkorpi around `st1`, south-west
corner ETRS-TM35FIN (402900, 6686100). The corridor starts like `st1`'s and continues north-east, about 1.6 km in all.

Regenerate the world:

```sh
D=prep/data/sipoonkorpi_6km
./dev prep python3 prep/prep.py world --km2 $D/korkeusmalli_2m.tif --mtk $D/maastotietokanta_kaikki.gpkg \
    --origin 402900 6686100 --size 2000 --date 2026-11-08 --out scenarios/st1-2km/world
```

## Data sources and attribution

Contains data from the National Land Survey of Finland (Maanmittauslaitos) Elevation model 2 m and Topographic
Database, downloaded 2026-11-08, licence CC BY 4.0.
