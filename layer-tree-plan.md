Here's a complete, hand-off-ready plan for the layer-tree fix. It's scoped to **Stage 1** only (make the existing compositor path carry live animation on the current backend). GPU present/readback and a full paint compiler are explicitly out.

I've written it to match this repo's work-package conventions (AGENTS.md): each package has an id, `depends_on`, the files to touch with line anchors, an approach, acceptance criteria, and required tests. Every package ships a test — that's non-negotiable per AGENTS.md.

---

# Plan: Live layer-tree compositing for animation frames

## Objective

Stop the live loop from re-rastering the whole viewport on every animation frame. Raster each layer's content **once**, cache it, and on later frames re-blit cached layers with fresh transform/opacity. Re-raster only the layers whose content actually changed.

## Outcome (what "done" looks like)

On the Animations demo, a steady-state frame should: blit the static base layer cheaply, re-blit the six transform/opacity animation layers from cache, and re-raster only the small hue box and the script-mutated status line. **No full-viewport raster per frame.** Target: the demo runs smooth at scale 1.0 and stays in single-digit-to-low-tens milliseconds per frame at scale 2.0, with per-frame backend raster calls dropping from "1 full viewport" to "N small changed layers."

## Background (root causes this plan removes)

From the investigation, on the live path today:

1. The compositor fast-path is gated off whenever a frame relayouts — `_animationOnlyFrame = animating && !needsRelayout` (`WebviewPanel.cs:1218`), and the demo relayouts every frame because of a status-text write, so `RenderViaLayerTree` never runs (`WebviewPanel.cs:1697`).
2. Layer promotion reads **static** style — `StackingContextResolver.Resolve` (`StackingContextResolver.cs:40`, transform check `:118`, opacity `:110`). Elements that only _animate_ transform/opacity are never promoted.
3. Every relayout wipes **all** caches — `ShowPage` → `PageRendererHost.InvalidateCache()` clears the flat cache and `LayerCacheStore` (`WebviewPanel.cs:342`, `PageRendererHost.cs:48-52`).
4. The layer cache is keyed by the global page version — `RenderViaLayerTree` passes `DisplayListVersion` (`PageRendererHost.cs:119`), which bumps on any relayout, so a change anywhere busts every layer.
5. Compositing the base layer is a per-pixel inverse-bilinear loop over the whole viewport every frame — `Compositor.BlendLayer` (`Compositor.cs:151-219`), so even a cached base layer is not cheap to composite.

## Core design (shared mental model for implementers)

The key idea is to **separate a layer's cached content from its per-frame compositing parameters.**

- **Content** = the rasterized slice (fills, text, images). Cached. Re-rastered only when the slice changes.
- **Compositing params** = transform, opacity, clip. Re-read every frame and applied at composite time. These never invalidate the content cache.

The code already half-does this: `LayerTreeBuilder.BuildLayer` reads the animated transform via `TryGetTransformMatrix(...)` into `layer.Transform` and **suppresses it from the slice** (`suppressRootTransform`, `LayerTreeBuilder.cs:82-92`), and opacity flows through `EffectiveOpacity` into `layer.Opacity`. So a transform/opacity-only animation already produces a byte-identical slice each frame. What's missing is: (a) promoting the animating element so it _gets_ its own layer, (b) a cache key that recognizes the slice is unchanged, (c) keeping that cache across relayouts, (d) taking the path at all, (e) a cheap base composite, and (f) isolating the script-mutated subtree so it doesn't drag the base layer.

Each work package below maps to one of those.

## Scope

**In:** live compositor path for the current ImageSharp backend (CPU or WebGPU), promotion of animating elements, per-layer content cache, cache persistence across in-place relayout, fast base-layer composite, isolation of script-mutated subtrees, and a benchmark to prove it.

**Out (later stages, do not touch):** GPU present / readback elimination, a GPU paint-command compiler, glyph atlas, per-container scroll on the compositor path (keep the existing flat fallback when `scrollLookup` is non-null), and intra-layer damage rects beyond what LTF-06 needs.

---

## Work packages

### `wp:LTF-00` — Compositor replay harness + baseline (DO FIRST)

