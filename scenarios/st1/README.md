# st1: Smoke test I

One agent moves from A (in forest) to B about 800 m away, inside a corridor of about 100 m, past buildings,
water, a steep slope, bog and a field whose forest edge is a hard boundary. Run in three variants: the leader
grants permission, denies it, or does not answer. Spec: "Taistelusimulaattori: speksi v0.1 ja Smoke test I".

## Status

- [ ] Map area chosen (open question in the spec): about 1 x 1 km, plus 2 x 2 km for performance measurement
- [ ] `world/` preprocessed from MML data (`./dev prep ...`, see `prep/README.md`)
- [ ] `scenario.json` written

## Data sources and attribution

Fill in when the area is chosen. MML open data requires attribution, for example:
"Contains data from the National Land Survey of Finland Elevation model 2 m and Topographic Database, <month/year>".
The same attribution must be shown in the Godot client.
