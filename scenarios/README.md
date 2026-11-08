# Scenarios

Scenario files and preprocessed map areas used by acceptance tests, the headless runner and the Godot client.
Tests find this directory with `ScenarioFiles` in `tests/Twin.Testing`.

```text
scenarios/<name>/
  README.md            what the scenario is, data sources and the required attribution
  scenario.json        points A and B, corridor, hard boundaries, scripted leader, seed
  world/manifest.json  origin (ETRS-TM35FIN), cell size, width and height in cells, layers, sources, checksums
  world/<layer>.bin    one raw little-endian raster per layer, row 0 = southernmost row
```

`world/*.bin` files are stored with Git LFS (`.gitattributes`). Run `git lfs install` once per machine.

The layout follows the Smoke test I spec draft (v0.1) and may change with it. Data rules: open data only,
no classified or personal data, and no areas restricted under the Finnish territorial surveillance act
(ADR 0008).
