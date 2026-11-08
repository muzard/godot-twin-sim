# Scenarios

Scenario files and preprocessed map areas used by acceptance tests, the headless runner and the Godot client.
Tests find this directory with `ScenarioFiles` and load a scenario with `ScenarioOnDisk` (`tests/Twin.Testing`).

| Scenario | Use |
|---|---|
| `st1/` | Smoke test I: about 1 x 1 km of real MML data (A1–A4, W4, P1) |
| `st1-2km/` | about 2 x 2 km, pathfinding time only (P1) |

```text
scenarios/<name>/
  README.md               what the scenario is, data sources and the required MML attribution
  scenario.json           agent, corridor, hard boundaries, scripted leader, seed (below)
  world/manifest.json     grid, origin, layers, sources, checksums (below)
  world/h_maa.bin         ground height, float32, meters (N2000)
  world/maastoluokka.bin  terrain class, uint8 (codes below)
  world/samples.json      check points read with GDAL from the original GeoTIFF (W4)
  world/jyrkanteet.json   cliff lines, local meters (ADR 0010)
```

The format follows spec v0.1 and may change with it. Layer files are raw little-endian, row-major, **row 0 = the
southernmost row**, index = row * width + column. A cell's value applies to its center. `world/*.bin` files are stored
with Git LFS (`.gitattributes`); run `git lfs install` once per machine.

## world/manifest.json

```json
{
  "schemaVersion": 1,
  "crs": "EPSG:3067",
  "origin": { "easting": 385000.0, "northing": 6672000.0 },
  "cellSize": 2.0,
  "width": 500,
  "height": 500,
  "layers": [
    { "name": "h_maa", "type": "float32", "file": "h_maa.bin", "sha256": "<hex>" },
    { "name": "maastoluokka", "type": "uint8", "file": "maastoluokka.bin", "sha256": "<hex>" }
  ],
  "cliffs": { "file": "jyrkanteet.json", "sha256": "<hex>" },
  "sources": [ { "name": "MML Korkeusmalli 2 m", "date": "2026-11-08" } ]
}
```

`origin` is the south-west corner of the area in ETRS-TM35FIN. Local coordinates are meters east and north of it.

## Terrain class codes (`maastoluokka.bin`)

| Code | Class | Code | Class |
|---|---|---|---|
| 0 | open (no other feature) | 4 | bog |
| 1 | road | 5 | water |
| 2 | field | 6 | building |
| 3 | forest | | |

Overlaps are resolved in this order: building, water, road, bog, field, forest, open.
Leaving forest is always a hard boundary (ADR 0009).

`cliffs` is optional; without it the world has no cliffs.

## world/jyrkanteet.json

Cliff lines from the Topographic Database layer `jyrkanne`, clipped to the area, as polylines in local meters. A route
may not cross one unless the agent can rappel (ADR 0010).

```json
{ "lines": [ [[120.5, 340.25], [131.0, 352.75]] ] }
```

## world/samples.json

At least 10 points at cell centers, with the height GDAL reads from the original GeoTIFF at that ETRS point:

```json
[ { "easting": 385101.0, "northing": 6672333.0, "h_maa": 23.41 } ]
```

## scenario.json

Points are ETRS-TM35FIN `[easting, northing]`, as read from a map; the simulation converts them to local meters.

```json
{
  "schemaVersion": 1,
  "seed": 1,
  "dtSeconds": 0.5,
  "agent": { "id": "alpha", "start": [385120.0, 6672110.0], "canRappel": false },
  "corridor": {
    "centerline": [[385120.0, 6672110.0], [385400.0, 6672500.0], [385830.0, 6672700.0]],
    "width": 100,
    "softMargin": 25,
    "softPenalty": 1,
    "hardBoundaries": [ { "id": "road-1", "line": [[385300.0, 6672000.0], [385300.0, 6673000.0]] } ]
  },
  "leader": { "answer": "grant", "delaySeconds": 30, "permitRadius": 25 },
  "permission": { "threshold": 1.5, "timeoutSeconds": 120 },
  "expect": { "detourWithoutCrossing": false },
  "doctrine": { "returnFireOverridesFireAt": true, "permissionAnswers": "whenFree" }
}
```

- The agent's destination B is the last centerline point. The agent is spec v0.1 infantry; `canRappel` (optional,
  default false) lets it cross cliffs (ADR 0010).
- `leader.answer` is `grant`, `deny` or `noAnswer`; acceptance tests run every variant regardless of this value.
- `hardBoundaries` are lines from the order. Forest edges need no line (ADR 0009).
- `doctrine` (optional) holds rules decided by people, not by physics (backlog 0002). Any key left out keeps its
  default, which is shown above:
  - `returnFireOverridesFireAt`: true means the duty to return fire beats a `FireAt` order whose target cannot be
    engaged.
  - `permissionAnswers`: `whenFree` means a permission answer that arrives while the agent is pinned or suppressed is
    handled when it is free; `immediately` means it is handled on time, but the agent still waits.
- `expect` is read only by tests: `detourWithoutCrossing` says whether B can be reached inside the corridor without
  crossing any hard boundary, including leaving forest. A person sets it after studying the area.

Data rules: open data only, no classified or personal data, and no areas restricted under the Finnish territorial
surveillance act (ADR 0008).
