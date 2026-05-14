---
id: "wp:M3-06i-skia-backend"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-skia-backend"
claimed_at: "2026-05-14T17:30:00Z"
branch: "main"
depends_on:
  - "wp:M3-06h-skia-interop"
  - "wp:M3-06c-decoded-image"
blocks:
  - "wp:M3-06j-skia-fonts"
subsystem: "Tessera.Paint"
plan_refs:
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06i-skia-backend — `SkiaGraphiteBackend` + `RenderedBitmap`

## Goal

Phase 5: add `SkiaGraphiteBackend` to `Tessera.Paint` — the same role as
`ImageSharpBackend`, consuming the **100% unchanged** `DisplayList` /
`DisplayItem` (that is the seam) and rendering through the `Tessera.Skia`
handles. Introduce `RenderedBitmap` in `Tessera.Common` and have
`Painter.RenderDocument` return it instead of `Image<Rgba32>`. Run old + new
backends **side by side behind a flag** so output can be diffed before
`ImageSharpBackend.cs` is deleted (deleted last, after goldens are re-baselined
in `06j`).

## Inputs

- `wp:M3-06h-skia-interop` complete: `Tessera.Skia` with context/surface/canvas
  handles and a passing interop smoke test.
- `wp:M3-06c-decoded-image` complete: `DecodedImage` is the final `DrawImage`
  payload type — the backend builds on that signature.

## Outputs

- `src/Tessera.Common/Image/RenderedBitmap.cs` — `{ int Width, int Height,
  byte[] Rgba }`.
- `src/Tessera.Paint/Backend/SkiaGraphiteBackend.cs` — walks `DisplayList`,
  issues the 4 `DisplayItem` ops through `Tessera.Skia`, `flush_and_submit` +
  `read_pixels` → `RenderedBitmap`.
- `src/Tessera.Paint/Painter.cs` — `RenderDocument` returns `RenderedBitmap`;
  backend selected behind a flag (`SkiaGraphiteBackend` vs `ImageSharpBackend`).
- `src/Tessera.Engine/Engine.cs` (~lines 136–175) — consume `RenderedBitmap`.
- `src/Tessera.Headless/Program.cs` — consume `RenderedBitmap` (PNG encode from
  raw RGBA stays in C# for now).
- Golden test pixel-readers updated to read `RenderedBitmap`.
- `DisplayList` / `DisplayItem` — **unchanged** (verify, do not edit).

## Acceptance

- `SkiaGraphiteBackend` renders the display list with no edits to `DisplayList`
  or `DisplayItem`.
- `RenderedBitmap` exists in `Tessera.Common`; `Painter.RenderDocument` returns
  it; `Engine`, `Headless`, and golden pixel-readers consume it.
- The dual-backend flag selects either backend; with the flag on
  `ImageSharpBackend`, all existing goldens still pass byte-exact.
- `dotnet run --project src/Tessera.Headless -- render testdata/hello.html -o
  out.png` succeeds on both backends; old-vs-new output is diffable behind the
  flag.
- `ImageSharpBackend.cs` is **not** deleted in this package.
- Full repo `dotnet test` green.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 5).
- GPU output is not bit-exact across drivers — final per-platform SSIM threshold
  retune happens at integration; goldens are re-baselined in `06j`.
- `ImageSharpBackend.cs` is deleted last, in the final integration merge after
  `06k-gui-canvas`, when the flag flips for good.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
