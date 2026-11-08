# Smoke test I: how to run it

Runbook for the environment behind the spec draft "Taistelusimulaattori: speksi v0.1 ja Smoke test I".
The tests themselves are written in the spec phase (docs/workflow.md); this page says where they go and how
each acceptance criterion is checked.

## Where the tests go

| Spec IDs | What | Project and folder |
|---|---|---|
| W1 | row 0 is the southernmost row | C# reader: `tests/Twin.Sim.Tests/World/WorldFilesTests.cs`; preprocessing: `prep/tests/test_layers.py` |
| W2–W3 | bilinear height, ETRS ↔ local | `tests/Twin.Sim.Tests/World/` |
| W4 | preprocessed values match GDAL | `tests/Twin.Acceptance.Tests/SmokeTest1/PreprocessedWorldTests.cs`, against `world/samples.json` |
| M1–M7 | pathfinding on small synthetic rasters | `tests/Twin.Scenarios.Tests/Movement/PathfindingTests.cs` |
| M8 | agent motion and eye height | `tests/Twin.Scenarios.Tests/Movement/AgentMovementTests.cs` |
| C1–C3, C6 | corridor zones and permits in pathfinding | `tests/Twin.Scenarios.Tests/Corridors/CorridorPathTests.cs` |
| C4–C7 | hard boundaries and permission requests | `tests/Twin.Scenarios.Tests/Corridors/PermissionTests.cs` |
| A1–A4 | real MML area `scenarios/st1/` | `tests/Twin.Acceptance.Tests/SmokeTest1/SmokeTest1Tests.cs` |
| P1 | pathfinding time, 1 x 1 km and 2 x 2 km | `tests/Twin.Acceptance.Tests/SmokeTest1/PathfindingPerformanceTests.cs` |

The spec ID starts the test name: `M3_a_diagonal_step_across_a_building_corner_is_not_allowed`. Tests set the spec's
starting values explicitly through `SpecV01` (`tests/Twin.Testing`), so calibrating defaults does not change them.
Small worlds are built with `TestMap`; routes are checked with `RouteAudit`; real scenarios load with `ScenarioOnDisk`,
which fails clearly when data is missing or is an unfetched Git LFS pointer. File formats: `scenarios/README.md`.
Forest edges are always hard boundaries when leaving the forest (ADR 0009).

Python tests (preprocessing): `./dev prep python3 -m pytest -p no:cacheprovider prep/tests`.

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

## Implementation map

| Spec part | Code |
|---|---|
| World layers, `IWorldQuery`, ETRS origin | `src/Twin.Sim/World/` |
| A*, movement profile, slope factor, cliffs and rappelling (ADR 0010) | `src/Twin.Sim/Movement/` |
| Corridor, hard boundaries, permits | `src/Twin.Sim/Corridors/`, `Pathfinder` |
| Permission requests, waiting, moving along the route | `src/Twin.Sim/Systems/CorridorMovementSystem.cs` |
| Scripted leader | `src/Twin.Sim/Command/` |
| Event log (JSON Lines) and replay | `src/Twin.Sim/Events/` |
| `scenario.json` | `src/Twin.Sim/Scenarios/ScenarioDefinition.cs` |
| Preprocessing | `prep/` (see `prep/README.md`) |

The event log has one JSON object per line, `{"v":1,"type":...,"time":...,"agent":...}` plus the event's fields.
Besides the spec's `PathPlanned`, `PermissionRequested`, `PermissionAnswered`, `Arrived` and `Blocked`, the log has
`AgentPlaced`, `AgentMoved` (position after every step in which it changed), `ActivityChanged` and
`PermissionTimedOut`, so `EventReplay` can rebuild the state at any step from the log alone (A4).

Choices made while implementing, beyond the tests:

- A route that only touches a drawn hard boundary counts as crossing it (conservative).
- Near the corridor's outer limit an arc is sampled, not only its end cells, because the limit is not convex at bends.
- The legs from the agent to its cell center and from the goal cell center to B are checked for boundaries too.
- The agent asks for one boundary at a time: the first unpermitted crossing on the reference route. After a grant it
  plans again and may ask for the next one. After the first denial or timeout it stops asking.
- The search covers the corridor's bounding rectangle (spec: "Hakualue").

## Measurements (2026-11-08, Apple silicon, Debug build)

| What | Result |
|---|---|
| Preprocessing, 1 x 1 km / 2 x 2 km | about 0.1 s each (`prep.py world`) |
| Pathfinding st1 (1 x 1 km): respecting / reference | 66 ms / 108 ms (with cliffs) |
| Pathfinding st1-2km (2 x 2 km): respecting / reference | 195 ms / 298 ms (with cliffs) |
| `./dev test`, all 110 tests | about 1.5 s of test time |
| Godot frame rate on the whole area | not measured yet (viewer not built) |
