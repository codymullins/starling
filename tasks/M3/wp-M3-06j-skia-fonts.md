---
id: "wp:M3-06j-skia-fonts"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-skia-fonts"
claimed_at: "2026-05-14T17:09:02Z"
branch: "main"
depends_on:
  - "wp:M3-06i-skia-backend"
blocks:
  - "wp:M3-06k-gui-canvas"
subsystem: "Tessera.Paint"
plan_refs:
  - "browser-plan/08_FONTS_PAINT.md#fonts"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/12_TESTING.md#golden-suite"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06j-skia-fonts — Skia typeface/shaping replaces SixLabors.Fonts

## Goal

Phase 6: move the font path onto Skia. Rewrite `FontResolver` to return Skia
typefaces, add `SkiaTextMeasurer` with real HarfBuzz-shaped metrics, and switch
`Painter.LayoutDocumentWithStyle` from `DefaultTextMeasurer` to it. Real shaped
metrics differ from the current 0.5em heuristic, so **every layout golden and
SSIM baseline shifts** — re-vendor all of `testdata/golden/` in the same PR.
This is correctness, not regression.

## Inputs

- `wp:M3-06i-skia-backend` complete: `SkiaGraphiteBackend` + `RenderedBitmap`
  exist; `Tessera.Skia` exposes typeface/font/`shape_text`/`font_metrics`.
- Existing 3-tier font chain (bundled → embedded → system) and the embedded
  `OpenSans-Regular.ttf`.

## Outputs

- `src/Tessera.Paint/FontResolver.cs` — rewritten to return Skia typefaces:
  `ts_typeface_from_data` for the embedded `OpenSans-Regular.ttf`,
  `ts_typeface_from_name` via Skia's `SkFontMgr`. The 3-tier chain stays
  conceptually identical.
- `src/Tessera.Paint/SkiaTextMeasurer.cs` (new) — implements `ITextMeasurer`
  with real HarfBuzz-shaped metrics.
- `src/Tessera.Paint/Painter.cs` — `LayoutDocumentWithStyle` switches from
  `DefaultTextMeasurer` to `SkiaTextMeasurer`.
- `src/Tessera.Layout/Text/ITextMeasurer.cs` — kept as the seam;
  `DefaultTextMeasurer` is **kept** for paint-free layout unit tests.
- `testdata/golden/` — re-vendored in full against the new shaped metrics.

## Acceptance

- `FontResolver` returns Skia typefaces through the unchanged 3-tier chain;
  `SkiaTextMeasurer` implements `ITextMeasurer` with HarfBuzz-shaped metrics.
- `Painter.LayoutDocumentWithStyle` uses `SkiaTextMeasurer`;
  `DefaultTextMeasurer` still exists and is still used by paint-free layout unit
  tests.
- `testdata/golden/` is fully re-vendored; the golden suite passes under the new
  per-platform SSIM thresholds.
- Layout-shift from real metrics is reflected in the re-vendored goldens (not
  suppressed) — documented as correctness in the handoff log.
- Full repo `dotnet test` green on win/mac/linux.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 6).
- The golden re-vendor is expected and budgeted — every layout golden and SSIM
  baseline moves. Do it in this same package, not a follow-up.
- `DefaultTextMeasurer` must survive — it keeps layout unit tests paint-free.
- `06k-gui-canvas` depends on this: the GUI canvas paints through the same Skia
  font path.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
