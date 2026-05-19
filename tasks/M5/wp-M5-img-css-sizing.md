---
id: wp:M5-img-css-sizing
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T23:30:00Z"
completed_at: "2026-05-19T23:35:00Z"
branch: "main"
depends_on:
  - wp:M1-08-layout-block-inline
  - wp:M2-07a-img-fetch-decode-paint
subsystem: Starling.Layout
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/07_LAYOUT.md
---

# wp:M5-img-css-sizing — CSS `width`/`height`/`max-*`/`min-*` on `<img>`

## Goal

Honor author CSS sizing on replaced inline elements. Today `<img>` paints at
its intrinsic pixel dimensions regardless of any computed `width`, `height`,
`max-width`, `max-height`, `min-width`, or `min-height`. The bug is reproducible
on https://docs.htmlcsstoimage.com — the just-the-docs theme ships
`img { max-width: 100%; height: auto; }`, which Starling silently ignores, so
oversized images blow out their column.

The fix is to resolve the used width/height per CSS 2.1 §10.3.2 / §10.4
(replaced element + min/max clamps) at *inline-layout* time using the box's
`ComputedStyle`, with percentages on the inline axis resolved against the
containing block's available width.

## Inputs

- `src/Starling.Layout/Inline/InlineLayout.cs` — `LayoutImage` (formerly used
  raw `image.IntrinsicWidth`/`IntrinsicHeight`).
- `src/Starling.Layout/Block/BlockLayout.cs` — `ResolveLength` /
  `ResolveMaxLength` helpers (already `internal`).
- `src/Starling.Layout/Box/Box.cs` — `ImageBox` (intrinsic dimensions live
  on the box; not modified by this WP).
- `src/Starling.Layout/Tree/BoxTreeBuilder.cs` — image construction; HTML
  presentational `width`/`height` attribute handling continues to populate the
  intrinsic dimensions on the box (so a CSS-free `<img width=…>` is unchanged).

## Outputs

- `LayoutImage` consults the box's computed style and the resolution
  algorithm below to produce the *used* width/height; the frame written to
  the box reflects this used size, not the raw intrinsic.
- Resolution algorithm (CSS 2.1 §10.3.2 + §10.4, simplified):
  1. Resolve `width`/`height` and their `min-*` / `max-*` counterparts via
     `BlockLayout.ResolveLength`; `width` percentages use the available
     inline-axis width as the basis. `max-*: none` is "no upper bound".
  2. If both `width` and `height` are specified, use them as-is. If only
     one is specified, the other defaults to the intrinsic ratio. If
     neither is specified, fall back to the box's intrinsic dimensions.
  3. Clamp by `max-width`, `min-width`, `max-height`, `min-height` in turn.
     When the constrained axis was `auto` (i.e. derived from the other
     axis), preserve the aspect ratio while clamping; when it was
     explicit, clamp the constrained axis only.
- Tests in `tests/Starling.Layout.Tests/ImageReplacedSizingTests.cs`
  exercising: `max-width: 100%`, `width: 50%`, explicit `width`+`height`,
  one-axis-with-auto-aspect, `max-height` clamp, `min-width` floor, and the
  docs.htmlcsstoimage.com regression (1200×600 image in a 400px column ⇒
  400×200 used dimensions).

## Acceptance

- New tests under `tests/Starling.Layout.Tests/ImageReplacedSizingTests.cs`
  pass.
- Existing `LayoutEngineTests` and the rest of `Starling.Layout.Tests` /
  `Starling.Paint.Tests` continue to pass — in particular the image
  fallback + background-image cases are unaffected.
- `dotnet build && dotnet test` green from the repo root.

## Notes

- This intentionally keeps `ImageBox.IntrinsicWidth`/`IntrinsicHeight` as
  the *intrinsic* dimensions (including HTML attribute presentational
  hints, which the box-tree builder already folds in). A future cleanup
  could separate "natural intrinsic" from "presentational-hint width" so
  author CSS strictly outranks HTML attributes; that's out of scope here.
- Height percentages still resolve against a containing-block height that
  isn't generally known at inline-layout time, so a percentage `height` on
  an inline `<img>` resolves to `auto` (matches Chromium for the common
  case where the containing block has no specified height).

## Handoff log

- 2026-05-19T23:30Z — created + claimed by agent-copilot-claude-opus-4.7
- 2026-05-19T23:35Z — implemented `InlineLayout.ResolveReplacedSize` (CSS 2.1 §10.3.2 + §10.4 for replaced elements). Added `tests/Starling.Layout.Tests/ImageReplacedSizingTests.cs` (11 cases: no-CSS baseline, `max-width:100%` on big + small images, `width:%`, explicit width+height, single-axis aspect ratio, `max-height`, `min-width`, `max-width:none`, CSS overriding HTML width attr). Full sln green (`dotnet test`: 9000+ pass, 0 fail). Completed by agent-copilot-claude-opus-4.7.
