---
id: wp:M5-css-08-rAF-event-loop
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:41:33Z"
branch: "main"
depends_on: []
subsystem: Starling.Loop
plan_refs:
  - browser-plan/10_WEB_APIS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-08-rAF-event-loop — requestAnimationFrame in the event loop

## Goal

Add `requestAnimationFrame` / `cancelAnimationFrame` to `WebEventLoop` and the
JS Window binding, with the per-frame timestamp + ordering semantics the HTML
spec requires.

## Inputs

- `Tessera.Loop.WebEventLoop` (microtasks + timers today).
- `Tessera.Bindings.WindowBinding`.
- HTML spec §"event loop processing model" step 11 (run-animation-frame-callbacks).

## Outputs

- `WebEventLoop` gains:
  - `int RequestAnimationFrame(Action<double> callback)` returning a handle.
  - `bool CancelAnimationFrame(int handle)`.
  - `void RunFrame(long nowMs)` (or `AdvanceToFrame(long nowMs)`): sets the
    monotonic clock, drains microtasks, fires due timers, drains the rAF
    queue (snapshotted; callbacks scheduled *during* drain run next frame),
    drains microtasks again.
  - All rAF callbacks in one frame see the same `nowMs` value.
- `WindowBinding.requestAnimationFrame(JsFunction)` /
  `WindowBinding.cancelAnimationFrame(JsNumber)` route through the loop.
- Tests:
  - One rAF fires once on next frame.
  - Nested rAF schedules for the *following* frame, not the current.
  - Cancel before fire removes the callback.
  - All callbacks in a frame observe the same timestamp.

## Acceptance

- Existing `WebEventLoop` tests stay green.
- Window binding test calls `requestAnimationFrame(cb)`, then
  `loop.RunFrame(16)`, asserts `cb` ran with timestamp 16.

## Notes

- No engine wiring here — `TesseraEngine.RenderFrame` is wp:M5-css-10.
- Keep `AdvanceBy` for back-compat; have it call `RunFrame(_nowMs + n)` for
  the rAF phase to fire.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7).
- 2026-05-19T15:41:33Z — claimed by agent-copilot-claude-opus-4.7, working on main
- 2026-05-19T19:00Z — completed.
  * `WebEventLoop` gained `RequestAnimationFrame`/`CancelAnimationFrame`/
    `RunFrame(long nowMs)`. Frame ordering: microtasks → due timers
    (each followed by microtask drain) → snapshotted rAF queue (each
    followed by microtask drain) → final microtask drain. All rAF
    callbacks in a single frame see the same nowMs. Nested rAFs land
    in the freshly-emptied queue, so they fire on the *next* frame.
  * `AdvanceBy(n)` now routes through `RunFrame(_nowMs + n)` for
    back-compat — existing timer-only tests stay green and rAFs now
    fire as part of time advancement.
  * `RunFrame` rejects backwards time.
  * New `AnimationFrameBinding.Install(runtime, loop)` mirrors
    `TimersBinding`: defines `requestAnimationFrame` /
    `cancelAnimationFrame` on the global. Non-callable handler throws
    TypeError; callback errors route through `ConsoleSink` at Error
    level (does not stop the frame).
  * 8 new `AnimationFrameTests` (loop layer), 5 new
    `AnimationFrameBindingTests` (binding layer). Full sln test sweep
    shows only the two pre-existing failures
    (`NativeImageDecoderTests.DecodesPngCornerPixels`,
    `DisplayListBuilderTests.Underlined_link_emits_text_and_underline_fill`)
    — no regressions.
  * Engine wiring deferred to wp:M5-css-10 (now unblocked).
