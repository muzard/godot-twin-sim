# Development workflow

This is the agreed way of working. It applies equally to code written by people and by AI agents.
Rules and commands are summarised in [AGENTS.md](../AGENTS.md); this document explains the process.

## Why test-driven, and why two phases

Working with LLMs makes writing code cheap and checking it expensive. A test written *before* the code,
and reviewed by a person, is the cheapest reliable check: it states what "done" means in a form a
machine can verify. It also gives an agent a clear, objective target and a fast feedback loop.

The known failure mode is an agent that "makes the tests pass" by changing the tests. So the work is split
into two phases, and a human gate sits between them:

```
ticket ──> SPEC phase ──> human reviews tests ──> IMPLEMENT phase ──> refactor ──> PR + CI + review ──> merge
           (tests only,     (the tests are         (src/ only, existing
            src/ locked)     the specification)     tests locked)
```

The phase is a local setting (`./dev phase spec|implement`), set only by a person. In Claude Code, hooks
enforce it; with other tools it is a convention. In every case CI and code review are the final gate.

## Step by step

1. **Pick a ticket** with clear acceptance criteria. If criteria are vague, clarify them first.
   Create a branch: `feature/<ticket-id>-short-name`.
2. **Spec:** `./dev phase spec`. Write scenario tests, one per criterion (by hand or with an agent using
   the `tdd-feature` skill). Run `./dev test`; each new test must fail for the expected reason.
3. **Review the tests.** Read them as a specification, not as code: does each test describe the behaviour
   the ticket asks for? Are tolerances and timeouts sensible? Are invalid inputs covered? Commit the tests
   on their own (`test: ...`) so the review history shows them separately.
4. **Implement:** `./dev phase implement`. Make the tests pass with the simplest change in `src/`.
5. **Refactor** while green, then `./dev check`.
6. **Pull request:** small and focused, linked to the ticket, template filled in. CI must be green and one
   person must approve. Reviewers look at tests first, then implementation.

## Using AI coding agents

- Claude Code is our primary harness (ADR 0008). It reads `CLAUDE.md`, which imports `AGENTS.md`, and
  runs the hooks in `.claude/settings.json`. Other tools that read `AGENTS.md` work too, without hooks.
- Shared skills, best practices, research and cross-project design live in the team's harness repository,
  mounted at `harness/` (ADR 0011). Fetch it with `git submodule update --init harness`; update the pin
  with `git submodule update --remote harness` and a `chore: bump harness` pull request. This
  repository's own rules win where the two disagree.
- The person who opens a pull request owns every line in it, however it was written.
- Keep agent sessions scoped to one ticket. Start a new session for a new ticket.
- If an agent is stuck (the stop hook gives up after three red attempts), take over: the problem is
  usually an unclear test or a missing concept, not a missing line of code.
- Data rules: synthetic data only in prompts, tests and fixtures (ADR 0008).

## Commit and branch conventions

- Branches: `feature/…`, `fix/…`, `chore/…`. Main is protected: merge via pull request only.
- Commit messages: `type: summary` with types `feat`, `fix`, `test`, `refactor`, `docs`, `chore`.
- One package upgrade per pull request.

## Changing the rules

Rules change through a pull request that updates `AGENTS.md` and adds or amends an ADR. Anyone can propose;
the team agrees in review. This keeps the harness, the docs and actual practice in sync.
