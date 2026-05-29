# Incremental layout — Phases 1 through 5 status

This continues `PHASE0_STATUS.md`. Phase 0 (the measurement plan and the
dual-run safety net) landed first. This doc covers the rest.

## Phases 1–3 — done and verified

The core of the plan ships: layout recomputes only the subtrees a mutation
touched and reuses the rest, keyed on `(input, constraint-space)`.

### How it works

A persistent `LayoutSession` (`src/Starling.Layout/Incremental/`) keeps the box
tree and an element/text-node → box map alive across frames. Each frame it:

1. Drains the document's mutation batch — `(target, ChangeKind)` records written
   at the text, attribute, and child insert/remove sites, only for nodes
   connected to the live tree.
2. Reconciles the changed subtrees:
   - **text change** → refresh the text box in place and re-shape it,
   - **layout-relevant attribute** → re-cascade and rebuild that element's box
     subtree, splice it back in place,
   - **child insert/remove** → rebuild only the affected parent's subtree, with
     a localized re-run of the anonymous-block wrapping,
   - **active animation/transition target** → re-cascade at the frame clock
     (its style changes with no DOM mutation to record).
3. Marks the root-to-change paths dirty and re-runs the block pass. A clean
   subtree with an unchanged constraint space is repositioned in O(1) — never
   descended into or re-shaped — because every box `Frame` is parent-relative.

Anything the reconciler can't prove safe (a display-level flip, a viewport
resize, an unmapped or document-level target) falls back to a full rebuild,
which is always correct.

### Why this shape, not a separate immutable-fragment output tree

The plan proposed immutable fragments as the *mechanism* for soundness and
thread-sharing. We get the same soundness from the reuse key — a box is reused
only when it is off every dirty path and its constraint space is unchanged, so a
changed input can't be silently reused — plus the dual-run verifier that checks
every incremental frame against a full rebuild. Building a parallel fragment
tree that the paint and hit-test code would have to be rewritten to read buys no
behavior and a lot of risk, so the retained box tree stays the consumed
representation. This is the deliberate deviation AGENTS.md asks us to record.

The full-rebuild path is byte-for-byte unchanged: the incremental code only runs
when `STARLING_INCREMENTAL_LAYOUT=1`, so the 12,000-plus existing tests protect
the default path by construction.

### Engine wiring (Phase 2e/2f)

`RelayoutPage` routes through the session when the feature is on, reusing the
page's `StyleEngine` — so the animation and transition engines are no longer
rebuilt per frame — and a retained tree carried across relayout successors. A
test asserts the StyleEngine identity is stable across frames.

### Verification (Phase 2g)

`STARLING_LAYOUT_VERIFY=1` makes the session re-run a full rebuild each frame and
log the first divergence. Tests flip the 0d harness to incremental-vs-full and
assert the incremental path is actually taken (counter checks) and matches a
full rebuild after text, attribute, and structural changes. 227 layout tests
plus engine integration tests are green.

## Phase 4 — per-frame hit-index / cache reuse: gated, deferred

The plan gates Phase 4 explicitly: run it *only if* the 0a trace shows
`show_page.hit_index` (and the picture-cache wipe) is a significant slice of the
frame, and skip it otherwise.

Two things make this a deliberate deferral here rather than a skipped step:

- **The gate can't be evaluated.** 0a needs the live GUI animation loop and the
  Aspire dashboard. The default WebGPU paint backend can't start in this sandbox
  (no GPU adapter), and Aspire isn't running, so the hit-index ratio is
  unmeasured. (See `PHASE0_STATUS.md`.)
- **Analysis says the benefit is conditional.** The hit-test index is a flat
  list of fragments at absolute document positions. A geometry change to any box
  shifts the absolute position of every later fragment, so an incremental index
  update saves work only when the change is at the very end of the document. The
  plan flags this, which is why it gates the phase on a real measurement. The
  repo performance policy (AGENTS.md) likewise says not to add this kind of
  complexity without a measured benefit.

What unblocks it: capture the 0a trace on a GPU-capable host with Aspire up. If
`show_page.hit_index` is a real slice, make `BoxHitTester` rebuild the index only
when fragment *structure* changes and reuse it when only geometry shifted, and
scope `_renderer.InvalidateCache()` to the changed region. Until then, building
it would be speculative complexity the plan tells us to avoid.

