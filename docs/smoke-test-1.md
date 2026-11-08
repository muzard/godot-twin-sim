# Smoke test I: how to run it

Runbook for the environment behind the spec draft "Taistelusimulaattori: speksi v0.1 ja Smoke test I".
The tests themselves are written in the spec phase (docs/workflow.md); this page says where they go and how
each acceptance criterion is checked.

## Where the tests go

| Spec IDs | What | Project and folder |
|---|---|---|
| W1–W3 | world: row order, bilinear height, ETRS ↔ local | `tests/Twin.Sim.Tests/World/` |
| W4 | preprocessed values match GDAL | GDAL side in `prep/` (`./dev prep ...`), C# side in `tests/Twin.Acceptance.Tests/` |
| M1–M8 | movement and pathfinding on small synthetic rasters | `tests/Twin.Scenarios.Tests/Movement/` |
| C1–C7 | corridor, hard boundaries, permission requests | `tests/Twin.Scenarios.Tests/Corridor/` |
| A1–A4, P1 | real MML area `scenarios/st1/` | `tests/Twin.Acceptance.Tests/SmokeTest1/` |

Put the spec ID at the start of the test name: `M3_diagonal_step_across_a_building_corner_is_not_allowed`.
Real map data is read through `ScenarioFiles.Require("st1", "world/manifest.json")`, which fails clearly when
the data is missing or is an unfetched Git LFS pointer.

## Workflow

1. A human runs `./dev phase spec`. Write the tests; `src/` stays locked.
2. `./dev test`: the new tests fail for the expected reason (often a missing type: the whole test project
   then fails to compile, which is expected in this phase).
3. Review, then a human runs `./dev phase implement`.

## Acceptance criteria

| # | How it is checked |
|---|---|
| 1–3 | `./dev test` (all tests) or `./dev smoke` (only `Twin.Acceptance.Tests`, with test output shown) |
| 4 | `./dev docker-test`: builds `docker/test.Dockerfile`, runs the suite in Linux, prints the time against the 60 s limit. CI job `docker-test` does the same |
| 5 | Manual: open `src/Twin.Client/project.godot` in Godot 4.7.2 .NET |
| 6 | Preprocessing: time `./dev prep ...`. Pathfinding (P1): measure with `Stopwatch` inside the test (allowed in tests, banned in `src/Twin.Sim`) and print through xUnit's `ITestOutputHelper`; `./dev smoke` shows it. Godot frame rate: run the client with `--print-fps` |
| 7 | Human review |

## Differences between the spec draft and this repository

The spec proposes some things the repository already decided differently. Tests should follow the repository
unless the team changes the ADR:

- .NET 10 and `net10.0` (ADR 0007), not .NET 8: .NET 8 support ends in November 2026, and Godot 4.7.2 builds
  `net10.0` fine.
- Projects are `Twin.Sim`, `Twin.Headless`, `Twin.Client`, not `Sim.Core`/`Sim.World`/`Sim.Cli`/`Sim.Godot`.
- The tick rate is configurable (`SimulationConfig.TickRateHz`, default 20 Hz). The spec's dt = 0.5 s is 2 Hz.
- The existing `ITerrain` covers height only; the spec's `IWorldQuery` (terrain class, cell size) is new API.
