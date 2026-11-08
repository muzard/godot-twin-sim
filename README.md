# Twin — digital twin walking skeleton

A deterministic 2.5D simulation of terrain and agents, with a Godot 4 client that shows it as a top-down
map. This repository is a *walking skeleton*: one feature (ordering a unit to move) is built end to end
using the team's agreed workflow, so every next feature can follow the same path.

## Quick start

Prerequisites: [.NET SDK 10](https://dotnet.microsoft.com/download) (pinned in `global.json`),
Git with [Git LFS](https://git-lfs.com), and — only for the visual client —
[Godot 4.7.2 .NET edition](https://godotengine.org/download/archive/4.7.2-stable/).

```sh
git lfs install
./dev setup          # Windows: dev setup
```

`./dev setup` checks prerequisites, builds everything and runs all tests. The simulation and its tests
need only the .NET SDK, not Godot. [Docker](https://www.docker.com/) is needed only for `./dev docker-test`
and the map preprocessing (`./dev prep`).

Run the visual client: open `src/Twin.Client/project.godot` in Godot 4.7.2 .NET and press Play.
Left click selects a unit, right click orders it to move, the mouse wheel zooms.

Run the simulation without graphics: `./dev run -- --seconds 60`.

## Repository map

| Path | What |
|---|---|
| `src/Twin.Sim` | Simulation core. Plain .NET, deterministic, no engine dependency. |
| `src/Twin.Client` | Godot client: rendering, input, UI. No game rules. |
| `src/Twin.Headless` | Console runner for smoke tests, batch runs, later a server. |
| `tests/Twin.Testing` | Scenario kit: the language for behaviour tests. |
| `tests/Twin.Scenarios.Tests` | Behaviour tests of agents ("unit reaches X, Y, Z"). |
| `tests/Twin.Sim.Tests` | Unit tests of pure logic (terrain, math, random). |
| `tests/Twin.Architecture.Tests` | Architecture rules as tests. |
| `tests/Twin.Acceptance.Tests` | Acceptance tests on real, preprocessed map data (Smoke test I). |
| `scenarios/` | Scenario files and preprocessed map areas. |
| `prep/` | Map preprocessing toolbox: Python + GDAL in Docker. |
| `docker/` | Container for running the core tests (`./dev docker-test`). |
| `tools/dev.cs`, `./dev` | Team command line. |
| `.claude/` | AI harness: hooks, skills, reviewer subagent. |
| `docs/adr/` | Architecture decision records: what we decided and why. |
| `docs/backlog/` | Next tickets, including the onboarding validation feature. |

## How we work

Read, in this order: [AGENTS.md](AGENTS.md) (rules and commands, ten minutes),
[docs/workflow.md](docs/workflow.md) (the TDD process and roles), [docs/testing.md](docs/testing.md)
(writing scenario tests), then skim [docs/adr](docs/adr).

## Status

| Area | State |
|---|---|
| Move-to-position order | Done, tested, visible in client |
| TDD workflow, AI harness, CI | Done; to be validated by the first onboarding feature |
| Stop order | Next ticket: [docs/backlog/0001-stop-order.md](docs/backlog/0001-stop-order.md) |
| Pathfinding, line of sight, NATO symbols | Not started |