### Where the sound/speculative line actually falls (analyzed)

Mapping the code against the gate sharpened the boundary:

- **Already satisfied — reuse the hit index on animation-only frames.** A pure
  transform/opacity frame doesn't relayout, so it never calls `ShowPage`, which
  is the only caller of `BoxHitTester.CollectFragments`. The fragment list is
  already reused untouched across those frames. There is no code to add here.

- **Still speculative — make layer caches survive a *relayout*.** The realistic
  animation (a rAF that writes one bit of text every frame, e.g. a status
  readout) does relayout each frame, which calls `ShowPage` →
  `InvalidateCache`, wiping the per-layer caches. Keeping them across the
  relayout does not help on its own, because the layer cache key is a **global**
  page version (`DisplayListVersion`) that bumps for the whole page on any
  relayout — so every layer would re-raster anyway. Real reuse needs a
  **per-layer content version** so an unchanged layer keeps its key while the
  one changed layer bumps. That is exactly the structural complexity the gate
  defers until a 0a trace proves the cost is real. Building it now would break
  the repo performance policy (no complexity without a measured win).

So Phase 4 stays gated by design. The cross-phase note for whoever picks it up:
scoped invalidation must keep `_layerCaches` keyed per layer, not wipe it, or it
will fight Phase 5's reuse.

## Phase 5 — composited layers for animation-only frames: core mechanism done

Phase 5 addresses DRAW cost, not LAYOUT cost: a `transform`/`opacity` animation
should re-composite a cached layer rather than re-rasterize it.

### What was already in place
The substrate existed: `StackingContextResolver` tags layer roots with
`LayerHint`, `LayerTreeBuilder` slices the box tree into `CompositorLayer`s whose
`transform`/`opacity` are applied at **composite** time (not baked into the
slice), and the `Compositor` rasters each layer into a per-layer `PictureCache`.

### The gap, and the fix (done)
The caches were created fresh on every `LayerTreeBuilder.Build`, so they never
survived to serve a later frame — every frame re-rasterized. The fix is a
persistent `LayerCacheStore` that holds one `PictureCache` per layer keyed by the
layer root's DOM **element** (stable across re-layouts). The layer tree is still
rebuilt each frame (cheap — it re-reads the current animated `transform`/
`opacity`), but each layer reuses its element's cache, so a frame whose only
change is a composite-time `transform`/`opacity` re-blits the layer's pixels from
cache and never calls the raster backend.

Verified on the CPU backend (`CompositorTests`): after an opacity-only **or**
transform-only change, re-rendering against the same store at the same page
version serves **both** layers (the page background and the promoted div) from
cache — zero re-raster — while the composited output reflects the new opacity /
rotation. This is the plan's Phase 5 acceptance at the compositor level.

### What remains — both integration steps now done

1. **A live animation clock — done (merged).** The live GUI (`WebviewPanel`) now
   has a 16ms timer that drives the animation loop. `LiveTick` calls
   `engine.PrepareAnimationFrame(page, clock)` (ticks the animation/transition
   engines) while `engine.HasActiveAnimations(page)` holds, then repaints once
   per frame with a per-box animated `styleOverride`. This came in by merging
   `feat/render-and-interact-fixes`. The merge kept this branch's narrow
   relayout signal (relayout only when `Document.LayoutInvalidationVersion`
   advances, not on every mutation) on top of that loop, so an animation rAF
   that only writes `data-*` / `aria-*` no longer pays a full reflow per frame.

