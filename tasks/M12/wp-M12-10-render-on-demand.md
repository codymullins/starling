---
id: wp:M12-10-render-on-demand
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on: []
blocks:
  - wp:M12-07-compositor-thread
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/11_AVALONIA_SHELL.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-10-render-on-demand — Demand-driven render loop + frame pacing

## Goal

Borrow the game-engine pattern (Frostbite "demand-driven rendering", Unity
`OnDemandRendering`, iOS `CADisplayLink.preferredFrameRateRange`): stop
running the paint/composite pipeline at a fixed cadence and instead drive
it from a "dirty" condition. When the page is static and the cursor isn't
moving, the engine should idle the GPU and produce 0–10 frames/sec. The
instant input or an animation arrives, it ramps to the display's native
refresh and stays there until activity ceases plus a short settle window.

This is the single largest power-efficiency win available to the browser,
typically 5–10× idle wattage reduction. It is also a prerequisite for the
compositor-thread split (`wp:M12-07`) to actually save anything — a
compositor that runs at 120Hz against a static page is no better than the
current code, just on a different thread.

## Inputs

- Current frame loop: `Starling.Engine.FrameLoop` / Avalonia
  `CompositionTarget.Rendering`. The exact wiring is in
  `src/Starling.Engine/` and `src/Starling.Gui/`.
- M5's animation compositor (`wp:M5-css-09-animation-compositor`) — it
  already knows whether any animation is active; we can subscribe.
- Avalonia exposes the display refresh rate via `Screen.Scaling`/
  `IPlatformRenderInterface`; if not available, default to 60 Hz and
  emit a counter `paint.render.refresh_rate_unknown`.

## Outputs

- `Starling.Paint.RenderScheduler`:
  - Holds a `_dirty` flag and a `CancellationTokenSource` per scheduled
    frame.
  - `RequestFrame(FrameReason reason)` — called by input handlers,
    animation ticks, mutation observers, network image arrival, etc.
    Sets `_dirty` and wakes the loop. `FrameReason` is a flag enum
    (`Input`, `Animation`, `Mutation`, `Network`, `Composite`, `Resize`)
    used purely for counters.
  - Internal loop:
    1. Wait on a `ManualResetEventSlim` until `_dirty` set.
    2. Compute the next vsync time via the display refresh rate; sleep
       until ~2ms before it (configurable jitter buffer).
    3. Run the frame; clear `_dirty` *before* running so reentrant requests
       during the frame schedule the next one.
    4. If no `_dirty` flag is set after the frame, drop into idle mode.
  - Idle mode: cap rate at `IdleHz` (default 10) — handles cursor
    blink, async network image decode finishing, etc.
  - Active mode: cap rate at `ActiveHz` (display native, e.g. 120).
  - "Settle window": after the last input/animation, stay in active mode
    for `SettleMs` (default 250ms) before downshifting. Avoids flapping
    between idle/active when input arrives in bursts.
- A small `IDisplayRefreshSource` abstraction so the Avalonia shell can
  provide the actual refresh rate; tests provide a fake.
- Counters:
  - `paint.render.frame.{input,animation,mutation,network,composite,resize}`
    — per-reason frame count.
  - `paint.render.idle_seconds_total` — wall time spent in idle mode.
  - `paint.render.active_seconds_total`.
  - `paint.render.coalesced` — frames requested while another was already
    pending.
  - `paint.render.skipped_no_change` — wake-ups where `_dirty` was already
    cleared by another request.

## Acceptance

- A test loads a static page, lets the scheduler settle, then asserts that
  over a 1-second window fewer than `IdleHz + 1` frames are produced.
- A test fires `RequestFrame(Input)` 100 times in 16ms (simulated trackpad
  burst); asserts exactly one frame is produced for that vsync and 99 are
  coalesced.
- A test starts a CSS animation, observes active-rate frames for the
  animation duration + settle window, then asserts return to idle rate.
- A test injects a fake `IDisplayRefreshSource` reporting 120 Hz and
  asserts the scheduler targets ~8.33ms frame interval (not 16.67).
- `dotnet build && dotnet test` green.

## Notes

- This WP changes the *contract* between mutation/input sources and the
  paint pipeline: anything that wants pixels updated must now call
  `RequestFrame`. Audit and update:
  - `WebviewPanel` input handlers (scroll, click, key).
  - `Starling.Css` animation compositor ticks.
  - `Starling.Net` image arrival callbacks.
  - `Starling.Dom` mutation observers.
  - `Starling.Layout` reflow completion.
  Document the contract in `browser-plan/08_FONTS_PAINT.md`.
- Present-mode coupling: when in idle mode, request `FIFO` (vsync,
  blocks); when in active mode, request `Mailbox` (drops stale frames,
  never blocks). This switching happens in the WebGPU backend; expose a
  `RenderScheduler.Mode` property the backend can read.
- Battery-state awareness can come later: on a future WP, drop `ActiveHz`
  to 60 even on 120Hz displays when on battery. Out of scope here, but
  the `ActiveHz` field should already be a settable property.
- Why is this a prerequisite for `wp:M12-07`? Because the compositor
  thread is only a win if it stops running when nothing changes. Without
  this scheduler, the compositor thread would run at 120Hz against a
  static page, burning a CPU core for no benefit. With this scheduler,
  the compositor thread blocks on the same `_dirty` event and sleeps
  cleanly between frames.
- Reference: Glenn Fiedler "Fix your timestep" (the inverse — variable
  render rate driving fixed logic); Frostbite "Render-on-demand" GDC
  talks; iOS CADisplayLink `preferredFrameRateRange` API; Mozilla FF70
  Core Animation rework that retrofitted the same pattern.

## Handoff log

- 2026-05-19T18:11Z — created (agent-copilot-claude-opus-4.7)