- **Goal:** be able to measure raster-vs-blit per frame before changing engine code.
- **Depends on:** none.
- **Files:** `bench/Starling.Bench/Replay/FrameReplayHarness.cs` (today it calls `_backend.Render` flat at `:181` — add a path that drives `PageRendererHost.RenderViaLayerTree` / `Compositor`), `bench/Starling.Bench/Replay/ReplayScenarios.cs`, `ReplayResult.cs`, `ReplayOptions.cs` (already has `Incremental`, `RunRaster` — add `Scale` and `Composite`).
- **Approach:** add three scenarios — transform-only animation, transform animation **plus** a per-frame text mutation (the demo), and a background-color animation. Add a per-frame counter for "layers rastered" vs "layers blitted from cache" (the `Compositor` already calls `_backend.Render` for raster and `CopyOut` for hits — count via a wrapping `IPaintBackend` or the existing `IDiagnostics` counters). Add `scale: 1.0` and `2.0` runs, and incremental scope (the two configs flagged as missing).
- **Acceptance:** harness reports, per scenario and scale, frame ms, raster ms, composite ms, layers-rastered/frame, and alloc/frame. Baseline captured.
- **Tests:** harness self-test asserting deterministic layer/raster counts on a fixed scenario.

### `wp:LTF-01` — Promote actively-animating elements to their own layer

- **Goal:** an element with an active animation or transition becomes a compositor layer root even with no static transform/opacity. Transform/opacity apply at composite; any other animated paint property re-rasters that element's own (small) slice.
- **Depends on:** `LTF-00` (soft, for measurement).
- **Files:** `src/Starling.Paint/Compositor/LayerTreeBuilder.cs` (`IsLayerRoot` `:52`, ctor `:39`), `src/Starling.Gui/PageRendererHost.cs` (`RenderViaLayerTree` `:103` — thread a predicate), `src/Starling.Gui/Controls/WebviewPanel.cs` (pass predicate from the page), `src/Starling.Engine/Engine.cs` / `BrowserSession.cs` (expose "is element actively animating, and which of its props are composited" using `page.Style.AnimationEngine.ActiveProperties` / `TransitionEngine`, near `HasActiveAnimations` `:960`).
- **Approach:** inject `Func<Element, bool> isAnimatingLayerRoot` into `LayerTreeBuilder`, evaluated **per frame** (promotion must track animation start/stop, which layout-time `Hints` cannot). `IsLayerRoot(box)` becomes `box.Hints != None || isAnimatingLayerRoot(box.Element)`. Do **not** bake this into `StackingContextResolver` (that runs at layout, not per frame).
- **Acceptance:** a box animating `transform` with no static transform produces its own `CompositorLayer` whose slice excludes the transform and whose `layer.Transform` carries it; a box animating `background-color` produces its own layer whose slice re-rasters per frame.
- **Tests:** unit — build the layer tree for a doc with an active transform animation, assert the element is a layer root and the transform is composite-time not in the slice; golden — a transform animation rendered via the layer tree is byte-identical to the flat render at the same clock.
- **Risk:** promotion changes stacking. Animating transform/opacity already establishes a stacking context per spec, so those are safe. For non-stacking-context props (background-color), restrict v1 promotion to simple isolated boxes (no positioned/overlapping descendants); otherwise leave the element in its parent layer (parent re-rasters — degrades to today, documented).

### `wp:LTF-02` — Per-layer content-hash cache key

- **Goal:** key each layer's raster cache by a stable hash of its slice content (which excludes the composite-time transform/opacity), so transform/opacity-only frames hit cache and unrelated relayouts don't bust it.
- **Depends on:** none (pairs with LTF-01 to deliver the win).
- **Files:** `src/Starling.Paint/Compositor/Compositor.cs` (`RenderLayerBitmap` `:107` — compute and pass the hash), `src/Starling.Paint/Cache/PictureCache.cs` (`TryServe`/`Reset` — widen the version key to 64-bit or add an overload), `LayerTreeBuilder.cs` if the slice needs exposing for hashing.
- **Approach:** compute a 64-bit hash (FNV-1a or xxHash) over the slice's display items — bounds, colors, text + font + shaped-run identity, image refs — and use it as the cache version instead of `DisplayListVersion`. The slice walk already happens during build, so this is cheap relative to raster.
- **Acceptance:** two consecutive composites where only the transform changed → **0** backend raster calls for that layer; a content change (color/text/size) → exactly **1** re-raster.
- **Tests:** unit with a counting backend asserting raster-call counts across frames; golden equality (cached output identical to a from-scratch render).
- **Risk:** hash collision shows stale pixels. Use 64-bit, and pair with the element's incremental-layout mutation counter (`Document.RecordLayoutMutation` already records `LayoutChangeKind`) as a belt-and-suspenders component of the key.

