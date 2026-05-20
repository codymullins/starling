---
id: wp:M12-01-viewport-clip
milestone: M12
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T16:52:08Z"
completed_at: ""
branch: "main"
depends_on: []
blocks:
  - wp:M12-02-picture-cache
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-01-viewport-clip — Viewport-clipped paint (off-screen cull)

## Goal

Stop rasterizing the entire page bitmap on every frame. The paint pipeline
must accept a `viewport: LayoutRect` describing the currently-visible region
(in page coordinates) and skip any display-list item whose bounds don't
intersect it. The output bitmap is sized to the viewport, not to
`root.Frame`. This is the prerequisite for everything else in M12 and the
permanent fix for the netclaw.dev / wgpu 8192 px texture crash class
(which is currently masked by a CPU fallback in `ImageSharpBackend`).

## Inputs

- `PageRendererHost.Render(root, scale, …)` currently passes
  `root.Frame.Width × root.Frame.Height` as the surface size — must thread
  a viewport through.
- `Starling.Paint.DisplayList.{DisplayList, DisplayItem, DisplayListBuilder}`.
- `ImageSharpBackend.Render(list, viewport, scale)`.
- Avalonia `WebviewPanel` — knows the `ScrollViewer.Offset` + `Viewport`
  size in CSS px; today it sets the `Image` size to the full page and lets
  Avalonia scroll.

## Outputs

- `IPaintBackend.Render` signature extended (or overloaded) to take a
  `viewport: LayoutRect` in page coordinates. Existing callers default
  to the root's full frame for backward compatibility (e.g. tests, headless
  CLI for full-page screenshots).
- `DisplayListBuilder.Build` accepts an optional `viewport` and emits only
  items whose AABB intersects it (with a small N-pixel overdraw margin to
  avoid edge popping on partial-pixel scroll). Items that are clipped out
  by an ancestor `PushTransform` / future `PushClip` use the transformed
  bounds.
- `ImageSharpBackend` translates the device canvas so paint at page-coord
  (viewport.X, viewport.Y) lands at device-coord (0, 0).
- `WebviewPanel` is rewritten to use a `ScrollViewer` with a *virtual*
  content size (page-sized blank `Canvas` for scroll extent) and a fixed
  viewport-sized `Image` overlaid on top that re-renders on scroll.
- The fallback added in the netclaw.dev fix
  (`MaxWebGpuTextureDimension` guard in `ImageSharpBackend`) is removed —
  the viewport is now bounded by window size and cannot exceed wgpu's
  texture limit under any realistic window.

## Acceptance

- A 200000-px-tall synthetic page renders without any code path allocating
  a >viewport-sized bitmap. New test in `Starling.Paint.Tests` asserts
  `RenderedBitmap.Width == viewport.Width` and `Height == viewport.Height`
  regardless of `root.Frame.Height`.
- The display list reaching the backend for that synthetic page contains
  only items intersecting the viewport (`displayList.Items.Count` is O(items
  on screen), not O(items on page)).
- Scrolling netclaw.dev under `STARLING_PAINT_BACKEND=imagesharp-gpu` renders
  without falling back to CPU (counter `paint.webgpu.fallback_cpu.oversize`
  stays at zero across the session).
- `dotnet build && dotnet test` green.

## Notes

- This deliberately *trades* full-page rasterization for per-scroll repaint.
  That is a regression on scroll smoothness for medium pages; the picture
  cache in `wp:M12-02-picture-cache` immediately follows to win that back.
- Items with transforms need their *post-transform* AABB compared against
  the viewport. Today's `TransformStack` in `ImageSharpBackend` handles
  the matrix at paint time; the builder needs the same math during cull.
- The headless CLI's "render full page to PNG" mode must keep working —
  treat a missing viewport as "render everything" (same behavior as today).
- Hit-testing already walks the layout tree and is independent of paint,
  so this change does not affect input dispatch.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7) after the
  netclaw.dev crash investigation surfaced the "render whole page in one
  bitmap" architectural limit.
- 2026-05-20T16:52:08Z — claimed by agent-claude-cody, working on main
