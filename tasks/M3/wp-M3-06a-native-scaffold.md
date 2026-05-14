---
id: "wp:M3-06a-native-scaffold"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-native"
claimed_at: "2026-05-14T14:43:49Z"
branch: "main"
depends_on: []
blocks:
  - "wp:M3-06b-native-build"
subsystem: "build"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/02_PROJECT_SETUP.md#repo-hygiene"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06a-native-scaffold — pin revisions and lay out native dirs

## Goal

Phase 0 of the pivot: pure scaffolding, no engine code. Hard-pin the Skia, Dawn,
and ANGLE revisions as a locked manifest, create the new top-level directory
layout the native build and interop will consume, and document the artifact
strategy (native binaries built out-of-band, never committed to git, never built
in PR CI). This is small and fast but it gates the entire native track — nothing
downstream can start until the revisions are pinned.

## Inputs

- No code dependencies — this is the first package in the critical path.
- Knowledge of the current repo top-level layout (`src/`, `tests/`,
  `browser-plan/`, `tasks/`, `testdata/`, `.github/`).

## Outputs

- `third_party/REVISIONS.md` — hard-pins a Skia milestone branch revision
  (Chrome-stable-aligned, e.g. `chrome/m1xx`, **not** `main`), plus the exact
  Dawn and ANGLE revisions Skia's `DEPS` pulls at that Skia revision. Treated as
  a locked manifest; a hash of this file keys the native build cache.
- New top-level directories:
  - `native/` — C++ build scripts + the C ABI shim (committed).
  - `runtimes/` — RID-laid-out native output (`runtimes/<rid>/native/`);
    **gitignored**.
  - `third_party/skia/` — the fetched Skia checkout; **gitignored**.
- `.gitignore` updates for `runtimes/` and `third_party/skia/`.
- A short `native/README.md` (or section in `third_party/REVISIONS.md`)
  documenting the artifact strategy: native binaries are built out-of-band by a
  dedicated pipeline, consumed as a versioned internal package / release
  artifact, **not** built in PR CI (Skia builds are 20–40 min), **not**
  committed to git.

## Acceptance

- `third_party/REVISIONS.md` exists and pins concrete, non-`main` revisions for
  Skia, Dawn, and ANGLE with the rationale (Chrome-stable alignment) recorded.
- `native/`, `runtimes/`, `third_party/skia/` exist; `git status` shows
  `runtimes/` and `third_party/skia/` are ignored (a touched file inside each is
  not listed).
- The artifact strategy is documented in-repo.
- No engine code or csproj changes in this package.
- `dotnet build` is unaffected (no `.sln` change).

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 0).
- Pick a Skia milestone branch that has Graphite + Dawn support landed; record
  the exact `DEPS`-resolved Dawn/ANGLE commit hashes, not floating refs — the
  whole point is reproducible builds.
- `Tessera.sln`, `Directory.Packages.props`, and `tasks/INDEX.md` are
  merge-conflict hotspots across the M3-06 fleet — note any touch in the handoff
  log.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T14:43:49Z — claimed by agent-claude-cody-native; working in an isolated worktree branch alongside wp:M3-06b.
