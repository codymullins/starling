---
id: "wp:M3-06h-skia-interop"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-skia-net"
claimed_at: "2026-05-14T16:41:43Z"
branch: "main"
depends_on:
  - "wp:M3-06g-skia-shim"
blocks:
  - "wp:M3-06i-skia-backend"
  - "wp:M3-06l-ci-policy"
subsystem: "Tessera.Skia"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/12_TESTING.md#interop-seam-policy-test"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06h-skia-interop — `src/Tessera.Skia` interop project + Dawn/Graphite wiring

## Goal

Phases 3 + 4: create `src/Tessera.Skia` — the primary vetted interop project,
the only engine project allowed `LibraryImport` — with source-generated bindings
to the `tessera_skia` shim, `SafeHandle` wrappers for deterministic native
cleanup, RID-specific native packaging, and the Dawn/Graphite device wiring
inside `ts_context_create` (Dawn `Instance → Adapter → Device` →
`skgpu::graphite::ContextFactory::MakeDawn`). ANGLE is the GL fallback only — do
not over-invest in v1.

## Inputs

- `wp:M3-06g-skia-shim` complete: `libtessera_skia.{dylib,dll,so}` per RID with
  the minimal C ABI; the `tessera_skia.h` header is the binding contract.
- .NET source-generated interop (`LibraryImport`) + `SafeHandle` knowledge.

## Outputs

- `src/Tessera.Skia/Tessera.Skia.csproj` — new project; references
  `Tessera.Common` only; added to `Tessera.sln`. The only engine project allowed
  `LibraryImport`.
- `src/Tessera.Skia/Interop/NativeMethods.cs` — source-generated
  `[LibraryImport("tessera_skia")]` partial methods mirroring `tessera_skia.h`.
- `SkContext` / `SkSurface` / `SkCanvas` / `SkFont` / `SkTypeface` / `SkImage` —
  `SafeHandle` wrappers for deterministic native cleanup.
- Native packaging: RID-specific `runtimes/<rid>/native/` copy via the csproj;
  `NativeLibrary.SetDllImportResolver` in a module initializer as the Mac
  Catalyst `.app`-bundle fallback.
- Dawn/Graphite wiring inside `ts_context_create` (shim side, finalized here):
  Dawn `Instance → Adapter → Device`, handed to
  `skgpu::graphite::ContextFactory::MakeDawn(...)`; store `Context` + `Recorder`;
  `TsBackendHint` override for debugging.
- `tests/Tessera.Skia.Tests/` — interop smoke test: create a context/surface,
  draw each `DisplayItem` kind, read pixels back.

## Acceptance

- `Tessera.Skia` builds, references only `Tessera.Common`, is in `Tessera.sln`.
- The interop smoke test creates a context + surface, draws every `DisplayItem`
  kind (`fill_rect`, `stroke_rect`, `draw_text`, `draw_image`), and reads back
  correct pixels.
- All native handles are `SafeHandle`-wrapped; no leaked native resources under
  the test run.
- Dawn auto-selects Metal/D3D12/Vulkan per platform; `TsBackendHint` can
  override.
- The native package restores before `dotnet build` (CI restore step is `06l`);
  the Mac Catalyst `.app` layout resolves the native lib via the
  `SetDllImportResolver` fallback.
- The interop-policy lint job tolerates `LibraryImport` in `Tessera.Skia`.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md`
  (Phases 3 + 4).
- ANGLE is the **fallback** GL provider only — minimal v1 investment.
- `06i-skia-backend` builds the `SkiaGraphiteBackend` on top of these handles;
  `06l-ci-policy` adds the native-package restore to `ci.yml`.
- `Tessera.sln` is a merge-conflict hotspot — note the touch in the handoff log.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
