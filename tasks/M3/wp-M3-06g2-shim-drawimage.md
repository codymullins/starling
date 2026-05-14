---
id: "wp:M3-06g2-shim-drawimage"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-shim2"
claimed_at: "2026-05-14T17:26:22Z"
completed_at: "2026-05-14T17:42:00Z"
branch: "main"
depends_on:
  - "wp:M3-06g-skia-shim"
  - "wp:M3-06j-skia-fonts"
blocks: []
subsystem: "native"
plan_refs:
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
---

# wp:M3-06g2-shim-drawimage — fix ts_canvas_draw_image on Graphite canvases

## Goal

Follow-up to `wp:M3-06g`. The shim's `ts_canvas_draw_image`
(`native/shim/tessera_skia.cpp`, ~line 362) builds a raster `SkImage` via
`SkImages::RasterFromPixmapCopy` and calls `drawImageRect` — which is a **no-op
on a Graphite (GPU) canvas**: Graphite canvases only draw texture-backed
images. `wp:M3-06j` discovered this when it flipped the default backend to
Skia, and worked around it by pinning the three image-pixel tests in
`tests/Tessera.Paint.Tests` to the ImageSharp backend plus a skipped repro
test. This WP fixes the shim so images actually blit on the Skia backend, then
removes that workaround.

## Inputs

- `wp:M3-06g` complete: the shim + `native/shim/CMakeLists.txt` build
  `runtimes/osx-arm64/native/libtessera_skia.dylib`.
- `wp:M3-06j` complete: it left the workaround + a skipped `draw_image` repro
  smoke test in `tests/Tessera.Paint.Tests`.
- The native Skia checkout at `third_party/skia/` (headers — ground truth for
  the current Graphite image API).

## Outputs

- `native/shim/tessera_skia.cpp` — `ts_canvas_draw_image` uploads the raw RGBA
  pixels as a Graphite **texture-backed** `SkImage` (e.g.
  `SkImages::TextureFromImage(recorder, rasterImage, ...)` or the current
  Graphite equivalent — read the headers) before `drawImageRect`. The shim's
  `Recorder` is reachable from the context; thread it to the draw call if the
  `TsCanvas` handle doesn't already carry it.
- Rebuilt `runtimes/osx-arm64/native/libtessera_skia.dylib` (gitignored — not
  committed).
- `native/shim/smoke_test.c` — extend (or a sibling) to draw an image and read
  back the blitted pixels, asserting they match the source.
- `tests/Tessera.Paint.Tests/*` — revert the `wp:M3-06j` workaround: un-pin the
  three image-pixel tests from ImageSharp, un-skip the `draw_image` repro test,
  so they exercise the Skia backend and pass.

## Acceptance

- A `fill image → flush → read_pixels` round-trip through the shim returns the
  source image's pixels (C smoke test).
- The three `Tessera.Paint.Tests` image tests pass on the **default (Skia)**
  backend with the workaround removed.
- `dotnet build` + `dotnet test` green from the repo root.
- `native/shim/CMakeLists.txt` link line unchanged (or, if a new lib is needed,
  documented in the handoff log).

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 2 /
  Phase 5 — the `DrawImage` display item).
- Graphite image upload API moved across Skia milestones — trust the headers in
  `third_party/skia/include/`, not API memory.
- osx-arm64 only until the win/linux native builds exist.

## Handoff log

- 2026-05-14T17:30:00Z — created (agent-claude-cody) as a follow-up after
  wp:M3-06j surfaced the no-op `ts_canvas_draw_image` on Graphite canvases.
- 2026-05-14T17:42:00Z — complete (agent-claude-cody-shim2). Fixed
  `ts_canvas_draw_image`: it now uploads the raster RGBA pixels to a GPU
  texture-backed `SkImage` via `SkImages::TextureFromImage(recorder,
  rasterImage, {})` (`include/gpu/graphite/Image.h`) before `drawImageRect` —
  a raster `SkImage` alone is a silent no-op on a Graphite canvas. The
  `Recorder` is threaded from `TsContext` -> `TsSurface` -> `TsCanvas` (both
  structs gained a borrowed `skgpu::graphite::Recorder*`); if no recorder is
  present the raster image is still used as a fallback. Rebuilt
  `libtessera_skia.dylib` via the shim CMake (re-link only) and re-installed to
  `runtimes/osx-arm64/native/` (gitignored, not committed). `smoke_test.c`
  gained an image round-trip (upload 16x16 green -> draw scaled into 32x32 dst
  -> flush -> read_pixels -> assert center is green, corner untouched) — C
  smoke test passes. Reverted wp:M3-06j's workaround: un-pinned the PNG + JPEG
  image-pixel tests in `ImagePaintGoldenTests` and the PNG test in
  `Tessera.Engine.Tests/EngineRenderTests` from `PaintBackend.ImageSharp` so
  they run on the default Skia backend, and un-skipped
  `SkiaInteropSmokeTests.DrawImage_BlitsPixels_IntoSurface`. Kept the
  `RenderOptions.Backend` / `RenderDocument(backend:)` arg — it is a
  reasonable general-purpose backend-selection knob (also drives
  `TESSERA_PAINT_BACKEND`), just no longer abused as a workaround. `dotnet
  build` + `dotnet test` green (Skia.Tests 3/3, Paint.Tests 33/33,
  Engine.Tests 79/79, 0 skipped). Link line in `native/shim/CMakeLists.txt`
  unchanged.
