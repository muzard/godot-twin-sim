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

**Fix:** not yet decided. Ask the team.
