# st1: Smoke test I

One agent moves from A (in forest) to B about 0.9 km away, inside a 100 m corridor, past buildings,
water, a steep slope, bog and a field whose forest edge is a hard boundary. Run in three variants: the leader
grants permission, denies it, or does not answer. Spec: "Taistelusimulaattori: speksi v0.1 ja Smoke test I".

## Area

1 x 1 km in Sipoonkorpi (Sipoo/Vantaa), south-west corner ETRS-TM35FIN (403400, 6686600). Forest with rock
outcrops and hills, fields and roads in the middle, a building group in the east, a lake in the south-west and a
bog in the west. The area is not restricted under the Finnish territorial surveillance act.

The corridor runs from A (403460, 6686940) through (403850, 6686850) to B (404300, 6687050). A is in forest
and B is in a separate forest patch across fields and roads, so every route leaves the forest at least once:

| Leader | Result (with the values in `scenario.json`) |
|---|---|
| grants | asks once at t = 0.5 s, gets a permit 30 s later, arrives at B at t ≈ 1260 s |
| denies | asks, is denied, reports `Blocked` at A (`expect.detourWithoutCrossing` is false) |
| no answer | asks, times out after 120 s, reports `Blocked` at A |

Regenerate the world (takes under a second):

```sh
D=prep/data/sipoonkorpi_6km
./dev prep python3 prep/prep.py world --km2 $D/korkeusmalli_2m.tif --mtk $D/maastotietokanta_kaikki.gpkg \
    --origin 403400 6686600 --size 1000 --date 2026-11-08 --out scenarios/st1/world
```

## Data sources and attribution

Contains data from the National Land Survey of Finland (Maanmittauslaitos) Elevation model 2 m and Topographic
Database, downloaded 2026-11-08, licence CC BY 4.0. The same attribution must be shown in the Godot client.
