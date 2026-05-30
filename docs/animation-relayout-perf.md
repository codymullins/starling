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

### Frame time: the GPU blend (wp:M12-13)

The blend used to run on the CPU. It touched every output pixel for each layer,
so its cost scaled with the viewport, not with what changed. On a cheap page
like `compositor-demo` (solid fills), that blend cost more than the flat raster
it replaced. The composite path was slower at scale 1.0 and far slower at
scale 2.0.

The blend now runs on the GPU. Each layer uploads to a wgpu texture once, keyed
by its slice content hash, and stays resident across frames. An unchanged layer
is never re-uploaded. Every frame the layers blend in one render pass — a
textured quad per layer, placed by its transform, scaled by its opacity, and
clipped by a scissor rect. The blend is alpha-over in premultiplied space, which
reproduces the CPU `AlphaOver` math. The only per-frame transfer is the final
viewport readback, the same transfer the flat path already pays. See
`src/Starling.Paint/Compositor/GpuLayerCompositor.cs`. The CPU blend stays as the
fallback for hosts with no GPU adapter, and the two paths are pinned together by
`GpuCompositeParityTests`.

Measured on the WebGPU backend (`imagesharp-webgpu`, 150 frames):

| compositor-demo | flat raster (ms) | composite raster (ms) | flat frame (ms) | composite frame (ms) |
| --- | --- | --- | --- | --- |
| scale 1.0 | ~12.9 | ~12.4 | ~13.4 | ~12.9 |
| scale 2.0 | ~17.2 | ~16.1 | ~17.8 | ~16.5 |

The composite path is now at or below the flat path at both scales: it re-rasters
only the one changed layer and blends the rest from resident textures, instead of
re-rastering the whole display list. The win grows with how expensive the cached
layers are to raster (text, gradients, images) and with the scale.

## Still open

- Glyph atlas, per-container scroll on the compositor path, and intra-layer
  damage rects (re-raster only the changed rectangle of a layer).
- LTF-04's gate is currently "the page is animating." A page with static
  compositor layers does not take the path on scroll or navigation. The blend is
  now cheap enough to widen that gate — see `wp:M12-14-compositor-path-gate`.

## History: animation engines survive relayouts

The `AnimationEngine`, `TransitionEngine`, and `AnimationCompositor` live on a
per-document `AnimationTimeline` that outlives the `StyleEngine`
(`src/Starling.Css/Animations/AnimationTimeline.cs`). A relayout reuses the live
engines instead of rebuilding them, so a frame no longer re-imports or re-primes
animation state. Web Animations (`element.animate`) register straight into the
persistent engine, and declarative `@keyframes` priming is keyed on the laid-out
box-tree root so the same tree primes once across its frames.
