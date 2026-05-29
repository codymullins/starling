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

## Phase 5 — composited layers for animation-only frames: separate track

The plan files Phase 5 as a separate, lower-priority track that addresses DRAW
cost, not LAYOUT cost. It is not implemented here, and the reason matters for
understanding the end-to-end picture.

Even with incremental layout, the live GUI still repaints the whole viewport
each animated frame: the picture-cache key folds the animation clock
(`pageVersion = DisplayListVersion + animClockMs` in `RenderPageBitmap`), and
each relaid page gets a fresh `DisplayListVersion`, so every frame is a cache
miss. So the incremental-layout win removes the per-frame *relayout*, but the
per-frame *repaint* remains until Phase 5.

Concrete approach when this track is picked up (on a GPU-capable host so it can
be verified): for an animation that touches only `transform`/`opacity`, promote
the element to a compositor layer (the substrate exists — `LayerHint` is already
populated by `StackingContextResolver`, and the `Compositor` caches per layer),
keep the animated property *out* of the picture-cache key, and apply it at
composite time so a no-layout-change frame serves the cached layer and skips the
viewport redraw. Acceptance: an animated `transform`/`opacity` frame with no
layout change serves the layer from cache and skips the redraw.

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
