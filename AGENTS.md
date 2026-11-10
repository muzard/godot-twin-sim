# AGENTS.md

Instructions for everyone who changes this repository: people and AI coding agents alike.
Keep this file short and concrete. The reasoning behind each rule lives in `docs/adr/`.

## What this is

A digital twin of a military operating environment. A deterministic simulation (`src/Twin.Sim`)
models terrain and agents (units); a Godot client (`src/Twin.Client`) shows it as a top-down 2D map.
The world model is 2.5D: one ground height per map position (ADR 0004).

## Commands

| Task | Command |
|---|---|
| First-time setup | `./dev setup` (Windows: `dev setup`) |
| Fast tests (run constantly) | `./dev test` — filter: `./dev test --filter MoveTo` |
| Everything CI checks | `./dev check` |
| Format code | `./dev format` |
| Show TDD phase | `./dev phase` |
| Headless demo | `./dev run -- --seconds 60` |
| Acceptance tests on real map data only | `./dev smoke` |
| Core tests in Docker, timed | `./dev docker-test` |
| Map preprocessing toolbox (GDAL) | `./dev prep <command>` (see `prep/README.md`) |

## Golden rules

1. **Tests first.** Every behaviour change starts with a failing test. See "Workflow" below.
2. **The simulation knows nothing about Godot.** No engine, UI or rendering code in `src/Twin.Sim`.
   Game rules never go in `src/Twin.Client`. (ADR 0002, enforced by `tests/Twin.Architecture.Tests`)
3. **The simulation is deterministic.** No `DateTime.Now`, `Stopwatch`, `System.Random`, `Guid.NewGuid`,
   threads, `Dictionary`/`HashSet` iteration. Use `Simulation.TimeSeconds`, `Simulation.Random`,
   `SortedDictionary`/`List`. (ADR 0005, enforced by architecture and determinism tests)
4. **Coordinates:** meters; X east, Y north, Z up. Conversion to Godot happens only in
   `src/Twin.Client/CoordinateMapper.cs`. (ADR 0003)
5. **Never weaken a test to make it pass.** If a test looks wrong, stop and say why.

## Workflow (test-driven, in two phases)

The TDD phase is set by a human with `./dev phase spec|implement`. Hooks enforce it for Claude Code.

1. **Spec phase** (`./dev phase spec`): turn the ticket's acceptance criteria into scenario tests.
   `src/` is locked. Run `./dev test` and confirm the new tests fail *for the expected reason*.
2. **Human review:** a person reads the tests and agrees they describe the right behaviour.
   This is the most important step: the tests are the specification.
3. **Implement phase** (`./dev phase implement`): make the tests pass with the simplest code in `src/`.
   Existing test files are locked; new test files (e.g. for edge cases you discover) are allowed.
4. **Refactor** with tests green. Run `./dev check`.
5. **Pull request:** small, linked to its ticket, CI green, one human review.

## Writing tests

- Behaviour of agents: `tests/Twin.Scenarios.Tests`, written with the scenario kit (`tests/Twin.Testing`).
  Reference example: `tests/Twin.Scenarios.Tests/Movement/MoveToTests.cs`. Guide: `docs/testing.md`.
- Pure logic (terrain, math, random): `tests/Twin.Sim.Tests`.
- One behaviour per test. Name it as a sentence: `Unit_reaches_destination_and_becomes_idle`.
- Arrange with `Scenario.Create()...Build()`, act with `Order(...)`/`Step()`/`RunFor()`/`RunUntil()`,
  assert with `sim.Unit("id").Should()...`.
- Never loop on real time. Use `RunUntil(condition, timeout)`, which fails with a diagnostic instead of hanging.
- Missing vocabulary (a new assertion, terrain shape, builder option)? Add it to `tests/Twin.Testing`
  in the spec phase, with a doc comment, instead of writing ad-hoc helpers inside a test.

## Code conventions

- C# latest, nullable on, warnings are errors (except the Godot client). File-scoped namespaces.
- Public simulation types get XML doc comments.
- New simulation behaviour goes in an `ISimSystem` (`src/Twin.Sim/Systems`). Orders are immutable
  records in `src/Twin.Sim/Orders`, validated in `Simulation.Issue`.
- Package versions live only in `Directory.Packages.props`. Adding or upgrading a package is its own PR.

## Definition of done

- [ ] Acceptance criteria are covered by tests that a human reviewed before implementation
- [ ] `./dev check` passes locally and CI is green
- [ ] No architecture rule needed an exception (or an ADR explains it)
- [ ] Docs updated if behaviour, commands or conventions changed

## Data and AI use

Do not paste classified, personal or customer data into prompts, tests or fixtures. Use synthetic terrain
and invented units. See ADR 0008 for what may be sent to cloud AI services.

## When unsure

Ask the human rather than guessing, especially about intended behaviour. A short question is cheaper
than a wrong implementation backed by a wrong test.
