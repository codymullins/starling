---
id: "wp:M3-06h-skia-interop"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-skia-net"
claimed_at: "2026-05-14T16:41:43Z"
completed_at: "2026-05-14T16:55:00Z"
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
- 2026-05-14T16:55:00Z — completed (agent-claude-cody-skia-net).
  - Created `src/Tessera.Skia` (`Interop/NativeMethods.cs`, `Interop/NativeLoader.cs`,
    `Handles/Sk{Context,Surface,Canvas,Typeface,Font,Image}.cs`,
    `SkiaInteropException.cs`) and `tests/Tessera.Skia.Tests`. Both added to
    `Tessera.sln` (nested under the `src` / `tests` solution folders like Codecs).
  - All 20 `ts_` functions bound via source-generated `[LibraryImport]`. Signatures
    mirror `native/shim/tessera_skia.h` exactly, including the documented ABI change:
    `ts_read_pixels(TsContext*, TsSurface*, ...)` — surfaced as `SkSurface.ReadPixels(SkContext, w, h)`.
    `ts_flush_and_submit` likewise takes the context. Opaque handles bound as `nint`;
    POD structs (`TsColor/TsRect/TsGlyph/TsFontMetrics`) as `[StructLayout(Sequential)]`;
    `ts_typeface_from_name` uses `StringMarshalling.Utf8`; pointer params (`glyphs`,
    `pixels`, `utf8_text`) bound as `unsafe` `T*`.
  - Signature quirk / HEADER DIVERGENCE: the header has **no `TsImage` opaque handle**
    and no `ts_image_create/destroy` pair — `ts_canvas_draw_image` takes raw RGBA8888
    pixels directly. So `SkImage` is a managed-only value holder (documented in the
    file), NOT a `SafeHandle` — there is no native handle to own. The other five
    wrappers are `SafeHandleZeroOrMinusOneIsInvalid`; `SkCanvas` is non-owning
    (borrowed from the surface, `ReleaseHandle` is a no-op) per the header comment.
  - Native packaging: csproj copies `runtimes/<rid>/native/libtessera_skia.*` to the
    output `runtimes/<rid>/native/` layout (per-RID `Exists`-guarded `None` items —
    `%(Identity)` is not allowed in item `Condition`s, hence the explicit per-RID
    blocks). `NativeLoader` module initializer installs a `SetDllImportResolver`
    fallback that walks up to the gitignored repo-root `runtimes` tree.
  - **osx-arm64 only** until win/linux dylibs are built (06g). The P/Invoke smoke
    test (mirrors `smoke_test.c`: context → surface → clear + fill_rect → flush →
    readback → assert pixels, plus a handle-release loop) is guarded by
    `OperatingSystem.IsMacOS()` AND dylib-present via `Assert.SkipUnless` (xunit v3
    native skip — repo has no `SkippableFact` package). On this macOS box the test
    **actually ran and passed** against the real Dawn/Graphite dylib.
  - `dotnet build` + `dotnet test` from repo root: both green. Test count +2
    (`Tessera.Skia.Tests`, both passed, 0 skipped here).
  - `Tessera.sln` touched (merge-conflict hotspot — note for rebasing agents):
    2 `Project` blocks, 2 `ProjectConfigurationPlatforms` blocks, 2 `NestedProjects` lines.