2. **Route that repaint through `RenderViaLayerTree` — done.** When an animation
   is in flight on a page that has a transform/opacity compositor layer,
   `RenderPageBitmap` now routes through the layer-tree path with a page version
   that **excludes** the animation clock — just `DisplayListVersion`, stable
   across frames — so each layer's content stays in cache while the re-sampled
   transform/opacity are applied at composite. A frame whose only change is a
   composite-time transform/opacity re-blits the cached pixels instead of
   re-rasterizing. The flat path stays the default for every other frame (no
   animation, or no promoted layer), so the existing goldens are untouched.

   The routing predicate (`WebviewPanel.PageHasCompositorLayers`) walks the box
   tree once per layout for a `Transform3D` / `OpacityLessThanOne` / `WillChange`
   layer hint and caches the answer. It is guarded to the no-per-element-scroll
   case, because `RenderViaLayerTree` does not yet thread per-container scroll
   offsets — a scrollable page falls back to the flat path, which handles them
   correctly. **Follow-up:** thread `scrollOffsets` through `LayerTreeBuilder`
   so a page with both an animated layer and a scroll container also takes the
   compositor path.

   Verified headlessly on the CPU backend
   (`AnimationLoopTests.Transform_animation_on_a_promoted_layer_reblits_content_from_cache`):
   a `will-change: transform` div animating `translateX` re-blits its layer
   content from cache across two frames at different clocks (the flat path's
   clock-keyed version would miss every frame) while the composited box still
   moves. The `Compositor`'s own SSIM parity test already proves the layer-tree
   output matches the flat path within tolerance, so the live routing is sound
   to keep on by default. A final golden pass on a GPU host is still the bar
   before trusting it on the WebGPU backend.

## Benchmarks

`bench/Starling.Bench/IncrementalLayoutBench.cs` compares a full rebuild against
an incremental relayout after one small DOM change per frame, on a synthetic
page that scales with a row count and on the real nginx.org snapshot. Both sides
share one pre-built StyleEngine, so the numbers isolate the layout pass.

Indicative results (allocation is deterministic; treat absolute times as
ballpark until a full BenchmarkDotNet run on a quiet host):

| Change            | Rows | Full alloc | Incremental alloc | Time ratio |
|-------------------|-----:|-----------:|------------------:|-----------:|
| Text              |  100 |   49.7 MB  |        0 B        |   ~0.01    |
| Text              | 1000 |  489 MB    |        0 B        |   ~0.01    |
| Attribute         | 1000 |  489 MB    |     ~670 KB       |   ~0.02    |
| No change         | 1000 |  489 MB    |        0 B        |   ~0.01    |
| Structural toggle | 1000 |  467 MB    |      155 MB       |   ~0.29    |
| Text (nginx.org)  |   —  |   22.4 MB  |     ~9 KB         |   ~0.02    |

The headline holds: a text edit, an attribute edit, and a no-op frame reuse the
retained tree and recompute only the dirty subtree — flat allocation regardless
of page size, while the full rebuild's cost grows with the tree.

### Structural changes — §3a per-child splice + §3b localized re-wrap (done)

Child insert/remove now splices the one changed child into the parent's box
children and reuses every unchanged sibling's laid-out subtree by identity, then
re-buckets only the parent's direct child run (the localized anonymous re-wrap).
Build, layout, and text-shaping are O(change). The parent's subtree is still
re-cascaded first, so a sibling whose computed style shifts — a positional
`:nth-child` rule firing as siblings move — is detected (style value-equality)
and that subtree rebuilt, while style-unchanged subtrees are reused. That keeps
it sound for sibling and positional selectors. `:has()` / `:empty`, whose match
can change the cascade outside the changed parent, force a full rebuild instead
(`StyleEngine.StructuralChangeNeedsFullRebuild`).

The residual cost is the re-cascade (the structural toggle lands at ~1/3 of a
full rebuild, cascade-bound), not the near-zero of a text/attr edit, which is
the sound price of catching positional restyles. Tests prove unchanged siblings
are reused by reference-identity, that a geometry-affecting `:nth-child` restyle
on insert still matches a full rebuild, and that `:has()` falls back.

A companion `Fixtures.LocateRepoRoot` fix was needed: it looked only for
`Starling.sln`, but the repo uses `Starling.slnx`, so every fixture-backed
bench threw before running. It now accepts either.

## Test inventory added

- `Starling.Layout.Tests/Incremental/LayoutSessionTests` — text, attribute,
  structural insert/remove, anonymous re-wrap, clean-sibling reuse, no-op frame,
  and the in-session dual-run check.
- `Starling.Engine.Tests/IncrementalRelayoutTests` — end-to-end RelayoutPage
  incremental-vs-full match and StyleEngine reuse across frames.
- (Phase 0) `LayoutVerifierTests`, the `LiveLayoutSignalTests` 0c test.
