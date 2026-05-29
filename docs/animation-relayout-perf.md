# Animation lag: the full-relayout-per-frame problem

A working note for the engine issue behind the laggy Animations demo. In short:
the live frame loop has no incremental layout. Any per-frame change to the page,
even one character of text, rebuilds the whole page from scratch every frame.

## The hot path (one frame, about 60 times a second)

`WebviewPanel.LiveTick` (`src/Starling.Gui/Controls/WebviewPanel.cs`) runs once
per frame while a page is live. For the Animations demo, each tick does this:

1. `scripting.PumpFrame(...)` runs the page's `requestAnimationFrame` callbacks.
   The demo writes the status line every frame:
   `status.textContent = anim.playState + " · " + t + " ms"`
   (`testdata/sites/animations/app.js`). `t` changes every frame, so the text
   really does change.
2. The text write bumps `Document.MutationVersion`. So `PumpFrame` returns
   `mutated = true` (`src/Starling.Bindings/Backend/StarlingScriptSession.cs`,
   the `MutationVersion` before-and-after check).
3. `mutated` makes `LiveTick` call `RefreshLiveLayout`, which calls
   `Engine.RelayoutPage`, which calls `_painter.LayoutDocumentWithStyle(...)`.
   That is a full cascade and a full layout of the whole document, from scratch.
4. The result goes through `ShowPage`. It wipes the picture cache
   (`_renderer.InvalidateCache()`), rebuilds the full hit-test fragment index
   (`BoxHitTester.CollectFragments`), and rebuilds the find index.
5. The frame is drawn.

So one tiny text write turns every animation frame into a full cascade, a full
layout, a cache wipe, a full hit-test rebuild, and a draw. That is the lag.

## Current layout architecture

`LayoutDocumentWithStyle` (`src/Starling.Paint/Painter.cs`) runs in stages, and
each stage has its own span:

1. `paint.style_cascade` builds the `StyleEngine` and cascades every element.
   This also makes the new `AnimationEngine` and `TransitionEngine`.
2. `paint.layout` runs `LayoutEngineImpl.LayoutDocument`. It builds a fresh box
   tree and lays it out. Text shaping happens here, through the measurer.
3. `paint.display_list` turns the box tree into a paint list. Paint path only.
4. `paint.raster:<backend>` draws the list to pixels.

The live GUI loop runs stages 1 and 2 through `RelayoutPage`, then draws through
the box-tree renderer (`PageRendererHost`), not stages 3 and 4 of the painter.

**The box tree is never retained.** Every relayout and every frame builds a new
`BlockBox` from scratch in `LayoutEngineImpl.LayoutDocument`, called from both
`LayoutDocumentWithStyle` and the retained-engine `RenderWithStyle`. No box
survives from one frame to the next. So "reuse the unchanged part of the tree"
is not possible today. A retained, dirty-tracked box tree is the precondition
for incremental layout (fix direction 4), and it does not exist yet.

**Engine retention exists in one path, but the demo defeats it.** The headless
frame loop (`Engine.RenderFrame` calls `Painter.RenderWithStyle`) keeps one
`StyleEngine` alive across paints, so animation state is not reseeded each frame.
The live GUI loop does not use that path. When a frame mutates the DOM,
`RelayoutPage` makes a brand-new `StyleEngine`, so the animation engines are
rebuilt and must be re-imported and re-primed. The demo writes text every frame,
so it mutates every frame, so engine retention never helps it. This is why fix
direction 3 matters.

## Why one text write rebuilds everything

Four separate engine facts add up to "rebuild the whole page."

### 1. The live loop watches the coarsest possible signal

`PumpFrame` decides `mutated` from `Document.MutationVersion`. That counter
bumps on any DOM change at all: text, attributes, or structure. It is the wrong
signal to trigger a full relayout.

