---
id: wp:M5-css-10-engine-frame-loop
milestone: M5
status: "claimed"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:50:55Z"
branch: "main"
depends_on:
  - wp:M5-css-08-rAF-event-loop
  - wp:M5-css-09-animation-compositor
subsystem: Starling.Engine
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/11_AVALONIA_SHELL.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-10-engine-frame-loop — Drive rAF + restyle + repaint from the engine

## Goal

Stitch the rAF-aware event loop and the animation compositor into
`TesseraEngine` so a hosted page animates frame-by-frame and the headless
renderer can rasterize a chosen frame.

## Inputs

- `WebEventLoop.RunFrame(nowMs)` (wp:M5-css-08).
- `StyleEngine.Compute(Element, nowMs)` (wp:M5-css-09).
- Existing `TesseraEngine.RenderAsync` one-shot pipeline.

## Outputs

- `TesseraEngine.RenderFrame(LaidOutPage, long nowMs)`:
  - Calls `loop.RunFrame(nowMs)`.
  - Re-runs `StyleEngine.Compute(.., nowMs)` for elements whose effective
    values may have changed (initially: re-cascade everything; optimize later).
  - Re-runs layout + paint (damage-rect optimization is a follow-up).
  - Returns a `RenderedBitmap`.
- Headless renderer flag: `--frames N --frame-step Mms` emits N PNGs
  `frame0000.png` … `frameNNNN.png`.
- Avalonia shell (separate sub-task if needed): subscribe to
  `CompositionTarget.Rendering`; call `RenderFrame((long)e.RenderingTime.TotalMilliseconds)`.

## Acceptance

- Golden test: a page with `transition: opacity 1s` triggered at t=0 produces
  a `frame0030.png` whose mid-element opacity matches `~ 0.5` within
  tolerance.
- Golden test: `@keyframes fade {…}` rendered at frame 15 of a 30-frame
  1s animation matches a recorded PNG.
- One-shot `RenderAsync` callers still work — internally call `RenderFrame(0)`.

## Notes

- Damage rectangles + layer caching are paint-perf follow-ups, not blockers.
- Avalonia presentation hookup can split into a sibling WP if the
  Avalonia shell isn't ready.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7). Blocked on
  wp:M5-css-08 and wp:M5-css-09.
- 2026-05-19T15:50:55Z — claimed by agent-copilot-claude-opus-4.7, working on main
