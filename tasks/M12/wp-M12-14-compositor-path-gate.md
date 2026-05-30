---
id: wp:M12-14-compositor-path-gate
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-13-gpu-composite-blend
blocks: []
subsystem: Starling.Gui
plan_refs:
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
  - layer-tree-plan.md
---

# wp:M12-14-compositor-path-gate — Widen the compositor-path gate past animation frames

## Goal

Let pages with static compositor layers (a fixed header, a translucent panel)
take the layer-tree path on scroll and navigation, not just on animation frames,
once the blend is cheap enough that this never regresses frame time.

## Inputs

- `wp:M12-13-gpu-composite-blend` shipped, so compositing is no longer the floor
  on frame time.
- The current gate in `WebviewPanel.RenderPageBitmap`
  (`src/Starling.Gui/Controls/WebviewPanel.cs`) is
  `useLayerTree = scrollLookup is null && _animating`.

## Outputs

- A widened gate. The plan's original form is
  `scrollLookup is null && (PageHasStaticCompositorLayers() || pageIsAnimating)`.
- A byte-exact golden sweep that proves the layer-tree path matches the flat path
  for static pages on scroll and on first paint.

## Acceptance

- Static pages with a promoted layer take the layer-tree path on scroll and on
  navigation, and their output stays byte-identical to the flat path. Add or
  extend goldens that scroll a page with an `opacity` or `transform` box.
- No frame-time regression on a cheap-to-raster static page at scale 1.0 or 2.0
  (measured with the replay harness).
- A page with no compositor layers, or with per-container scroll
  (`scrollLookup` non-null), still falls back to the flat path.

## Notes

LTF-04 (`layer-tree-plan.md`) deliberately shipped the narrow `_animating` gate
instead of the plan's wider condition. Routing every static page's scroll and
navigation through the layer tree is too large a change to enable without a full
byte-exact golden sweep, and while the blend is expensive it would regress cheap
pages (the composite path measured slower than the flat path at scale 1.0, and
much slower at scale 2.0). Once `wp:M12-13-gpu-composite-blend` removes the blend
cost, both blockers clear and the gate can widen.
