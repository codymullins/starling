---
id: wp:M12-02-picture-cache
milestone: M12
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T17:03:45Z"
completed_at: "2026-05-20T17:10:35Z"
branch: "main"
depends_on:
  - wp:M12-01-viewport-clip
blocks:
  - wp:M12-04-layer-tree
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-02-picture-cache — Single-bitmap picture cache (WebRender-style)

## Goal

Avoid re-painting from scratch on every scroll by caching the most recent
viewport raster keyed by `(page-version, scale, page-coord origin)`. On a
scroll where the new viewport is fully covered by the cached image, blit
the cached pixels at an offset instead of re-running the paint backend.
On a scroll where part of the new viewport extends beyond the cache, paint
only the newly-exposed strip (delta-paint) and stitch it onto the existing
cache.

This is intentionally *one* bitmap, not a tile grid — that comes in
`wp:M12-05-tile-grid`. The picture cache buys back the scroll smoothness
lost when `wp:M12-01-viewport-clip` removed the "render the whole page
once" approach.

## Inputs

- `wp:M12-01-viewport-clip` shipped: `IPaintBackend.Render` takes a
  viewport, `WebviewPanel` re-renders on scroll.
- A way to invalidate the cache when the display list changes — for now,
  a monotonically increasing `Page.DisplayListVersion` bumped whenever
  layout or style recomputes. Style/layout already runs on the UI thread;
  bumping a counter is cheap.

## Outputs

- New `Starling.Paint.PictureCache`:
  - Holds the last rendered `RenderedBitmap`, its page-coord origin, its
    scale, and the `pageVersion` it was rendered against.
  - `TryServe(viewport, scale, pageVersion, out blit)` returns a hit when
    `viewport ⊆ cached.Bounds` and the version + scale match.
  - `Stitch(strip)` accepts a newly-painted edge strip and grows the cache
    bitmap, evicting if it would exceed a configurable max area
    (e.g. 2× window size).
- `PageRendererHost` (or a new `CachedPageRenderer`) consults the cache
  before invoking the backend, splits the requested viewport into "cache
  hit" + "uncached strips" rectangles, paints the strips through the
  backend, and composites the result.
- A diagnostics counter set: `paint.cache.hit`, `paint.cache.miss`,
  `paint.cache.partial`, `paint.cache.evict`. Histogram of strip area
  painted per frame.

## Acceptance

- A test that scrolls a synthetic 200000-px page by 50 px N times records
  ≥ N-1 `paint.cache.partial` hits and ≤ 1 full miss. Strip area painted
  per scroll is ≤ `50 × viewport.Width` (plus the overdraw margin).
- A test that bumps `DisplayListVersion` between frames records a full
  cache miss on the next render.
- Scrolling netclaw.dev feels smooth (manual smoke; documented in the
  handoff log) and `paint.cache.hit` dominates `paint.cache.miss` in the
  Aspire dashboard.
- `dotnet build && dotnet test` green.

## Notes

- The cache is per-tab. Switching tabs throws the cache away; a future
  enhancement can hold N tab caches with LRU eviction.
- Subpixel scrolling: strips are painted at integer page-coord offsets
  but composited at the requested float offset; the inner stitching code
  rounds to whole device pixels to avoid aliasing.
- This WP does NOT touch CSS animations or hover styles — any change to
  the display list bumps `pageVersion` and invalidates wholesale. Smarter
  partial invalidation lands with `wp:M12-06-invalidation`.
- Reuses `RenderedBitmap` storage; the cache is a single managed
  `byte[]` plus a `Rectangle` describing its page-coord placement.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-20T17:03:45Z — claimed by agent-claude-cody, working on main
- 2026-05-20T17:10:35Z — merged; complete
- 2026-05-20 — Implemented (agent-claude-cody). Deliverables:
  - `src/Starling.Paint/Cache/PictureCache.cs` — pure-managed single-bitmap
    cache. Works in integer device pixels (page CSS px × scale, rounded). API:
    `TryServe`/`TryServeRaw` (HIT containment + blit; only `TryServe` bumps
    `paint.cache.hit`), `ComputeUncachedStrips` (owns the per-frame
    `paint.cache.miss`/`paint.cache.partial` counter; returns top/bottom/left/
    right edge strips via rect subtraction), `Stitch` (row-copy grow of the
    union; emits `paint.cache.strip_area`; returns false + bumps
    `paint.cache.evict` when growth would exceed `maxAreaPx`, default ~4M px),
    `Reset`/`Invalidate`. `DeviceRect` int rect + `CacheBlit` row-copy descriptor.
  - `src/Starling.Paint/Cache/CachedPageRenderer.cs` — wraps `IPaintBackend`:
    HIT → blit (no backend call); PARTIAL → paint each strip via
    `DisplayListBuilder.Build(strip)` + `backend.Render(strip)`, stitch, serve;
    eviction during stitch → full repaint + reseed (keeps output correct); MISS →
    full repaint + seed. Output is byte-identical to a from-scratch render.
  - `pageVersion`: `LaidOutPage.DisplayListVersion` — process-wide monotonic int
    assigned at construction (each fresh layout = new page = new version; a scroll
    reuses the page so the version is stable). Threaded
    `WebviewPanel → PageRendererHost.Render(..., pageVersion) → CachedPageRenderer`.
    Hover-override transitions also `InvalidateCache()` since they mutate the
    display list without bumping the version.
  - `PageRendererHost` now holds a `CachedPageRenderer`; the viewport (scroll)
    path goes through it, the no-viewport full-page path bypasses the cache
    (headless screenshots stay byte-identical). `InvalidateCache()` called on
    `ShowPage`.
  - Tests: `tests/Starling.Paint.Tests/PictureCacheTests.cs` (6 methods) — drives
    the real `CachedPageRenderer` + `ImageSharpBackend`. Covers the 200000px
    scroll-partial acceptance (≥N-1 partial, ≤1 miss, bounded strip area),
    version-bump full miss, byte-identical HIT and PARTIAL serves vs from-scratch,
    eviction correctness + reseed, repeated-viewport pure HIT.
  - `dotnet build` + full `dotnet test` green (Paint.Tests 100/100; whole solution
    15 projects, exit 0, no failures).
  - NOT runtime-verified here (headless env): the WebviewPanel scroll integration
    and the "netclaw.dev feels smooth" manual smoke from Acceptance. The
    GUI wiring is compile-checked + covered by the existing Gui.Tests (82 pass),
    but the live scroll smoothness / Aspire dashboard hit-vs-miss observation is
    left for a manual smoke run.
