---
id: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "available"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on: []
blocks: []
subsystem: "build"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/02_PROJECT_SETUP.md#ci"
  - "browser-plan/03_NETWORKING.md#tls-approach"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/12_TESTING.md#interop-seam-policy-test"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06-native-interop-pivot — adopt Skia Graphite, ANGLE, OS-native codecs, system TLS

## Goal

Umbrella package for the largest single effort in the project to date: abandon
"Rule 0" (pure-managed, no `DllImport`) and pivot the bottom graphics/codec/TLS
layer to native libraries — **Skia Graphite** (Dawn/WebGPU GPU rasterizer),
**ANGLE** (GL fallback), **OS-native image codecs** (ImageIO/WIC/libjpeg-png-webp),
and **`SslStream`** (OS TLS) replacing BouncyCastle. "Rule 0" becomes the
**interop seam policy**: native `LibraryImport` is confined to two vetted
projects — `Tessera.Skia` and `Tessera.Codecs` — and every other engine project
stays P/Invoke-free. This parent tracks the 12 children below; it is "done" when
all 12 are `complete`, the dual-backend flag has flipped, `ImageSharpBackend.cs`
is deleted, and `dotnet build && dotnet test` is green on win/mac/linux under
the repurposed interop-policy lint job.

## Inputs

- M0–M2 complete: static HTML/CSS render to PNG + HTTP/1.1 networking.
- The approved master plan (see Notes) and `tasks/SCHEMA.md`.
- The existing `tasks/` git-based claim/branch/handoff queue (`tasks/lib/claim.sh`).

## Outputs

This package itself authors no engine code — it decomposes the pivot into 12
child work packages under `tasks/M3/`:

| Child | Phase | Scope |
|---|---|---|
| `wp:M3-06a-native-scaffold` | 0 | `third_party/REVISIONS.md`, `native/`+`runtimes/` dirs, pin Skia/Dawn/ANGLE revisions |
| `wp:M3-06b-native-build` | 1 | GN/Ninja Skia+Graphite+Dawn build osx-arm64→win→linux, `native.yml` |
| `wp:M3-06c-decoded-image` | 8 (seam) | `DecodedImage` type in `Tessera.Common`, thread through resolver/display-list/fetcher |
| `wp:M3-06d-codecs` | 8 (project) | `Tessera.Codecs` interop project + ImageIO/WIC/Linux decoders + tests |
| `wp:M3-06e-sslstream-tls` | 9 | `SslStreamTlsTransport`, rewrite cert verification, delete BouncyCastle |
| `wp:M3-06f-docs-policy` | 10 (docs) | Rewrite README/AGENTS/`browser-plan/*` for the interop seam policy |
| `wp:M3-06g-skia-shim` | 2 | `native/shim/tessera_skia.{h,cpp}` + CMake, static-link, C++ smoke test |
| `wp:M3-06h-skia-interop` | 3 + 4 | `src/Tessera.Skia` project, `LibraryImport` bindings, SafeHandles, Dawn/Graphite wiring |
| `wp:M3-06i-skia-backend` | 5 | `SkiaGraphiteBackend`, `RenderedBitmap`, rewire Painter/Engine/Headless behind a flag |
| `wp:M3-06j-skia-fonts` | 6 | `FontResolver` rewrite, `SkiaTextMeasurer`, re-vendor `testdata/golden/` |
| `wp:M3-06k-gui-canvas` | 7 | `SkiaCanvasView` + handler, rewrite `MainPage` hit-testing, delete `BoxTreeRenderer` |
| `wp:M3-06l-ci-policy` | 10 (CI) | Repurpose `lint` job, native-artifact restore, Linux codec libs, `12_TESTING.md` rewrites |

## Acceptance

- All 12 child packages reach `complete`.
- The dependency DAG below is respected; Wave 1 packages land independently
  mergeable to `main`; the Skia track stays behind the dual-backend flag until
  `06k-gui-canvas`.
- Final integration: the dual-backend flag flips, `ImageSharpBackend.cs` +
  `BoxTreeRenderer.cs` + the three BouncyCastle TLS files are deleted, all
  `packages.lock.json` regenerated, per-platform SSIM thresholds retuned.
- `dotnet build && dotnet test` green on win/mac/linux; repurposed
  interop-policy lint job passes; `grep -rn BouncyCastle src/` is empty.
- `tasks/INDEX.md` reflects the closed M3-06 section.

## Notes

- **Master plan** lives outside the repo at
  `~/.claude/plans/make-a-plan-to-serialized-boole.md` — the source of truth for
  every child's Goal/Inputs/Outputs/Acceptance.
- **Parallel execution plan / waves:**
  - **Wave 1 — start immediately, fully parallel (up to 6 agents):**
    `06a-native-scaffold`, `06b-native-build` (long pole #1, deps 06a),
    `06c-decoded-image`, `06d-codecs` (deps 06c), `06e-sslstream-tls`,
    `06f-docs-policy`. Of these, `06a`, `06c`, `06e`, `06f` have zero deps and
    are claimable on day one.
  - **Wave 2 — gated on the native track:** `06g-skia-shim` (deps 06b),
    `06h-skia-interop` (deps 06g), `06i-skia-backend` (deps 06h + 06c),
    `06j-skia-fonts` (deps 06i), `06k-gui-canvas` (deps 06j),
    `06l-ci-policy` (deps 06d + 06e + 06h).
  - **Critical path:** `06a → 06b → 06g → 06h → 06i → 06j → 06k`. The native
    build (2–4 weeks alone) dominates — start it on day one.
- **File-contention hotspots:** `DisplayItem.cs` is touched by both
  `06c-decoded-image` and `06i-skia-backend` — land `06c` first.
  `Tessera.sln`, `Directory.Packages.props`, and `tasks/INDEX.md` are
  merge-conflict hotspots — note in every handoff log.
- **Big-bang delivery, staged work:** Wave 1 packages do not break the running
  engine (ImageSharp stays working; TLS swap is behind `ITlsTransport`). The
  Skia track integrates on a long-lived branch behind the dual-backend flag.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