The engine already has a finer signal right next to it:
`Document.LayoutInvalidationVersion` (`src/Starling.Dom/Document.cs`). It only
moves for changes a layout pass would care about. Attribute writes that no
built-in style targets (`data-*`, `aria-*`, Google's `js*` framework attributes)
are filtered out by `Document.IsLayoutRelevantAttribute` and do not bump it.

The live loop never reads `LayoutInvalidationVersion`. So today, a page that
only writes `data-*` attributes in its `requestAnimationFrame` still triggers a
full relayout every frame. The layout-cache logic already knows that change does
not matter, but the live loop never asks.

### 2. There is no incremental layout

`Engine.RelayoutPage` always runs `LayoutDocumentWithStyle` over the whole
document. There is no dirty-subtree path and no "only this element changed"
path. A one-character text edit costs the same as replacing the whole DOM.

This is the core issue. Everything else is a workaround for it.

### 3. Every relayout throws away all cached style work

`BoxTreeBuilder` makes a fresh `CascadeCache` on every build
(`src/Starling.Layout/Tree/BoxTreeBuilder.cs`). The cache lives for one layout
pass. It speeds up that single pass. It does not survive across frames. So every
frame re-runs selector matching and the cascade for every element, even the
thousands that did not change.

### 4. Every relayout builds brand-new animation engines (FIXED — see "Already done")

This used to be true: `StyleEngine`'s constructor made a fresh `AnimationEngine`
and `TransitionEngine`, so a relayout rebuilt the animation state and the loop
re-imported and re-primed every frame. The animation engines now live on a
per-document `AnimationTimeline` that outlives the `StyleEngine`. See the
"Already done" section for what changed.

## What already exists to help

- `Document.LayoutInvalidationVersion` and `Document.IsLayoutRelevantAttribute`
  are a ready-made "does this change matter for layout" signal. The script-time
  layout cache uses them. The live loop does not.
- `Document.AttributeMutationCounts` is a per-attribute count of what drives
  mutations. Good for spotting which writes cause reflows.
- OpenTelemetry spans on the live loop (added with this note). `gui.live.tick`
  wraps each frame. Its children are `live.pump`, `live.relayout`,
  `live.prepare_anim`, and the inner `gui.render`. Set
  `OTEL_EXPORTER_OTLP_ENDPOINT` and watch the Aspire trace timeline. You will
  see `live.relayout` take most of each frame.

## Measurements (to fill in)

These numbers are not captured yet. A design session needs them to pick the
right fix. Capture them, then fill the table.

How to capture: run the GUI with the Aspire dashboard and set
`OTEL_EXPORTER_OTLP_ENDPOINT`. Open the Animations demo, let it settle, and read
one steady-state `gui.live.tick` span. Record each child's wall time. The frame
budget at 60 frames a second is 16.6 ms.

| Span | What it covers | Time (ms) | Share of frame |
| --- | --- | --- | --- |
| `live.pump` | run the page's `requestAnimationFrame` callbacks | TODO | TODO |
| `live.relayout` | full relayout (sum of the two below) | TODO | TODO |
| → `paint.style_cascade` | rebuild `StyleEngine`, cascade every element | TODO | TODO |
| → `paint.layout` | rebuild and lay out the box tree, shape text | TODO | TODO |
| `show_page.hit_index` | rebuild the hit-test fragment index | TODO | TODO |
| `live.prepare_anim` | re-import and re-prime animation state, tick | TODO | TODO |
| `gui.render` | draw the viewport | TODO | TODO |
| **`live.tick`** | **whole frame** | **TODO** | — |

Questions the numbers should answer:

- Inside `live.relayout`, is `paint.style_cascade` or `paint.layout` the bigger
  cost? This decides whether direction 2 (cache the cascade) or direction 4
  (incremental layout) pays off first.
- How many elements are in the demo's box tree? Element count sets the scale of
  the rebuild.
- How much of `paint.layout` is text shaping? The status line reshapes every
  frame.
- Does `show_page.hit_index` matter, or is it noise next to the relayout?

## Fix directions, cheapest first

1. **Narrow the live-loop signal. Small and safe, partial win.**
   Have the live loop relayout on `LayoutInvalidationVersion`, not
   `MutationVersion`. A text edit still bumps both, so this does not fix the
   demo. But it stops attribute-only and metadata-only writes from forcing
   reflows, which is the common case on real pages. Watch the correctness caveat
   in the remarks on `IsLayoutRelevantAttribute`: a page whose author CSS uses an
   attribute selector and then writes that attribute would miss a recompute. The
   spec-correct version is selector-aware invalidation.

2. **Let cascade results survive across frames. Medium win.**
   Make the `CascadeCache`, or an equivalent per-element computed-style cache,
   persist across relayouts and invalidate per element on change. This cuts the
   cascade cost of a relayout from "whole tree" to "changed elements."

3. **Keep the animation engines alive across relayouts. Medium win. DONE.**
   Split `AnimationEngine` and `TransitionEngine` lifetime from `StyleEngine`
   lifetime so a relayout does not rebuild them. This removes the per-frame
   re-import and re-prime. The `ScriptAnimationStore` indirection is then no
   longer needed for the live path. See "Already done" below.

4. **Incremental layout. The real fix, large.**
   Mark only the changed subtree dirty and re-lay-out just that, reusing the
   rest of the box tree. This is what removes the lag for the demo's text write
   and for real pages. It is the big lever and the most work. Everything above
   is a stopgap until this lands.

5. **Split draw from layout for animation-only frames. Separate win.**
   The picture cache is busted every animation frame because the cache key
   includes the animation clock (`pageVersion = DisplayListVersion + animClockMs`
   in `WebviewPanel.RenderPageBitmap`). Even with incremental layout, an animated
   transform or opacity still redraws the whole viewport. A composited layer for
   animated transform and opacity would let most frames skip the draw. Lower
   priority than 1 through 4, but it caps the floor cost.

## Open decisions

The choices to argue at the whiteboard.

1. **Retained vs rebuilt box tree.** Incremental layout needs a box tree that
   survives frames with per-node dirty flags. That is a big change to
   `LayoutEngineImpl`. Is it in scope now, or do we ship the cheaper signal and
   cache fixes first?
2. **Where does invalidation state live?** On the DOM node, on the box, or on
   the computed style? Each choice changes who marks a thing dirty and who reads
   the flag.
3. **What should `PumpFrame` return?** Today it returns one bool, `mutated`. It
   cannot say what changed or whether layout is needed. A richer return, such as
   "layout-relevant?" or "which subtree?", is what lets the loop skip work.
4. **How is a cross-frame style cache keyed and invalidated?** Per element by
   mutation version? Per element by a dirty bit set on change?
5. **Hit-test index and picture cache.** `CollectFragments` and `InvalidateCache`
   run every frame. Can the hit-test index update per change instead of
   rebuilding whole? Can the picture cache survive an animation frame, or do we
   need composited layers (direction 5) first?
6. **Signal correctness.** Direction 1 uses `LayoutInvalidationVersion`, which
   can miss an attribute-selector recompute. Do we accept that gap, or build
   selector-aware invalidation now?

## Already done

`LiveTick` now draws once per frame instead of twice. It used to draw inside
`ShowPage` with a stale animation clock, then again after
`PrepareAnimationFrame`. The fix added a `deferRender` flag to `ShowPage` and
`RefreshLiveLayout`, so the single draw happens after the clock is advanced.
That halved the draw work per frame. It left the full relayout in place, which
is what this note is about.

**The animation engines now survive relayouts (fix direction 3).** The
`AnimationEngine`, `TransitionEngine`, and `AnimationCompositor` moved off the
`StyleEngine` and onto a new per-document `AnimationTimeline`
(`src/Starling.Css/Animations/AnimationTimeline.cs`). The `Painter` keeps one
timeline per `Document` (weak-keyed) and hands the same one to every
`StyleEngine` it builds for that document (`Painter.GetAnimationTimeline`), so a
relayout reuses the live engines instead of making new ones. Three follow-on
cleanups fell out of this:

- Web Animations API (`element.animate`) animations register straight into the
  persistent `AnimationEngine`. The `ScriptAnimationStore` re-import indirection
  is gone — `EngineAnimationHost` now holds the live `AnimationInstance` per
  handle id and the store file was deleted.
- `Engine.ImportScriptAnimations` is gone. `PrepareAnimationFrame` no longer
  re-imports anything per frame.
- Declarative `@keyframes` priming is now keyed on the laid-out box-tree root
  (`Engine._primedTrees`), not the engine. The same tree primes once across all
  its frames, and a relayout re-primes the fresh tree so animation-* changes are
  picked up. `OnAnimationsCascaded` matches by name, so playback is preserved.
  The keyframe registry is the one piece still rebuilt per layout: the
  `StyleEngine` constructor clears it on the reused timeline before re-adding the
  current sheets, which leaves active playback untouched.

This removes the per-frame re-import and re-prime, but the full relayout itself
(fix directions 1, 2, 4) is still the dominant per-frame cost on a mutating
page.
