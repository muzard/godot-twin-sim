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
`PermissionTimedOut`, so `EventReplay` can rebuild the state at any step from the log alone (A4). Combat
(backlog 0002) adds `ShotFired`, `ShotHeard`, `HealthChanged`, `CasualtyOccurred`, `SuppressionStarted`,
`SuppressionEnded` and `PostureChanged`; replay rebuilds health, posture and suppression too. Backlog 0003 adds the
fire mode and rounds to `ShotFired`, replaces `SuppressionStarted`'s `until` with `heavy`, and adds
`ContactDrillStageChanged` and `ContactReported`; replay also rebuilds heavy suppression, the drill stage and waiting
for orders. Squads (backlog 0004) add `SquadFormed` (with the teams and their first leaders), `CommandIssued`,
`CommandReceived`, `LeaderChanged`, `TeamLeaderChanged`, `FormationChanged`, `OrderRequested`, `OrderRepeated` and
`EdgeDeployed` (the squad's open line at the forest edge has formed; the deployment itself is a `CommandIssued`);
replay rebuilds each squad's leader, team leaders and formation. A
leader's place is empty from its `HealthChanged` to `OutOfAction` until its successor's change event.

To look at a run, `./dev replay st1` runs the scenario until the agent is idle or blocked (at most 2 h of simulated
time), writes the log to `replays/st1-Grant.jsonl` (ignored by Git), reads it back and prints every agent's state
every 60 s of simulated time. Options: `--answer Grant|Deny|NoAnswer` (the scripted leader), `--every <seconds>`,
`--out <file.jsonl>`, `--max-seconds <s>` (exit code 2 if the agent is still busy when it runs out). `./dev replay replays/st1-Grant.jsonl` only replays an existing log.
Relative paths are relative to the repository root. The code is `src/Twin.Headless/ScenarioReplay.cs`.

Add `--godot` to watch the run on the map: `./dev replay st1 --godot` (or `./dev replay replays/st1-Grant.jsonl --godot`)
builds the client and opens `src/Twin.Client/Replay.tscn` in Godot (found through `GODOT`, `PATH` or
`Godot_mono.app` in `/Applications` or `~/Applications`; on Windows set `GODOT` to the Godot .NET executable). `GODOT` may be
a full path, `~/...`, a path relative to the directory you run `./dev` from, or on macOS the `.app` itself; if Godot cannot
be started, `./dev` says which path it tried (installing Godot: `GODOT_README.md`). The viewer runs no simulation: it rebuilds each moment from
the log with `EventReplay` and draws the terrain classes with relief and contours, the corridor (inner zone, soft
zone, centerline, hard boundaries), cliffs, the planned route, the trail walked so far, permission requests and their answers, shots
of the last second, casualties and the agents (color = health, flatter = lower posture, yellow edge = suppressed),
with the latest events listed on the right. Keys: space play/pause, left/right −/+1 s (shift 10 s), up/down speed,
home/end; wheel zooms, dragging pans, the slider at the bottom seeks. A log whose file name does not start with its
scenario needs `--scenario <name>`. The camera starts framed on where the agents start and the corridor.

`./dev replay st1-ambush` (add `--godot` to watch it) runs the scenario `scenarios/st1-ambush` (backlog 0004): the
reference squad of nine, in a wedge, starts in the forest on the corridor's second leg and is ordered to B; three red
fighters with a machine gun lie prone in the copse across the field, watching it. Both sides fire freely at enemies they
have spotted. It shows what the simulation does today: spotting, spot reports, the squad halting, and each fighter's
drill under fire; the squad leader does not yet direct fire or manoeuvre (backlog 0004 S5b, S6), so the squad stays
where it halted. A squad scenario runs until its leader arrives or is blocked, one side has nobody in action, or nobody
has fired for 120 s after the first shot; the log goes to `replays/st1-ambush-Grant.jsonl`.

`./dev replay st1-patrol` (add `--godot` to watch it) runs a built-in patrol (`src/Twin.Headless/PatrolDemo.cs`): the
reference squad walks east across the big field in the middle of st1 in file, crossing the road; past x = 480 m its leader
commands a wedge; past x = 560 m three red fighters in the copse on the field's north side, about 20 m from the route,
open fire on its flank. The scenario format cannot yet change formation on the way, so the run is scripted. There is no
concealment model yet (prone fighters at a forest edge are spotted across the open field from far off), so red is placed
only when it opens fire. It writes `replays/st1-patrol.jsonl`. `./dev replay st1-hill` is the same patrol ambushed from
the south edge of the wooded rise north-west of the field, about 125 m from the route and 8 m above it (from lower ground
nearer the route a low swell in the field hides the route); the wedge comes past x = 450 m, the fire past x = 520 m.

The viewer draws agents as dots in their side's colour (blue, red; grey for an agent that is not a fighter), smaller the
lower the posture, ringed orange when pinned, red when wounded and yellow when suppressed, grey with a cross when out of
action; a squad leader has a second ring and its squad's id and formation (or `halted`) next to it. Each agent's watch direction is a short line, its observation sectors translucent wedges, and a small mark above
it shows that it watches the air. A fresh sighting (`EnemySpotted`) is a dashed line to the enemy for 4 s, a spot report a
ring of the 30 m voice range, and where each side believes an enemy is a hollow diamond with the enemy's id. For this the
log records each fighter's side in `AgentPlaced` and its watch in `WatchChanged` (direction, sectors, air watch, logged
when they change); replay rebuilds both.

`./dev replay st1-combat` (add `--godot` to watch it) runs a built-in fire fight instead of a scenario file, because
`scenario.json` holds one agent and no opponents: two fire teams of three under free fire with the backlog 0002 rifle,
placed on the field of acceptance test K2 (`src/Twin.Headless/CombatDemo.cs`). Since backlog 0004 S5a1 fighters must
spot an enemy before they fire, so each side starts watching toward the other (`Watch`). It runs until one side has nobody in
action or nobody has fired for 120 s, and writes `replays/st1-combat.jsonl`.

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
