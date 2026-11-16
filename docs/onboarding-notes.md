# Onboarding notes (ticket 0001)

Points of confusion or friction found while implementing ticket 0001 (Stop order) using only the docs,
the reference feature and the harness. Each entry records the finding and, once made, the fix.

## 1. Spec-phase tests for a new order fail to compile, not for the expected reason

**Finding:** `docs/workflow.md` and `AGENTS.md` say that in the spec phase each new test must fail
"for the expected reason", while `src/` is locked. A ticket that adds a new order type (here `Stop` in
`src/Twin.Sim/Orders`) cannot create even an empty record in the spec phase, so the new tests fail with a
compile error instead of a meaningful assertion failure. A compile error also breaks the whole test project,
so the other tests cannot run. The docs do not say whether this counts as the expected reason or how to
avoid it (for example a stub type, or a small preparatory change to `src/` before the spec phase).

The `tdd-feature` skill (`.claude/skills/tdd-feature/SKILL.md`) does count "a missing type/member" as a
right reason to fail, but `docs/workflow.md` said nothing about it. See also finding 2, which shows why a
compile error alone is a weak check.

**Fix (resolved):** `docs/workflow.md`, steps 2 and 4. A compile error from a new type is an accepted
reason to fail in the spec phase. The implement phase starts by adding the type with no behaviour, so the
tests fail on an assertion.

## 2. The Stop tests passed without any Stop handling

**Finding:** after adding only the empty `Stop` record (no handling), all five `StopTests` passed. The
generic order path in `Simulation.Step` already does the work: any new order runs `CombatQueries.EndOrder`
(zero velocity, idle) and becomes the unit's current order, and no system acts on `Stop`. So the tests
went from "does not compile" straight to green and never failed on an assertion. Nothing showed that they
can catch wrong behaviour.

**Mutation check:** `CombatQueries.EndOrder(unit)` in the `default:` branch of `Simulation.Step` was
temporarily disabled and `./dev test --filter StopTests` was run. The code was then restored exactly.

| Test (criterion) | Result under mutation | Why |
|---|---|---|
| 1. `Moving_unit_ordered_to_stop_is_idle_with_zero_velocity_after_the_next_tick` | **failed** | Unit kept `Moving` and its old velocity: "Expected unit 'alpha' to be idle and stationary". |
| 2. `Stopped_unit_halts_where_it_was_and_stays_on_the_ground` | passed | It checks position only. No system moves a unit whose order is `Stop`, so the position is unchanged even though the velocity is stale. |
| 3. `Stopping_an_idle_unit_is_accepted_and_has_no_effect` | passed | The unit was already idle with zero velocity. |
| 4. `Move_order_after_stop_reaches_its_destination` | passed | `MovementSystem` sets idle and zero velocity on arrival. |
| 5. `Stop_for_unknown_unit_is_rejected` | passed | Rejection happens in `Simulation.Issue`, before the order type matters. |

So criterion 1 is guarded by a test that can fail; the other tests pass under this mutation for reasons
that are correct for them.

**Fix (resolved):** `docs/workflow.md`, step 4. If tests pass before any behaviour is added, do a mutation
check and record which tests fail.

## 3. The current order stayed `Stop` after stopping

**Finding:** after a `Stop`, `Unit.CurrentOrder` is `Stop { }` rather than `null`, and it shows in
diagnostics as "order Stop { }". The acceptance criteria do not say which is wanted, and no test checks it.
At first Oskari (human reviewer of ticket 0001) accepted this behaviour. Oskari withdrew that acceptance on
2026-11-16.

**Decision (Oskari, 2026-11-16):** `Stop` must clear the current order, so `CurrentOrder` is `null` after
stopping.

**Fix (resolved):** `Simulation.Step` handles `Stop` explicitly and clears the current order. Test
`Stopped_unit_has_no_current_order` checks it.

## 4. The `scenario-test` skill lists out-of-date assertions

**Finding:** the list of available assertions in `.claude/skills/scenario-test/SKILL.md` does not include
`BeSuppressed()`, `HaveHealth(health)`, `BeIn(posture)` and `HaveNoOrder()`. An agent that uses the skill
does not know about them, or must read `tests/Twin.Testing/UnitAssertions.cs` to find them.

**Fix:** open. The skill is part of the AI harness, which only a human changes. Fix it in a separate PR.