### `wp:LTF-03` — Persist layer caches across in-place relayout

- **Goal:** stop wiping `LayerCacheStore` on every relayout. Clear it only on navigation (Document change).
- **Depends on:** `LTF-02` (the content-hash key makes persistence correct).
- **Files:** `src/Starling.Gui/PageRendererHost.cs` (split `InvalidateCache` `:48` into a flat-only invalidation and a navigation reset that also clears `_layerCaches`), `src/Starling.Gui/Controls/WebviewPanel.cs` (`ShowPage` `:314` — call the navigation reset only when `page.Document` differs from the previous; the panel already tracks document identity via `_scrollOffsetsDocument` `:357`).
- **Acceptance:** an in-place relayout that doesn't change layer L's content → L served from cache (0 re-raster); navigation → all layer caches cleared.
- **Tests:** integration — relayout the same document, assert the unchanged layer's cache is retained; navigate, assert cleared.

### `wp:LTF-04` — Take the compositor path on live frames even after a relayout

- **Goal:** remove the `_animationOnlyFrame` gate. Take `RenderViaLayerTree` whenever the page has compositor layers (static or live-animating) and `scrollLookup is null`, regardless of whether something relayouted this frame.
- **Depends on:** `LTF-01`, `LTF-02`, `LTF-03`.
- **Files:** `src/Starling.Gui/Controls/WebviewPanel.cs` (`LiveTick` `:1218-1220`, `RenderPageBitmap` `useLayerTree` `:1697`, `PageHasCompositorLayers` `:1764`).
- **Approach:** `useLayerTree = scrollLookup is null && (PageHasStaticCompositorLayers() || _hasActiveAnimations(page))`. Drop the `!needsRelayout` requirement. The incremental relayout still runs to update geometry; the layer tree is rebuilt from the new box tree and the content-hash caches decide what re-rasters. Keep the flat path as fallback for scrollable pages and pages with no layers.
- **Acceptance:** the demo takes the compositor path every frame; only the changed layers re-raster.
- **Tests:** integration — drive `PageRendererHost` over N frames with a transform animation + a per-frame text mutation; assert per-frame raster-layer count is small and roughly constant (not "1 full viewport").
- **Risk:** this is the integration point — guard behind the existing live-loop entry so non-animating navigation is unaffected; verify goldens for static pages are unchanged.

### `wp:LTF-05` — Fast composite blit for axis-aligned, opacity-1 layers

- **Goal:** make compositing the big static base layer cheap.
- **Depends on:** none (independent; most visible after LTF-04).
- **Files:** `src/Starling.Paint/Compositor/Compositor.cs` (`BlendLayer` `:151`).
- **Approach:** when the effective transform is identity or integer translation, opacity is 1, and the clip is axis-aligned, take a rectangular blit path — `Array.Copy` rows when source pixels are opaque, straight per-pixel alpha-over otherwise — skipping the matrix inverse and bilinear sampling. Keep the general path for rotated/scaled layers.
- **Acceptance:** identity-transform layer composite is **byte-identical** to the general path; measurably faster in the LTF-00 harness.
- **Tests:** golden — fast path vs general path byte-identical on a representative layer; micro-timing recorded by LTF-00.

### `wp:LTF-06` — Isolate the script-mutated subtree (required for the demo)

