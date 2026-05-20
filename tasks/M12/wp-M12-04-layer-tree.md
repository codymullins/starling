---
id: wp:M12-04-layer-tree
milestone: M12
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T17:13:50Z"
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-02-picture-cache
  - wp:M12-03-stacking-contexts
blocks:
  - wp:M12-05-tile-grid
  - wp:M12-06-invalidation
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-04-layer-tree — Build a compositor layer tree

## Goal

Convert the flat `DisplayList` into a tree of `CompositorLayer`s — one per
`LayerHint`-promoted box plus an implicit root layer. Each layer owns:

- its page-coord bounds (the union of the painted items it contains),
- its display-list slice (the items painted by its subtree, with items
  inside descendant layers excluded),
- its effective transform / opacity / clip from the cascade,
- a reference to its child layers in paint order.

`PageRendererHost` then paints each layer's display-list slice into the
layer's own bitmap (one picture cache per layer, reusing the cache from
`wp:M12-02-picture-cache`) and composites them top-down with their
effective transform / opacity / clip applied per layer.

## Inputs

- `wp:M12-02-picture-cache` shipped (per-layer cache is a straightforward
  generalization of the single-layer cache).
- `wp:M12-03-stacking-contexts` shipped (every promotable box is tagged).
- `DisplayListBuilder` from M1.

## Outputs

- New `Starling.Paint.Compositor.CompositorLayer`:
  ```
  Rect Bounds (page coords)
  DisplayList Items
  Matrix3x2 Transform
  float Opacity
  Rect? Clip
  IReadOnlyList<CompositorLayer> Children
  PictureCache Cache
  ```
- A `LayerTreeBuilder.Build(BlockBox root) → CompositorLayer` that walks
  the layout tree, opens a new layer at each `LayerHint`-tagged box, and
  routes display items into the deepest open layer that contains them.
- `Compositor.Render(viewport, scale) → RenderedBitmap` that walks the
  layer tree, asks each layer's `PictureCache` to serve its slice into a
  layer-local bitmap, then composites layer bitmaps with their effective
  transform/opacity onto the output via the existing
  `ImageSharpBackend` blit primitives.
- The transform brackets emitted by `DisplayListBuilder` for promoted
  boxes (from `wp:M5-css-02-transform-paint`) are removed for promoted
  subtrees — the layer carries the transform now, so paint inside the
  layer is in untransformed local space and only the final composite
  applies the matrix. Non-promoted transformed boxes keep the existing
  push/pop path.

## Acceptance

- A page with one promoted `transform: rotate(45deg)` div renders
  identically (SSIM ≥ 0.99) to the M5 path that pre-baked the rotation
  into per-glyph polygon outlines. The new path: layer's local bitmap
  contains the upright text; final composite applies the rotation.
- A page with `opacity: 0.5` over a coloured background shows the correct
  alpha-blended result (the layer is painted at full opacity into its
  cache, then the composite step applies opacity).
- Scrolling a page where only one promoted subtree moves only re-blits
  that subtree's layer; other layer caches stay valid.
- `dotnet build && dotnet test` green.

## Notes

- The implicit root layer is always present even for pages with no
  promotion — `LayerTreeBuilder.Build` returns a single-node tree in that
  case. This keeps the call site uniform.
- Layer ordering: children are stored in *paint order* (the order the
  spec says to draw them — z-index sort happens here, not at composite
  time). See CSS-Position 3 §9.
- `Clip` initially handles only `overflow: hidden` rectangles. Border
  radius clipping is deferred to a later WP.
- Transformed layers still suffer the same wgpu-max-texture issue when
  their bounds exceed 8192 px in any dimension. The next WP (tile grid)
  fixes that by splitting per-layer caches into tiles.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-20T17:13:50Z — claimed by agent-claude-cody, working on main
- 2026-05-20 — completed (agent-claude-cody). Added
  `Starling.Paint.Compositor.CompositorLayer` (page-coord Bounds + slice Items +
  Matrix2D Transform + Opacity + Rect? Clip + paint-ordered Children + per-layer
  PictureCache), `LayerTreeBuilder.Build(BlockBox) → CompositorLayer` (root layer
  always present; opens a layer at every `Hints != None` box; routes display
  items into the deepest enclosing layer; z-index paint-order sort per
  CSS-Position-3 §9 done at build time), and `Compositor.Render(viewport, scale,
  pageVersion)` (per-layer slice rastered into its own cache, then top-down
  alpha-over composite applying composed transform/opacity/clip via a pure-managed
  inverse-mapped bilinear sampler). DisplayListBuilder refactor was option (b):
  threaded an optional layer-boundary predicate + `BuildLayerSlice` through the
  EXISTING `Visit`/`PaintBoxAndChildren` recursion (null on the flat path → flat
  `Build` is byte-identical for all current callers); promoted layer roots skip
  their own transform bracket (layer carries it), non-promoted transformed boxes
  keep push/pop. Added a transparent-background `Render` overload to
  `ImageSharpBackend`/`IPaintBackend` (default stays opaque white) so layer slices
  composite correctly. Wired `PageRendererHost.RenderViaLayerTree` as the new
  entry point; flat path untouched. Acceptance: transform-parity SSIM = 0.998
  (≥ 0.99); opacity blend, z-index order, and per-layer cache-isolation tests all
  green. Full solution builds; FULL suite green (Engine 112/112 golden+SSIM,
  Paint 107/107, Gui 82/82, plus all others). Unblocks M12-05 + M12-06.
