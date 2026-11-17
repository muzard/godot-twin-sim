# 0012. Shared team knowledge through a plugin and a personal clone

- Status: Accepted
- Date: 2026-11-17
- Supersedes: [0011](0011-shared-harness-submodule.md)

## Context
ADR 0011 mounted the team's shared repository, `pvarki/matrix-docs`, as a git submodule at `harness/`,
pinned to a commit. (This ADR says `matrix-docs` for that repository, so it is not confused with this
repository's own AI harness of ADR 0008: `AGENTS.md`, hooks and the TDD phase.)

Since then `matrix-docs` has defined how it is meant to be used: a Claude Code plugin (`matrix`) whose skills
read and write each person's own clone of `matrix-docs`. The skills find that clone through the
`MATRIX_DOCS` environment variable or next to the repository being worked on, and pull it before reading.
They do not look in `harness/`.

That leaves the submodule with no reader and several costs:
1. **Nothing uses it.** The build, tests, hooks and CI never read it (ADR 0011, point 2), and the skills of
   `matrix-docs` read a clone, not `harness/`.
2. **It is the wrong place to write.** The skills record findings on a branch in the clone and open a pull
   request in `matrix-docs`. In `harness/` that is against ADR 0011 point 5 (read-only from here), and every
   such write would show up in this repository as `modified: harness (new commits)`.
3. **Two copies of the same knowledge.** A person with both a pinned `harness/` and a clone has two
   versions, and an agent can read the old one without anyone noticing.
4. **The pin goes stale** unless someone moves it in a pull request.
5. **It is empty for most readers.** This repository is public and `matrix-docs` is private. CI and cloud
   sessions do not fetch it, and `git clone --recurse-submodules` fails for everyone without access.

What the submodule does give is a review gate: shared material reaches this repository only after a pull
request here moves the pin (ADR 0011, point 1). No rule here depends on a specific version of
`matrix-docs`, and the skills ignore the pin anyway, so in practice the gate protects nothing today.

## Decision
1. Remove the `harness/` submodule and `.gitmodules`.
2. Each person who uses `matrix-docs` keeps their own clone, preferably next to this repository
   (e.g. `~/code/godot-twin-sim` and `~/code/matrix-docs`). Setting `MATRIX_DOCS` to the clone's path is
   recommended: it also works from git worktrees, where "next to the repository" points somewhere else.
3. Claude Code users install the `matrix` plugin at **user scope** (per machine, not per branch or
   repository; nothing in this repository changes). Local scope (`.claude/settings.local.json`, not
   committed) also works for someone who wants it in this repository only. Other agents link the skills
   from the clone, as the `matrix-docs` README describes.
4. The plugin is **not** enabled in `.claude/settings.json` while `matrix-docs` is private, because this
   repository is public and outside contributors could not reach it.
5. These rules of ADR 0011 stay in force:
   - **Division of responsibility.** Here: rules that are enforced or specific to this code (`AGENTS.md`,
     hooks and the TDD phase, the `tdd-feature` and `scenario-test` skills, ADRs about the simulation, the
     backlog). In `matrix-docs`: everything meant for more than one project.
   - **Precedence.** Where the two disagree, this repository (`AGENTS.md`, `docs/adr/`) wins. `matrix-docs`
     informs; it does not override an enforced rule. A practice we want enforced here is adopted by a pull
     request to `AGENTS.md`, with an ADR if it is significant.
   - **Changes to shared material** are made in `matrix-docs` through its own review.
   - **Data.** While `matrix-docs` is private, its content is not copied into this public repository.
     Nothing contributed to `matrix-docs` from this project may break the data rules of ADR 0008.

## Alternatives considered
- **Keep the submodule (ADR 0011).** Keeps the review gate of the pin, but nothing reads the pinned copy:
  the skills read a clone. Its costs remain while the gate protects nothing.
- **Keep the submodule and point `MATRIX_DOCS` at it.** One copy and the skills would use it, but writing
  findings into it breaks ADR 0011 point 5 and dirties this repository, and pulling it bypasses the pin.
- **Enable the plugin for the whole project now** (`extraKnownMarketplaces` and `enabledPlugins` in
  `.claude/settings.json`). Everyone would get the skills automatically, but the public repository would
  point to a private marketplace. Each person would still need access and a clone. Revisit when
  `matrix-docs` is public.
- **Copy the files in with a sync script.** Copies drift from the source, and copying private content into
  this public repository is not allowed.

## Consequences
- One setup step per person and machine (clone and plugin) instead of `git submodule update --init harness`
  per clone of this repository. A plain clone of this repository has nothing extra to fetch.
- **The review gate is given up.** Agents read `matrix-docs` live: whatever branch the clone has checked out,
  after a fast-forward pull. Keep the clone on `main`. The safeguards are the review in `matrix-docs` and the
  precedence rule above. If a rule here ever has to depend on specific shared content, adopt it into
  `AGENTS.md` or an ADR instead of pinning.
- Existing clones that ran `git submodule update --init harness` keep a copy of `matrix-docs`, including its
  history, after this change. Clones that never fetched it need nothing. To clean up:
  - before pulling this change: `git submodule deinit -f harness && rm -rf .git/modules/harness`
  - after pulling it: `rm -rf harness .git/modules/harness && git config --remove-section submodule.harness`
  - Windows (PowerShell), after pulling: `Remove-Item -Recurse -Force harness, .git/modules/harness` then
    `git config --remove-section submodule.harness`
- `.dockerignore` keeps `harness/` for now, so a leftover copy is not sent into a local Docker image.
- Claude Code in the cloud (claude.ai/code) has neither the plugin nor a clone unless its environment is
  given access to `matrix-docs`. This was already true of the private submodule.
- When `matrix-docs` is published, decide in a separate pull request whether to enable the plugin for the
  whole project (point 4), and update this ADR, `AGENTS.md` and `README.md`.
