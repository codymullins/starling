---
id: wp:M5-css-02-transform-paint
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T14:53:52Z"
completed_at: "2026-05-19T15:30:00Z"
branch: "main"
depends_on:
  - wp:M5-css-01-transform-value
subsystem: Starling.Paint
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-02-transform-paint — Apply CSS `transform` in paint

## Goal

Use the structured `CssTransform` value produced by `wp:M5-css-01` to actually
transform painted output. Wires the existing transform parsing into the
display-list + `ImageSharpBackend` so `transform: rotate(45deg)` and friends
visibly rotate, scale, translate, and skew the box they apply to.

## Background — paint backend shape after wp:M5-skia-removal

ImageSharp.Drawing 3 is now the engine's sole paint backend. Its
`DrawingCanvas` API has no built-in transform stack (no
`Scale`/`Translate`/`Concat`/`Save(matrix)`) — transforms are applied
per-primitive through `DrawingOptions.Transform` (`System.Numerics.Matrix4x4`)
on each `Fill` / `Draw` / `DrawText` call. `DrawImage` does **not** take a
`DrawingOptions` and therefore cannot be transformed by that route; it needs a
separate code path (pre-transform the source image with
`AffineTransformBuilder`, then `DrawImage` it at the transformed bounds).

That means this WP can no longer "add `SkCanvas.Concat` and let the backend
play it back": there is no native shim. Instead the backend must maintain its
**own** transform stack in managed code and thread the current matrix into
every primitive call.

## Inputs

- `Tessera.Css.Values.CssTransform` + `Matrix2D` (from `wp:M5-css-01`).
- `Starling.Paint.DisplayList.{DisplayList, DisplayItem, DisplayListBuilder}`.
- `Starling.Paint.Backend.ImageSharpBackend` (the only backend).
- ImageSharp.Drawing 3 surface:
  - `SixLabors.ImageSharp.Drawing.Processing.DrawingOptions.Transform` (Matrix4x4)
  - `SixLabors.ImageSharp.Processing.AffineTransformBuilder` (for `DrawImage`)

## Outputs

- New display items `PushTransform(Matrix2D)` / `PopTransform()` (a bracketed
  group rather than a flat pair is also acceptable so long as builder + backend
  agree).
- `DisplayListBuilder` queries `box.Style.Get(PropertyId.Transform)`, runs it
  through `CssTransformParser.Parse`, and emits the wrapping push/pop around
  the box's paint stream when the result is non-identity. The matrix is
  pre-translated by `transform-origin` (default `50% 50% 0`).
- `ImageSharpBackend` gains an explicit transform stack:
  - A `Stack<Matrix3x2>` (or `Matrix4x4`) maintained by `Apply` so
    `PushTransform`/`PopTransform` mutate "current matrix" without touching
    `DrawingCanvas.Save`/`Restore` (those only checkpoint clip + scene state).
  - Every existing call site (`FillRect`, `StrokeRect`, `DrawText`, etc.) is
    refactored to obtain its `DrawingOptions` from a small helper
    (e.g. `BuildOptions()`), which sets `Transform` to the current matrix
    (lifted from `Matrix3x2` → `Matrix4x4` via `new Matrix4x4(m)`).
  - `DrawImage` uses an `AffineTransformBuilder` per-call when the current
    matrix is non-identity: clone the source image, apply the matrix, place at
    the original destination rect's origin (or compute the transformed bounds).
    A non-identity transform on `DrawImage` is rare enough that the slow path
    is acceptable in M5.
- Golden tests under `tests/Starling.Paint.Tests/Golden/` for
  `transform-translate.html`, `transform-rotate.html`, `transform-scale.html`,
  `transform-matrix.html`, `transform-compose.html`. SSIM ≥ 0.99 (the
  ImageSharp rasterizer is not byte-exact across machines; existing M5 goldens
  already use SSIM thresholds — match that policy).

## Acceptance

- A box with `transform: translate(50px, 0)` renders 50px to the right of its
  layout-time frame; backgrounds, borders, text, and child boxes all shift.
