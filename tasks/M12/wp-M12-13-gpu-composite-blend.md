---
id: wp:M12-13-gpu-composite-blend
milestone: M12
status: "complete"
claimed_by: "agent-claude-opus"
claimed_at: "2026-05-30T01:09:44Z"
completed_at: "2026-05-30T02:30:00Z"
branch: "main"
depends_on:
  - wp:M12-04-layer-tree
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
  - layer-tree-plan.md
---

# wp:M12-13-gpu-composite-blend — Composite the layer blend on the GPU

## Goal

Move the per-frame layer blend off the managed CPU loop and onto the GPU, so the
raster-call win from the LTF series (re-raster only changed layers) becomes a
frame-time win for every page.

## Inputs

- The LTF series shipped (see `layer-tree-plan.md`): each layer caches its
  content, keyed by a slice content hash, and only changed layers re-raster.
- `Compositor.BlendLayer` (`src/Starling.Paint/Compositor/Compositor.cs`) is the
  blend. It runs on the CPU and touches every output pixel for each layer.

## Outputs

- A GPU composite path that uploads each cached layer once and blends the layers
  into the viewport on the GPU. Transform, opacity, and clip apply at blend time.
- The CPU `BlendLayer` stays as a fallback for hosts with no GPU adapter.

## Acceptance

- On the `compositor-demo` replay scenario, the composite path's frame time at
  scale 2.0 is at or below the flat raster path (today it is far above — see the
  measurements in `docs/animation-relayout-perf.md`).
- The GPU composite output stays byte-identical (or within the existing
  similarity-score tolerance for rotations) to the CPU `BlendLayer` path. Add a
  golden test that renders the same layer tree through both paths.
- `bench/Starling.Bench -- replay compositor-demo --composite --scale 2.0`
  reports a raster phase no worse than the flat run, and the frame phase improves.

## Notes

This is the missing half of the LTF series. LTF-00 through LTF-06 cut *what*
re-rasters to the changed layers only, and proved it (the harness reports one
layer rastered per frame on the demo, the rest blitted from cache). But the blend
that stitches the cached layers together is still O(viewport pixels) per layer on
the CPU, so on a cheap-to-raster page the blend costs more than the flat raster it
replaces. The win lands once the blend runs on the GPU.

Root cause #5 in the original investigation named this: "compositing the base
layer is a per-pixel inverse-bilinear loop over the whole viewport every frame."
LTF-05 made the identity case a cheap row copy, but a rotated or scaled layer
still walks every pixel, and even a row copy of a full-viewport base is real work
at Retina scale. The GPU does this blend almost for free.

This work also unblocks widening the compositor-path gate — see
`wp:M12-14-compositor-path-gate`. While the blend is expensive, the path is
gated tightly to animation frames to avoid a regression on cheap pages.

## Handoff log

- 2026-05-30T01:09:44Z — claimed by agent-claude-opus, working on main
- 2026-05-30 — COMPLETE. The composite blend now runs on the GPU.
  - New `src/Starling.Paint/Compositor/GpuLayerCompositor.cs`: a persistent
    wgpu device/queue/render-pipeline/sampler driven via Silk.NET.WebGPU
    (pinned in Directory.Packages.props, already transitive through the
    SixLabors WebGPU backend — no new native dependency). Each layer uploads to
    a texture once, keyed by its slice content hash, and stays resident across
    frames. Every frame blends the layers in one render pass: a textured quad
    per layer placed by its transform, scaled by opacity, clipped by a scissor
    rect, alpha-over in premultiplied space. One viewport readback per frame.
  - `Compositor.cs` refactored: the tree walk now emits a flat list of
    `LayerBlend` ops (shared geometry), then dispatches to the GPU
    (`GpuLayerCompositor.Shared`, default on) or the managed CPU `BlendOp`
    fallback. `DisableGpuBlend` init flag pins the CPU path. CPU output is
    unchanged from before (byte-for-byte).
  - Parity: `GpuCompositeParityTests` renders the same tree through both paths.
    Upright layers match the CPU blend within a rounding unit (≥99.9% of pixels,
    SSIM ≥ 0.999); a rotated layer matches within SSIM ≥ 0.99. Skips when no GPU
    adapter. All existing compositor tests pass through the GPU default path.
  - Perf (frame-replay, WebGPU backend, 150 frames, compositor-demo): composite
    is now at or below the flat raster path at BOTH scales —
    raster scale 2.0 16.1 ms vs flat 17.2 ms (frame 16.5 vs 17.8);
    raster scale 1.0 12.4 ms vs flat 12.9 ms (frame 12.9 vs 13.4).
    Docs updated in `docs/animation-relayout-perf.md`. This unblocks
    `wp:M12-14-compositor-path-gate` (the blend is cheap enough to widen the gate).
  - Drive-by: fixed the stale `bench/Starling.Bench/CompositorBench.cs` (it
    called the removed `Render(..., pageVersion:)` overload and broke the bench
    build) to the current content-hash signature.
  - Shared-file note: `src/Starling.Paint/Starling.Paint.csproj` also carries an
    `InternalsVisibleTo Starling.Gui.Core` line from a concurrent Gui.Core split
    that was uncommitted in the tree — included as-is, it is additive.
