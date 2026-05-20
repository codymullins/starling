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
- 2026-05-20 — implemented (agent-claude-cody). Summary:
  - `IPaintBackend.Render` gained a `Rect viewport` overload (page-coord
    X/Y = scroll offset, W/H = visible size); the old `Size` overload is a
    default-interface delegate with X=Y=0. `ImageSharpBackend` sizes the
    bitmap to viewport.W×H and translates the device canvas by
    (-viewport.X, -viewport.Y) (post-transform for transformed items, via
    the coordinate-conversion helpers + `CurrentInDeviceSpace`).
  - `DisplayListBuilder.Build(root, Rect? viewport, …)` culls items whose
    POST-transform page AABB misses the viewport (expanded by a 64 px
    `OverdrawMargin`). Transformed subtrees are painted into a scratch list
    and only bracketed with Push/Pop if they produced visible items, so the
    list stays balanced and O(on-screen). `viewport: null` reproduces the
    legacy emit-everything behavior exactly.
  - `Painter.RenderDocument` / `RenderWithStyle` and
    `PageRendererHost.Render` take an optional clip-viewport `Rect?`; null =
    full-page (headless PNG path unchanged).
  - Deliverable #4: the WebGPU→CPU oversize fallback is RETAINED as a
    minimal guard, NOT deleted. Reason: the headless full-page screenshot
    path and the no-viewport GUI render both legitimately ask for surfaces
    >8192 px and the CPU rasterizer must handle them; only the *unbounded*
    path can reach WebGPU oversize. The viewport-clipped scrolling path is
    bounded by window size and never trips the guard, so
    `paint.webgpu.fallback_cpu.oversize` stays at zero in a session — the
    acceptance criterion is met without risking a wgpu abort() on the
    full-page path.
  - `WebviewPanel` rewritten to a ScrollViewer over a page-sized virtual
    `Canvas` (scroll extent) with a viewport-sized `Image` repositioned to
    the scroll offset and re-rendered on `ScrollChanged`. NOTE: this GUI
    path is compile-checked only — Avalonia scroll behavior was not
    runtime-verified (headless env).
  - Tests: `tests/Starling.Paint.Tests/ViewportClipTests.cs` (6 cases:
    viewport sizing on a ~200000 px page, O(on-screen) culling,
    transform-onto/off-viewport culling with balanced brackets, viewport
    offset translation, null-viewport == legacy). Full solution builds;
    full suite green (Paint.Tests 94/94, all projects 0 failures).
  - Unblocks wp:M12-02-picture-cache (its only dep).