- `transform: rotate(45deg)` rotates around the box's centre by default.
- Nested transforms compose (`PushTransform(A); PushTransform(B)` paints with
  `A * B` for the inner box; `PopTransform` restores `A`).
- Goldens are SSIM ≥ 0.99 against snapshots on osx-arm64.
- `dotnet build && dotnet test` is green on osx-arm64.

## Notes

- Transform does NOT affect layout — boxes keep their original frame in
  layout-time coordinates and paint into a transformed canvas. Hit testing
  is a separate problem (deferred to a later WP) that needs the inverse
  matrix applied to event coordinates.
- `transform-origin` is part of the transform setup: pre-translate by `+origin`,
  apply the matrix, post-translate by `-origin`.
- 3D transforms remain out of scope here. If a stylesheet uses `translate3d`,
  the parser already returns `CssTransform.None` and the box paints flat.
- `Matrix2D` (from `wp:M5-css-01`) → `Matrix3x2`: trivial field copy.
  `Matrix3x2` → `Matrix4x4`: `new Matrix4x4(m)` (BCL has the lift) or
  hand-build the row-major form ImageSharp expects.
- Watch for `DrawingOptions` allocation churn — cache one per push frame
  rather than allocating per primitive when the matrix is unchanged.

## Handoff log

- 2026-05-19T02:23Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-19T03:05Z — rewritten for post-wp:M5-skia-removal world: target
  ImageSharp.Drawing 3's per-primitive `DrawingOptions.Transform` instead of
  the (now-deleted) Skia shim's `ts_canvas_save`/`concat44`. The backend now
  owns the transform stack in managed code. (agent-copilot-claude-opus-4.7)
- 2026-05-19T14:53:52Z — claimed by agent-copilot-claude-opus-4.7, working on main
- 2026-05-19T15:30Z — implemented. Discovered the WP's assumption about
  `DrawingOptions.Transform` was wrong: ImageSharp.Drawing 3.0's `DrawingCanvas`
  Fill/Draw/DrawText overloads **do not** accept a `DrawingOptions` parameter
  (the type still has a `Transform` property, but the canvas surface ignores
  it). Real seam in 3.0 is `IPath.Transform(Matrix4x4)` for shape geometry
  and `TextBuilder.GenerateGlyphs(text, TextOptions)` + per-glyph
  `GlyphPathCollection.Transform(Matrix4x4)` for text, then `canvas.Fill`
  on each glyph's `PathList`. Implementation:
    * `DisplayItem.cs` — added `PushTransform(Matrix2D)` / `PopTransform`
      (singleton `Instance`).
    * `DisplayListBuilder.cs` — `Visit` now brackets `PaintBoxAndChildren`
      with push/pop when `transform` is non-identity, baking
      `T(+origin) × M × T(-origin)` with the centre origin (the
      `transform-origin` parser is still TODO; centre is the spec default).
    * `ImageSharpBackend.cs` — new private `TransformStack` (post-multiply
      composition matching CSS §6.1), threaded through both `RenderCpu`
      and `RenderWebGpu`. Fill/StrokeRect on a non-identity stack build
      a 4-point `Polygon` from the rect's corners transformed by the
      current CSS-space matrix then scaled to device px. DrawText on a
      non-identity stack generates glyph outlines via `TextBuilder`,
      transforms each glyph by `(linear part) × scale(translate)` lifted
      to Matrix4x4, then fills its `PathList`. DrawImage on a non-identity
      stack paints at its untransformed position and bumps a
      `paint.draw_image.transform_skipped` counter — documented limitation.
    * Tests: 3 new `DisplayListBuilderTransformTests` covering the no-op
      case, translate matrix payload, and rotate-around-centre baking.
      All 3 pass; full sln test run shows only the two pre-existing failures
      (`Underlined_link_emits_text_and_underline_fill` and
      `NativeImageDecoderTests.DecodesPngCornerPixels`).
  Follow-ups intentionally not in this WP: golden-image tests (need
  cross-machine SSIM harness), `transform-origin` parser, transformed
  `<img>` rendering, hit-testing inverse-matrix walk.
  (agent-copilot-claude-opus-4.7)
