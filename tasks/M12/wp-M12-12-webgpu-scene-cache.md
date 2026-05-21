---
id: wp:M12-12-webgpu-scene-cache
milestone: M12
status: "closed-not-viable"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on: []
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - tasks/M12/wp-M12-11-glyph-atlas-cpu.md
---

# wp:M12-12-webgpu-scene-cache — Retained-scene caching for the WebGPU backend

## OUTCOME — CLOSED, NOT VIABLE (do not implement)

Spiked to the gating question and it **fails**: `DrawingCanvas.RenderScene` **bakes
the transform at encode time and ignores the replay-time canvas transform**, so a
single encoded scene cannot be replayed at a different scroll offset or scale —
every viewport needs a re-encode, which is the expensive operation the cache was
meant to avoid.

Controlled single-primitive pixel-extent experiments (WebGPU, scale=2, throwaway
`SceneCacheValidationTests`, since removed):

| Experiment | Result | Conclusion |
|---|---|---|
| direct fill under `Save(scale2)`, no scene | 100px → **199** | `Save` scales direct draws ✓ |
| encode at identity, **replay** `Save(scale2)` | **99** | replay scale **ignored** |
| **encode** under `Save(scale2)`, replay identity | 199 | transform **baked at encode** |
| encode `Save(scale2)`, replay `Save(scale2)` | 199 (not 399) | only encode transform applies |
| encode identity, replay `Save(translate +60)` (fill) | minX **0** | replay translate **ignored** |
| encode identity, replay `Save(translate +120)` (text) | minX **1** | ignored for text too |

So the picture-cache cases this WP targeted collapse:

- **Scrolling** needs a different transform per offset → re-encode each frame → no
  saving. The shipped sliding-window picture cache (`PictureCache`) already makes
  scrolling cheap on WebGPU: it pixel-blits the retained overlap and re-encodes
  only the thin newly-exposed strip.
- **Same-viewport re-render** (the only thing scene replay *can* do, since the
  baked transform matches) is already served by the picture cache as a **pixel
  blit with zero GPU work** — strictly cheaper than a scene replay.

The earlier "Phase 0.5" same-transform replay numbers below are real but only
apply when the replay transform equals the encode transform — i.e. the exact case
the picture cache already wins. An earlier transform spike (`SceneTransformSpike`)
appeared to show replay-time composition working; the single-primitive experiments
above are more rigorous and contradict it — treat that earlier result as an
artifact.

**Net:** the retained-scene API is the wrong shape for this engine's needs. No
code landed (backend scene primitives + tests were prototyped and reverted). The
original plan is retained below for context only.

---

## Goal (original plan — not pursued)

Encode a page's display list into a Vello retained scene once
(`DrawingCanvas.CreateScene`) and replay it with `DrawingCanvas.RenderScene` on
subsequent frames, so an unchanged display list skips the per-frame CPU
scene-encoding cost. This is the WebGPU analogue of the CPU glyph atlas
(`wp:M12-11`) — the right lever for the **default** backend, which the atlas does
not help.

## Why this exists (Phase 0.5 spike findings)

The WebGPU package is a Vello-style scene renderer. A spike
(`GlyphAtlasSpike`) showed its per-frame `command_record` cost is dominated by
**CPU-side scene encoding** of glyph runs (~26µs/glyph), not glyph
rasterization — so a glyph atlas gives it no benefit (≈0.93×).

A follow-up spike (`SceneCacheSpike`, throwaway, since removed) measured the
retained-scene API instead:

- **Same-target (device fixed):** draw-each-frame `21.97 ms` → `RenderScene`
  replay `3.25 ms` = **6.76×**. The replayed frame is correct (non-blank
  readback), so replay genuinely reproduces the content.
- **Cross-target:** a scene built on one `WebGPURenderTarget` replays on a
  **fresh** target — draw `24.81 ms` → replay `4.53 ms` = **5.48×**. This
  matters because `ImageSharpBackend.RenderWebGpu` allocates a new target per
  render; the scene survives that boundary.

So `RenderScene` skips re-encoding and is ~5–7× cheaper than re-issuing the
draw commands, and it works across the per-frame target lifecycle.

## Where it pays off

The sliding-window picture cache (shipped) already serves unchanged *pixels*
with no backend call, so this WP targets the cases that still hit the backend:

- **Scroll / arbitrary viewport from one encode.** Encode the whole page's
  display list into a retained scene once per display-list version, then each
  frame `RenderScene` it under a viewport-translate transform for the current
  scroll offset — replay cost (~3–5 ms) regardless of how much text is visible,
  instead of re-encoding the visible glyphs every strip/MISS.
- **MISS reseed + overlays.** A picture-cache MISS, hover/selection repaint, or
  animation frame on unchanged content replays the cached scene.

## Key open questions (spike before building)

1. **Transform at replay.** Can a viewport/scroll offset be applied at replay
   time (`canvas.Save(translate); canvas.RenderScene(scene); canvas.Restore();`)
   and does it compose with the scene's own transforms? Needed to render
   different scroll offsets from one encoded scene. (The same-target/cross-target
   spike replayed at the original transform only.)
2. **Scene lifetime / memory.** `DrawingBackendScene` holds owned GPU resources;
   size per page, eviction, and disposal on version bump / navigation.
3. **Device/target reuse.** Cross-target replay works, but each
   `new WebGPURenderTarget` still pays ~15 ms device/context init. Real gains
   need the device (and ideally target) reused across frames — relate to
   `wp:M12-07-compositor-thread` / `wp:M12-10-render-on-demand`.
4. **Invalidation granularity.** Re-encode on display-list version bump
   (`LaidOutPage.DisplayListVersion`); confirm partial invalidation
   (`wp:M12-06`) interplay.

## Acceptance

- WebGPU re-render of an unchanged display list at a new viewport drops to
  replay cost (target: ≥4× vs re-encode on a text-heavy page).
- Output stays ≥ 0.99 SSIM vs the re-encode path; `PictureCacheTests`
  byte-identical guarantee unaffected (cache sits below the picture cache).
- New bench capturing encode-once / replay-many.

## Out of scope

- CPU backend (use `wp:M12-11` glyph atlas there).
- Tile grid / partial invalidation (separate WPs).
