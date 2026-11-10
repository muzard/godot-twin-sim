# 0010. Cliffs block movement unless the unit can rappel

- Status: Accepted
- Date: 2026-11-08

## Context
An agent should not plan a route down off a ledge. The 2 m elevation model (KM2) smooths many cliffs: around the
Topographic Database cliff lines in the Smoke test I areas, roughly 13–28 % of the cliff vertices have no 2 m slope over the 0.8 limit nearby,
so the slope rule alone lets routes run over them.

## Decision
- Cliffs come from the MML Topographic Database layer `jyrkanne`. Preprocessing writes them as polylines in local
  meters to `world/jyrkanteet.json`, listed in `manifest.json` under `cliffs` with its sha256.
- A route may not cross a cliff line in either direction: neither down off the ledge nor up onto it.
- The exception is a unit that can rappel (`MovementProfile.CanRappel`, `agent.canRappel` in `scenario.json`). It may
  cross cliff lines both ways, whatever the slope there.
- A rappelled arc costs as much as walking the steepest slope the unit could walk (the slope factor at the limit).
  This placeholder cost is to be calibrated.
- A cliff is impassable terrain, not a hard boundary. There is nothing to ask permission for, and the reference route
  cannot cross it either.

Reason: the Topographic Database marks the cliffs that matter for movement, and the 2 m model cannot be trusted to
show them. This is a stand-in until more precise data (for example laser-scanning point clouds or a finer elevation
model) lets the slope rule find ledges by itself; then this decision can be replaced.

## Alternatives considered
- A height-drop threshold on the elevation model: misses the cliffs the 2 m model smooths, which is the problem.
- Blocking only the way down: units that cannot rappel cannot climb a cliff either.

## Consequences
- `IWorldQuery.Cliffs`; the pathfinder and the agent check arcs and the legs to and from cell centers against cliffs.
- Tests: `tests/Twin.Scenarios.Tests/Movement/CliffTests.cs`, `CliffFilesTests`, `CliffAcceptanceTests`, `prep/tests/test_cliffs.py`.
- The rappelling cost is a placeholder to calibrate.