- **Goal:** a subtree that a script mutates each frame (the demo's status line) gets its own small layer, so its repaint does not re-raster the base layer.
- **Depends on:** `LTF-01`, `LTF-02`, `LTF-04`.
- **Files:** `src/Starling.Paint/Compositor/LayerTreeBuilder.cs` (extend the `isAnimatingLayerRoot` predicate from LTF-01 to also return true for elements flagged "recently mutated"), `src/Starling.Dom`/incremental layout (surface a per-element "mutated in the last frame" flag from the existing `LayoutChangeKind` records), `WebviewPanel.cs` wiring.
- **Approach (v1, recommended):** promote elements whose subtree received a text/attribute mutation this frame. They become small isolated layers; the base layer's slice hash stays stable and serves from cache. Same paint-order caveat as LTF-01 (restrict to simple isolated boxes; otherwise leave in parent).
- **Alternatives (note for the implementer, do not build in v1):** intra-layer damage rects (re-raster only the changed rectangle of a layer, blit the rest — mirrors the flat cache's existing strip logic in `CachedPageRenderer`), or tiling the base layer. Both are more general and more work; v1 promotion is enough for the demo.
- **Acceptance:** with a per-frame text mutation, the base layer is served from cache (0 re-raster) and only the mutated subtree's layer re-rasters.
- **Tests:** integration — per-frame text mutation scenario; assert base layer raster count is 0 after warmup and only the status layer re-rasters.
- **Risk:** promotion churn (an element promoted/unpromoted as mutations come and go). Add light hysteresis (keep a recently-mutated element promoted for a few frames) and document it.

### `wp:LTF-07` — Validation + docs

- **Goal:** prove the win and update the stale design note.
- **Depends on:** `LTF-01..06`.
- **Files:** `bench/results/<date>/` (before/after from LTF-00 at scale 1 and 2), `docs/animation-relayout-perf.md` (it predates incremental layout and the compositor — rewrite the "fix directions" and measurements to match what shipped), `tasks/INDEX.md`.
- **Acceptance:** before/after table recorded; raster-layer count per frame on the demo scenario drops to the changed-layers-only count; CI green (`dotnet build && dotnet test`).
- **Tests:** the LTF-00 self-tests plus the goldens from LTF-01/02/05 stay green.

---

## Sequencing & parallelization

- **First:** `LTF-00` (baseline — you cannot claim the win without it).
- **Parallel after LTF-00:** `LTF-01`, `LTF-02`, `LTF-05` are independent (promotion, cache key, blit).
- **Then:** `LTF-03` (needs LTF-02), then `LTF-04` (integrates 01+02+03), then `LTF-06` (needs 01+04).
- **Last:** `LTF-07`.

These touch shared GUI files (`WebviewPanel.cs`, `PageRendererHost.cs`) — per AGENTS.md, call that out in each package's handoff log so concurrent agents rebase cleanly.

## Cross-cutting requirements

- **Test-first and every fix ships a test** (AGENTS.md). Correctness is proven by **byte-identical goldens** between the layer-tree path and the flat path at a fixed clock — the compositor must not change output, only when/what it rasters.
- **No new native dependency** and follow the C# performance policy (AGENTS.md) — the per-frame hot paths (slice hash, blit) should avoid per-pixel allocations and LINQ; reuse buffers.
- **Measure before/after** on the LTF-00 harness at scale 1.0 and 2.0.

## Decisions already made (so agents don't re-litigate)

- Promotion is evaluated **per frame in `LayerTreeBuilder`**, not baked into layout-time `Hints`, because animation state changes faster than layout runs.
- The layer cache key is a **content hash of the slice**, not the global page version.
- Layer caches **persist across in-place relayout**, cleared only on navigation.
- The compositor path runs **even on relayout frames** for pages with layers; the content hash decides re-raster.
- GPU present/readback, glyph atlas, and per-container scroll on the compositor path are **out of scope** for this plan.

## Open questions for the implementer (resolve in the package, don't block)

- Exact hash function and whether to pair it with the incremental mutation counter (LTF-02).
- Hysteresis window for mutation-driven promotion (LTF-06).
- Whether to widen `PictureCache`'s version field to 64-bit or add a parallel keyed overload (LTF-02).

## Exit criteria

On the demo scenario in the LTF-00 harness, after warmup: backend raster calls per frame ≤ (number of genuinely changed layers, ~2–3) instead of 1 full-viewport raster; base layer served from cache; frame time smooth at scale 1.0 and a large improvement at scale 2.0; all goldens byte-identical to the flat path.
