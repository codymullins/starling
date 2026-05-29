# Animation frame cost: relayout and draw

A working note on why the Animations demo used to lag, and what fixed it. The
lag had two causes. Every frame ran a full relayout of the whole page. Every
frame also redrew the whole viewport. Both are now addressed.

- **Relayout.** Incremental layout is the default. A frame reconciles only the
  subtrees that changed instead of rebuilding the box tree from scratch. See the
  incremental layout work and `LayoutSession`.
- **Draw.** The layer-tree compositor draws an animation frame by re-blitting
  each layer from a cache and re-rastering only the layers whose content
  actually changed. This is the LTF series described below.

## How an animation frame works now

`WebviewPanel.LiveTick` (`src/Starling.Gui/Controls/WebviewPanel.cs`) runs once
per frame while a page is live.

1. `scripting.PumpFrame(...)` runs the page's `requestAnimationFrame` callbacks.
   The demo rewrites a small status line each frame.
2. If the frame made a layout-relevant change, `RefreshLiveLayout` runs an
   incremental relayout. Pure `data-*` / `aria-*` writes do not trigger one.
3. The page advances its animation clock.
4. The frame draws once. When the page is animating, the draw takes the
   compositor layer-tree path instead of a flat full-viewport raster.

## The layer-tree compositor (LTF series)

The key idea is to separate a layer's cached **content** from its per-frame
**compositing parameters**. Content is the rasterized slice (fills, text,
images). It is cached and re-rastered only when it changes. Transform, opacity,
and clip are read every frame and applied when the layers are composited. They
never invalidate the content cache.

- **LTF-01 — promote animating elements.** A per-frame predicate gives an
  actively-animating element its own layer, even with no static stacking
  context. A transform or opacity rides on the layer and is applied at composite
  time, so the slice stays upright and cacheable.
- **LTF-02 — content-hash cache key.** Each layer's cache is keyed by a 64-bit
  hash of its slice content, not the global page version. A transform/opacity-
  only frame hashes the same and re-blits from cache. A content change re-rasters
  that one layer.
- **LTF-03 — caches persist across relayout.** An in-place relayout no longer
  wipes the per-layer caches. They clear only on navigation. The content hash
  keeps this correct.
- **LTF-04 — composite on relayout frames.** The compositor path is taken on any
  animating frame, even one that relayouted. The old gate dropped to the flat
  path whenever a frame relayouted, which the demo does every frame, so the
  caches never helped.
- **LTF-05 — fast blit.** A layer that lands as an integer-pixel translation at
  full opacity skips the matrix inverse and bilinear sample and blits rows
  directly. This is byte-identical to the general path.
- **LTF-06 — isolate the mutated subtree.** A subtree a script mutated in the
  last few frames is promoted to its own layer, so the base layer's content hash
  stays stable and the base serves from cache. Only the small mutated layer
  re-rasters.

Correctness is pinned by goldens: the layer-tree path is byte-identical (or
within a high similarity score for rotations) to the flat path at a fixed clock.
See `tests/Starling.Paint.Tests` (`LayerContentCacheTests`, `LayerPromotionTests`,
`FastBlitTests`, `LayerMutationIsolationTests`) and the Gui headless tests.

## Measurements

Captured with the frame-replay harness on the `compositor-demo` scenario (a
static base, a transform-only spinner, and a per-frame status line). Run it with
`dotnet run --project bench/Starling.Bench -- replay compositor-demo --composite`.
A quick check is `replay --selftest`.

The raster-call win is clear and is what this stage targets:

| Path | Layers / frame | Rastered / frame | Blitted from cache / frame |
| --- | --- | --- | --- |
| compositor | 3 | **1** | 2 |

The flat path re-rasters the whole viewport every frame. The compositor path
re-rasters only the one layer whose content changed (the status line) and blits
the base and the spinner from cache.

Frame time is a different story on the pure-managed backend. The compositor
blend runs on the CPU and touches every output pixel for each layer, so its cost
scales with the viewport, not with what changed. On a cheap-to-raster page like
`compositor-demo` (solid fills), the blend costs more than the flat raster it
replaces, so the composite path is slower at scale 1.0 and much slower at
scale 2.0.

| compositor-demo, 150 frames | flat raster (ms) | composite (ms) |
| --- | --- | --- |
| raster phase, scale 1.0 | ~6.5 | ~8.6 |
| raster phase, scale 2.0 | ~5.6 | ~31 |

So the per-frame draw work moves from "raster the whole viewport" to "blit cached
layers plus raster the changed ones." That is a real structural win, and it pays
in frame time when a cached layer is expensive to raster (text, gradients,
images) and does not reflow. It does not pay when the page is cheap to raster,
because the managed blend then dominates. The general frame-time win needs the
composite blend itself on the GPU, which is out of scope for this stage.

## Still open

- GPU compositing of the blend (the present/readback path). This is what turns
  the raster-call win into a frame-time win for every page.
- Glyph atlas, per-container scroll on the compositor path, and intra-layer
  damage rects (re-raster only the changed rectangle of a layer).
- LTF-04's gate is currently "the page is animating." A page with static
  compositor layers does not take the path on scroll or navigation, to avoid a
  blend-cost regression on cheap pages. Widening it waits on a cheaper blend.

## History: animation engines survive relayouts

The `AnimationEngine`, `TransitionEngine`, and `AnimationCompositor` live on a
per-document `AnimationTimeline` that outlives the `StyleEngine`
(`src/Starling.Css/Animations/AnimationTimeline.cs`). A relayout reuses the live
engines instead of rebuilding them, so a frame no longer re-imports or re-primes
animation state. Web Animations (`element.animate`) register straight into the
persistent engine, and declarative `@keyframes` priming is keyed on the laid-out
box-tree root so the same tree primes once across its frames.
