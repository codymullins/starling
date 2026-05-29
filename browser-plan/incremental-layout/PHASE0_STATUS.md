# Incremental layout — Phase 0 status

This tracks the "measurement and safety net" phase of the incremental-layout
plan. Phase 0 is the prerequisite that blocks the rest of the work.

## What landed

### 0b — fixed the false `RenderFrame` doc comment

`Engine.RenderFrame` claimed its cascade cache "short-circuits the static
side". That was not true. The box tree is rebuilt from scratch every call,
with a new per-build cascade cache. The comment now says so, and points at
incremental fragment reuse as the planned replacement.

### 0c — the live loop relayouts on the narrow signal

The live loop used to relayout whenever the DOM changed at all
(`Document.MutationVersion`). It now relayouts only when a change a built-in
style or layout pass cares about lands (`Document.LayoutInvalidationVersion`).

A `requestAnimationFrame` callback that writes only `data-*`, `aria-*`, or
`js*` framework attributes no longer forces a full reflow every frame. Text
writes and layout-relevant attribute writes still relayout, so the Animations
demo's per-frame status text keeps updating.

The change lives in `WebviewPanel`. It tracks the document's
`LayoutInvalidationVersion` from the last layout and compares it after each
pump. `ShowPage` re-baselines the version on every page swap.

**The attribute-selector gap is now fixed (plan §7 — selector-aware
invalidation).** Author CSS that uses an attribute selector (such as
`[data-state="open"] { ... }`) plus a script that writes that attribute used to
miss a recompute, because the static heuristic in
`Document.IsLayoutRelevantAttribute` treats `data-*`/`aria-*`/`role` as
cosmetic. The layout pass now records which attributes the page's stylesheets
actually select on (`StyleEngine.ReferencedAttributeNames` →
`Document.StyleReferencedAttributes`), and the attribute-mutation path checks
that set too (`Document.IsAttributeLayoutRelevant`). So a write to a
selector-referenced attribute invalidates layout while genuinely-cosmetic
`data-*` writes still don't.

### 0d — the verification harness shell

`LayoutVerifier` (in `Starling.Layout`) walks two layout outputs in lockstep
and reports the first place their geometry diverges. It returns the box path,
the DOM element, the field that differs (such as `Frame.X` or `ChildCount`),
and the expected and actual values. It stops at the first difference so the
report points at the root cause, not every box knocked out of place by it.

`LayoutEngine` runs it when `VerifyLayout` is set (default: the
`STARLING_LAYOUT_VERIFY=1` env switch). Today both sides are a full rebuild,
so this is an identity check that proves the harness itself works. When the
incremental path lands, the produced output becomes the incremental result and
the harness keeps checking it against a full rebuild.

Tests in `Starling.Layout.Tests/Verification/`:

- two full rebuilds of the same document are identical (the identity check),
- an injected fault names the right element and field (a 5px shove, a dropped
  child box, differing text, a different viewport width),
- the engine's dual run records the `layout.verify.ok` counter and logs no
  error on a deterministic page,
- every site under `testdata/sites/` lays out deterministically.

## Build and test state

The whole solution builds (a Six Labors license is now present in the sandbox
as an untracked, git-ignored `sixlabors.lic`). All work landed here is verified:

- 219 layout tests pass, including the 8 new `LayoutVerifier` tests.
- The full `Starling.Gui.Headless.Tests` suite passes, including the new 0c
  live-loop test.
- The rest of the solution is green apart from two pre-existing failures that
  are environmental, not from this work:
  - `Starling.Codecs.Tests` — `libturbojpeg.so.0` is not installed here.
  - `Starling.Engine.Tests` one golden snapshot (`nginx.org`) — the golden was
    captured with the WebGPU paint backend, and this sandbox has no GPU adapter,
    so tests run on the CPU `imagesharp` backend and the pixels differ. The same
    test fails with the identical SSIM (0.855) at the base commit before any of
    this work, which confirms it is not a regression. Run the headless suites
    with `STARLING_PAINT_BACKEND=imagesharp` here.

## What is still deferred

### 0a — the steady-state trace

Phase 0a wants a captured trace from the running app with the Aspire dashboard,
so the three forking numbers can be read and Phase 4 gated yes or no:

- `paint.style_cascade` vs `paint.layout`,
- the shaper sub-span as a fraction of `paint.layout`,
- `show_page.hit_index` as a fraction of the frame,
- plus garbage-collection events against frame time.

This needs the live GUI animation loop and a running Aspire dashboard. The GUI
now compiles, but the default WebGPU paint backend cannot start here — the
sandbox has no GPU adapter — and the Aspire dashboard is not running. So the
steady-state trace is deferred to a GPU-capable environment with the AppHost up.

The design forks 0a was meant to settle are already resolved in the plan: the
cross-frame cascade cache stays out, the shaped-text cache stays out, and
Phase 4 stays gated until the hit-index number is measured. So 0a is
confirmatory, not blocking, for Phases 1 through 3.

## Phase 1 readiness — blast radius and a green-at-each-step strategy

Phase 1 (the input/output split) is the largest single piece in the plan. The
0d harness is the safety net for it: every step can be checked full-vs-full so
behavior stays identical. Measured blast radius on this branch:

- Formatting passes to rewrite (Block, Inline, Position, Flex, Grid, Tree):
  about 4,560 lines.
- Sites that write box output (`Frame`, `Margin`, `Padding`, `Border`,
  `Hints`): about 40, all inside `Starling.Layout`.
- Files that read box output: about 28, spanning `Starling.Layout`,
  `Starling.Paint`, and `Starling.Gui`.

Because the split touches every formatting pass and every consumer at once, it
cannot land in one behavior-neutral commit while staying green. A strangler-fig
order that keeps every step green:

1. Add the immutable `Fragment` hierarchy next to `Box` (additive, no readers
   yet). Mirror `BoxKind`: `BlockFragment`, `InlineFragment`, `TextNodeFragment`
   (the existing `TextFragment` struct stays the per-line glyph-run leaf),
   `ImageFragment`, `AnonymousFragment`. Use an immutable spine with reference
   identity — never key a cache on a fragment's value equality. A fragment holds
   its own content-space size and edges and does not store its offset in the
   parent.
2. Add the persistent input layer (`LayoutInput`) and the element-to-input map
   the builder never had, alongside `Box`, still unused.
3. Have `LayoutDocument` build the input tree and emit a `Fragment` tree, with a
   thin shim that also fills the old `Box` outputs so consumers keep working.
   The 0d harness compares the two.
4. Migrate consumers (Painter display list, `PageRendererHost`, `BoxHitTester`,
   the find index, `LaidOutPage`) to read fragments one at a time, each behind
   a passing test run.
5. Delete the box-output fields once no reader remains.

Only after this is Phase 2 (constraint-space cache and the incremental win)
worth starting. The note's reuse rule keys on `(input, constraint-space)`, so
the input layer and the fragment cache slot from steps 2 and 1 are its
foundation.
