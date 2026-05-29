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

**Known accepted gap.** Author CSS that uses an attribute selector (such as
`[role="button"] { ... }`) plus a script that writes that attribute will miss
a recompute until the next layout-relevant change. The spec-correct fix is
selector-aware invalidation, which is a tracked follow-up. The gap is
documented in code at both the live loop and `Document.IsLayoutRelevantAttribute`.

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
