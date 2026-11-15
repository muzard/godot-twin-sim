# 0011. Shared AI harness as a git submodule

- Status: Proposed
- Date: 2026-11-15

## Context
The team now keeps one shared repository, `pvarki/matrix-docs` ("the harness"), for material that is not
specific to one codebase: agent skills, best practices, research findings, and architecture and design
documents. This repository has its own project-specific harness (ADR 0008): `AGENTS.md`, hooks, the TDD
phase, two skills and the ADRs. People and agents working here need to read the shared material, and we do
not want copies of it drifting apart in each project.

## Decision
1. The harness is mounted as a git submodule at `harness/`, pinned to a commit. Moving the pin is an
   ordinary pull request (`chore: bump harness`), so a change in the harness reaches this repository only
   after review here.
2. The submodule is **optional**. The harness is not public yet (the plan is to open-source it), so
   for now only team members with access can fetch it, while this repository is public. The build, the
   tests, the hooks and CI therefore never read `harness/`, and CI does not fetch it. A clone without the
   submodule works fully, and a change in the harness cannot break a build.
3. Division of responsibility:
   - **Here:** rules that are enforced or specific to this code: `AGENTS.md`, hooks and the TDD phase,
     the `tdd-feature` and `scenario-test` skills, ADRs about the simulation, the backlog.
   - **In the harness:** everything meant for more than one project: general skills and best practices,
     research findings, cross-project architecture and design.
4. Precedence: where the two disagree, this repository wins (`AGENTS.md` and `docs/adr/`). The harness
   informs; it does not override an enforced rule. A harness practice we want enforced here is adopted by
   a pull request to `AGENTS.md`, with an ADR if it is a significant decision.
5. `harness/` is read-only from this repository. Changes to shared material are made in `pvarki/matrix-docs`
   through its own review, then picked up here by moving the pin.
6. While the harness is private, its content is not copied or quoted at length into this public
   repository. Nothing contributed to the harness from this project may break the data rules of ADR 0008.

## Alternatives considered
- **Claude Code plugin marketplace.** Native distribution for skills, but Claude Code only, does not put the
  documents where other tools can read them, and needs a change to `.claude/settings.json`.
- **Copying files in with a sync script.** No submodule friction, but the copies are editable in place and
  drift from the source.
- **Links only.** No pinning, and agents without network access to the harness cannot follow them.

## Consequences
- One extra step after cloning, for those with access: `git submodule update --init harness`.
- Until the harness is public, `.gitmodules` shows its name to everyone, and
  `git clone --recurse-submodules` fails for people without access (a plain clone works).
- When the harness is published, update the wording here, in `AGENTS.md` and in `README.md`.
- Agents see the shared material as plain files, whichever tool is used (ADR 0008, point 4).
- Skills in `harness/` are not discovered automatically by Claude Code, which only looks in
  `.claude/skills/`. They are read as documents on request. If we want them as first-class skills, that is
  a separate decision (a sync step or a plugin), made by a human because it touches the harness.
- The pin goes stale unless someone moves it. Bump it when a harness change matters to this project.
