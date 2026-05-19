---
id: wp:M12-08-prefetch-ring
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-05-tile-grid
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-08-prefetch-ring — Predictive tile prefetch

## Goal

Hide first-scroll latency by speculatively painting a 1-tile ring of tiles
around the viewport during idle time (immediately after a successful
composite). When the user actually scrolls a tile-width, the next tile is
already in cache.

## Inputs

- `wp:M12-05-tile-grid` shipped.
- Optionally `wp:M12-07-compositor-thread` shipped: prefetch can run on
  the compositor thread to avoid stealing main-thread cycles. (Soft dep —
  this WP works without it but is much more useful with it.)

## Outputs

- `Compositor.PrefetchIdle(viewport, scale)` invoked after each successful
  composite. Paints the ring of tiles around the viewport (configurable
  ring width, default 1 tile).
- Prefetch respects the tile cache budget — if painting would push past
  `MaxBytes`, it skips rather than evicting in-use tiles.
- Prefetch is cancellable: a new composite request preempts the prefetch
  pass.
- Metric: `paint.tile.prefetched`, `paint.tile.prefetch_cancelled`.

## Acceptance

- Synthetic test: composite a viewport, then composite again offset by 1
  tile-width in the prefetched direction. The second composite records
  zero `paint.tile.cache_miss`.
- A scroll-velocity-aware variant: if the user is scrolling down, the
  ring is biased downward. Test with a synthetic 100-scroll sequence
  southward sees ≥ 95% hit rate.
- Prefetch never blocks a composite request: a test that interleaves
  prefetch with composite never observes composite latency >
  `frame_budget + 1ms`.
- `dotnet build && dotnet test` green.

## Notes

- This is the WP where the difference between "scroll-jank free" and "the
  page just appears as you scroll" lives.
- Direction biasing: track the last few `viewport.Y` deltas; bias the
  ring in the direction of the trend. A two-sample EWMA is fine.
- Prefetch budget could also be time-budgeted ("paint until X ms of idle
  is consumed"). Time-budgeting is preferred over tile-count-budgeting
  because tile paint cost varies wildly.
- **Game-engine streaming analogy.** This WP is the direct analogue of
  open-world game asset streaming (RDR2, Horizon, modern UE world
  partition): the "player" is the viewport, the "world" is the page,
  and the prefetch ring is the streaming radius. Borrow the UE heuristic
  almost verbatim:
  - Base radius `R` (default 1 tile) always resident.
  - Velocity-scaled bias along scroll axis: ring extends to
    `R + ceil(velocity_px_per_s / tile_height * lookahead_seconds)` in
    the scroll direction, where `lookahead_seconds` is 0.5 by default
    (covers one fling deceleration window).
  - Jobs prioritized by predicted-time-until-visible, computed from
    current scroll velocity. The closest-to-viewport tile in the
    predicted-visible direction goes first.
  - Tiles outside `R + hysteresis` (default 2 extra tiles) are evicted
    on a clock that also accounts for memory pressure (existing
    `MaxBytes` budget).
  - Hard cap on prefetch tile *paint* time per idle window (default
    8ms) so prefetch never delays a real composite by more than one
    frame, even if it gets preempted.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
