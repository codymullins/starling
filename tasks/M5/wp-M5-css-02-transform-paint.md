---
id: wp:M5-css-02-transform-paint
milestone: M5
status: available
claimed_by: ""
claimed_at: ""
branch: main
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
display-list + Skia backend so `transform: rotate(45deg)` and friends visibly
rotate, scale, translate, and skew the box they apply to.

## Inputs

- `Tessera.Css.Values.CssTransform` + `Matrix2D` (from `wp:M5-css-01`).
- `Starling.Paint.DisplayList.{DisplayList, DisplayItem, DisplayListBuilder}`.
- `Starling.Paint.Backend.SkiaGraphiteBackend`.
- `Starling.Skia.Handles.SkCanvas` — currently exposes only `Clear`, `Scale`,
  `FillRect`, `StrokeRect`, `DrawText`, `DrawImage`. Needs `Save`, `Restore`,
  `Translate`, `Concat(Matrix2D)` (or equivalent matrix op) added.

## Outputs

- New display items `PushTransform(Matrix2D)` / `PopTransform()` (or a
  bracketed `TransformGroup(Matrix2D, IReadOnlyList<DisplayItem>)`).
- `DisplayListBuilder` queries `box.Style.Get(PropertyId.Transform)`, runs it
  through `CssTransformParser.Parse`, and emits the wrapping push/pop around
  the box's paint stream when the result is non-identity. The matrix is
  pre-translated by `transform-origin` (default `50% 50% 0`).
- Skia shim additions:
  - native: `ts_canvas_save`, `ts_canvas_restore`, `ts_canvas_concat44`
    (Skia takes a 4×4; pass an identity-padded 2D matrix).
  - managed: `SkCanvas.Save`, `Restore`, `Concat(Matrix2D)`.
- `SkiaGraphiteBackend.Apply` handles the new items.
- `ImageSharpBackend` (currently the diff-target backend) gets an equivalent
  implementation, or is excluded from the goldens for transformed pages.
- Golden tests under `tests/Starling.Paint.Tests/Golden/` for
  `transform-translate.html`, `transform-rotate.html`, `transform-scale.html`,
  `transform-matrix.html`, `transform-compose.html`.

## Acceptance

- A box with `transform: translate(50px, 0)` renders 50px to the right of its
  layout-time frame; backgrounds, borders, text, and child boxes all shift.
- `transform: rotate(45deg)` rotates around the box's centre by default.
- Goldens are byte-exact or SSIM ≥ 0.99 against snapshots.
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

## Handoff log

- 2026-05-19T02:23Z — created (agent-copilot-claude-opus-4.7)
