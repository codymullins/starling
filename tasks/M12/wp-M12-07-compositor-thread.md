---
id: wp:M12-07-compositor-thread
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-05-tile-grid
  - wp:M12-10-render-on-demand
blocks:
  - wp:M12-09-compositor-anim
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/11_AVALONIA_SHELL.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-07-compositor-thread — Run composite on a background thread

## Goal

Decouple scroll-driven composite from the main (UI / style+layout+paint)
thread. Scrolling is by far the most common user input; today every
scroll event runs through `WebviewPanel` → `PageRendererHost.Render` →
`ImageSharpBackend` on the UI thread. With layers + tiles in place, a
pure scroll is just "blit different tiles" — no paint, no layout. That
blit can run on a compositor thread, leaving the UI thread free to
handle the next input event while the previous frame is still being
composited.

## Inputs

- `wp:M12-05-tile-grid` shipped: tiles exist, cache hit on scroll is the
  common case.
- Avalonia's `RenderTimer` already runs off the UI thread; we'll hook
  into it for compositor ticks.

## Outputs

- `Starling.Paint.Compositor.CompositorScheduler`:
  - Owns a single dedicated thread (not a thread-pool task — predictable
    latency matters more than throughput).
  - Receives `CompositeRequest(viewport, scale)` messages from
    `WebviewPanel` on every scroll/window-resize/animation tick.
  - Coalesces requests: at most one queued request, the newest wins.
  - Runs `Compositor.Render` and posts the resulting `RenderedBitmap`
    back to the UI thread for display.
- A clear protocol: while composite is running, the main thread can mutate
  the layer tree IF AND ONLY IF that mutation goes through an immutable
  snapshot exchange. Concretely:
  - The layer tree is rebuilt as an immutable structure each frame.
  - The compositor thread holds a reference to "the layer tree it's
    compositing"; the main thread publishes a new snapshot when ready;
    the compositor swaps to the new snapshot on the next request.
  - Tile cache is a concurrent dictionary; tile bitmap entries are
    immutable once published.
- Pure-paint frames (display list changed) still run paint on the main
  thread but composite on the compositor thread.
- New counters: `paint.compositor.frame_ms`, `paint.compositor.coalesced`,
  `paint.compositor.dropped`.

## Acceptance

- A test that posts 100 scroll events in tight succession sees ≤ N
  composite frames where N is the compositor's frame budget allows, and
  every event after the first sees `paint.compositor.coalesced` increment
  rather than queueing.
- A 100ms artificial sleep injected into layout on a scroll-heavy test
  does NOT stall scroll: the compositor keeps blitting cached tiles for
  the old layer tree until the new tree is published.
- No data races reported by xUnit's `ConcurrentTests` runner across 1000
  iterations of "scroll while mutating".
- `dotnet build && dotnet test` green.

## Additional requirements (game-engine input-to-photon pipeline)

These four requirements are bundled here because they're useless
individually — a "smooth scroll" architecture either has all of them or
delivers no perceptible win:

1. **Dedicated OS-rate input thread.** The compositor doesn't read input
   directly; an input thread captures scroll/touch/wheel deltas at the
   OS's max rate (120 Hz on ProMotion, 240+ Hz on gaming mice) and posts
   them to the compositor via a lock-free SPSC queue. Avalonia delivers
   events on the UI thread; tap into the platform layer (NSEvent on
   macOS via the existing Catalyst bridge, raw input on Windows) where
   possible, and fall back to UI-thread input forwarding otherwise.
2. **Predicted scroll position.** The compositor knows the next vsync
   time and the recent scroll velocity (EWMA over last ~4 samples).
   When compositing frame for vsync T+1, it extrapolates scroll position
   to T+1 rather than using the latest sampled position. Bounded: cap
   prediction at ~16ms to avoid overshoot when fling decelerates.
3. **Dynamic-DPI during fling.** When scroll velocity exceeds a threshold
   (default 2000 px/s), render tiles at 0.75× DPI; above 5000 px/s,
   0.5×. Snap back to native when velocity drops below the threshold for
   a settle window (~100ms). The user cannot resolve detail at fling
   speed; this cuts tile paint cost 2–4×. Counter:
   `paint.compositor.dynamic_dpi_active_seconds`.
4. **Present mode switching.** When `RenderScheduler.Mode == Active`
   (input or animation in flight), use WebGPU `PresentMode.Mailbox` —
   renders ahead, drops stale frames, never blocks. When idle, use
   `PresentMode.Fifo` — vsync-locked, sleeps cleanly on present. Default
   on Avalonia/CPU path: equivalent semantics (no buffered queue when
   idle).

Each of (1)–(4) should land in its own commit on this WP so they can be
bisected. The acceptance test should verify all four are wired:

- Synthetic input burst at 240 Hz produces ≤ refresh-rate frames with
  the latest delta represented.
- A 1000 px/s scroll over 500ms produces visible position that matches
  predicted-time + extrapolation, not last-sample (verify via test
  hook reporting the position used for each frame).
- A fling test at 6000 px/s observes `dynamic_dpi_active_seconds`
  incrementing.
- WebGPU backend test asserts present mode switches between Fifo/Mailbox
  in lockstep with `RenderScheduler.Mode`.

## Notes

- This is the WP where the engine grows a real threading discipline.
  Document the rules in `browser-plan/08_FONTS_PAINT.md` so future
  contributors know "anything the compositor thread reads must be
  immutable once published".
- Mac Catalyst's main-thread rules (UIKit needs main-thread for most
  things) are not violated — the compositor thread produces a bitmap;
  handing it to the `Image` control happens via `Dispatcher.UIThread.Post`.
- Performance target: 60 fps composite under a 4K display, layer tree
  with 10 promoted subtrees, while the main thread does 16ms of work
  per frame.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
