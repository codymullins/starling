---
id: "wp:M3-06g-skia-shim"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on:
  - "wp:M3-06b-native-build"
blocks:
  - "wp:M3-06h-skia-interop"
subsystem: "native"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06g-skia-shim — custom C ABI shim (long pole #2)

## Goal

Phase 2: write `native/shim/tessera_skia.{h,cpp}` — a small custom `extern "C"`
ABI (~600–1000 lines) exposing exactly what the Tessera display list needs, and
nothing more. Statically link `libskia` + Dawn into a single
`libtessera_skia.{dylib,dll,so}` per RID so .NET loads one native file with no
transitive native-dep hell. WebGPU types **never** cross into .NET — they stay
behind opaque `void*` handles. This is long pole #2.

## Inputs

- `wp:M3-06b-native-build` complete: per-RID Skia + Dawn static libs exist under
  `runtimes/<rid>/native/` (built out-of-band).
- C++ / CMake fluency; SkiaSharp's `libSkiaSharp` available **only as a
  reference** for the non-Graphite calls (it has zero Graphite coverage — do not
  extend it).

## Outputs

- `native/shim/tessera_skia.h` + `tessera_skia.cpp` — the custom `extern "C"`
  ABI. Minimal C surface:
  - context/device lifecycle (`ts_context_create`, destroy);
  - surface create + canvas;
  - the 4 `DisplayItem` ops: `fill_rect`, `stroke_rect`, `draw_text` (shaped
    glyph runs), `draw_image` (from RGBA pixels);
  - font/typeface + `shape_text` + `font_metrics`;
  - `flush_and_submit` + `read_pixels` for golden/headless readback.
- `native/shim/CMakeLists.txt` — statically links `libskia` + Dawn into a single
  `libtessera_skia.{dylib,dll,so}` per RID.
- A tiny C++ smoke harness that fills a rect and reads back a PNG.

## Acceptance

- `tessera_skia.h` exposes only the minimal surface above; WebGPU/`wgpu::` types
  appear nowhere in the header — they are opaque `void*` handles.
- The CMake build produces a single statically-linked
  `libtessera_skia.{dylib,dll,so}` per RID (no transitive native deps to ship).
- The C++ smoke harness creates a context + surface, fills a rect via
  `fill_rect`, calls `flush_and_submit` + `read_pixels`, and writes a correct
  PNG.
- The shim does **not** extend SkiaSharp's `libSkiaSharp`.
- PNG **encode** stays in C# for now (`read_pixels` → raw RGBA → existing
  encoder) — `Ssim.cs` / `PngComparison.cs` are untouched.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 2).
- The opaque-`void*` insulation is the key defense against WebGPU C-API churn —
  treat it as a hard rule, not a style preference.
- `06h-skia-interop` consumes this: the `[LibraryImport("tessera_skia")]`
  bindings mirror exactly this header.
- Dawn `Instance`/`Adapter`/`Device` creation inside `ts_context_create` is
  fleshed out in `06h` (Phase 4 wiring) — this package can stub the device path
  enough for the rect smoke test.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
