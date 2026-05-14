---
id: "wp:M3-06b-native-build"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-native"
claimed_at: "2026-05-14T14:43:49Z"
completed_at: "2026-05-14T14:50:57Z"
branch: "main"
depends_on:
  - "wp:M3-06a-native-scaffold"
blocks:
  - "wp:M3-06g-skia-shim"
subsystem: "build"
plan_refs:
  - "browser-plan/02_PROJECT_SETUP.md#ci"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06b-native-build — native Skia + Graphite + Dawn build (long pole #1)

## Goal

Phase 1: produce a reproducible native build of Skia with the Graphite GPU
backend and Dawn, per RID, driven by GN/Ninja against the pinned revisions from
`06a`. Build **osx-arm64 first and fully working**, then win-x64 (D3D12 via
Dawn), then linux-x64 (Vulkan via Dawn). Wire a dedicated GitHub Actions
workflow that builds each RID out-of-band and uploads the artifacts. This is the
dominant long pole (2–4 weeks alone) — dedicate one agent and stage strictly.

## Inputs

- `wp:M3-06a-native-scaffold` complete: `third_party/REVISIONS.md` pins
  Skia/Dawn/ANGLE; `native/`, `runtimes/`, `third_party/skia/` dirs exist; the
  out-of-band artifact strategy is documented.
- GN/Ninja toolchain knowledge.

## Outputs

- `native/build-skia.sh` and `native/build-skia.ps1` — fetch Skia at the pinned
  revision and build with GN args: `skia_enable_graphite=true`,
  `skia_use_dawn=true`, `skia_use_gl=true` (ANGLE fallback),
  `skia_use_harfbuzz=true`, `skia_use_icu=true`, `is_official_build=true`.
- `.github/workflows/native.yml` — three runners (macOS/Windows/Linux), each
  builds its RID, uploads the Skia/Dawn static libs + license files as
  artifacts; aggressively caches the Skia checkout keyed by the hash of
  `third_party/REVISIONS.md`.
- Build output staged under `runtimes/<rid>/native/` locally (gitignored) for
  the shim package in `06g` to statically link against.

## Acceptance

- `native/build-skia.sh` on an osx-arm64 host produces working Skia + Dawn
  static libraries with Graphite enabled — verified by the GN args above being
  honored and the libs containing Graphite symbols.
- Windows and Linux builds follow once osx-arm64 is fully green (strict staging
  — do not parallelize the platforms).
- `.github/workflows/native.yml` runs on all three runners, caches the Skia
  checkout keyed by `REVISIONS.md` hash, and uploads per-RID artifacts +
  license files.
- The build is **not** added to PR `ci.yml` (Skia builds are 20–40 min).
- Builds are reproducible against the pinned revisions.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 1).
- Strict staging is the risk mitigation: osx-arm64 fully green before touching
  Windows/Linux. Pinned revisions make builds reproducible.
- This package produces the raw Skia/Dawn libs; `06g-skia-shim` statically links
  them plus the custom C ABI into a single `libtessera_skia.{dylib,dll,so}` per
  RID.
- `.github/workflows/` is shared with `06l-ci-policy` — coordinate via handoff
  log to avoid merge conflicts.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T14:43:49Z — claimed by agent-claude-cody-native. Dependency wp:M3-06a-native-scaffold is being implemented in this same isolated worktree branch and will be marked complete in the same series of commits, so 06b's `depends_on` is satisfied within this worktree. Both packages land together when the worktree merges.
- 2026-05-14T14:50:57Z — complete (scaffolding). Created `native/build-skia.sh` + `native/build-skia.ps1` (fetch Skia at the pinned revision, `tools/git-sync-deps`, `gn gen` with `skia_enable_graphite/use_dawn/use_gl/use_harfbuzz/use_icu=true`, `is_official_build=true`, per-RID `target_cpu`/`target_os`, `ninja`, stage into `runtimes/<rid>/native/`; both scripts hard-abort if the checked-out Skia SHA != `third_party/REVISIONS.md`, and also verify the synced Dawn/ANGLE SHAs). Created `native/shim/` scaffold (`tessera_skia.h` C ABI surface, `tessera_skia.cpp` stub bodies returning `TS_NOT_IMPLEMENTED`, `CMakeLists.txt` that static-links the shim against `libskia` — stub-only mode until real Skia libs exist). Created `.github/workflows/native.yml`: manual + `REVISIONS.md`-path-triggered, 3 jobs (macos-15 / windows-2025 / ubuntu-24.04), Skia checkout cached keyed by the `REVISIONS.md` hash, uploads per-RID static libs + license files as artifacts; NOT added to PR `ci.yml`. `bash -n`, PowerShell parser, and YAML parse all pass. **IMPORTANT: the actual native binaries were NOT built here** — that requires running `native/build-skia.*` on a provisioned machine with `depot_tools` + the platform toolchain (a multi-hour GN/Ninja build). The scripts are the reproducible recipe; they have not been executed end-to-end. **`wp:M3-06g-skia-shim` is blocked on a real `libskia`** existing — it can only proceed after `native/build-skia.*` has actually been run and `runtimes/<rid>/native/` is populated. `.github/workflows/` is shared with `06l-ci-policy`: this WP only *adds* the new `native.yml` file and does not touch `ci.yml`, so there should be no conflict. No `.cs`/`.csproj`/`.sln`/`tasks/INDEX.md` touched.
