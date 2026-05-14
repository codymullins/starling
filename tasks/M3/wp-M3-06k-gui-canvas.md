---
id: "wp:M3-06k-gui-canvas"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on:
  - "wp:M3-06j-skia-fonts"
blocks: []
subsystem: "Tessera.Gui"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06k-gui-canvas — Skia-painted GUI canvas, retire `BoxTreeRenderer`

## Goal

Phase 7: switch the Mac Catalyst GUI from the native-view `BoxTreeRenderer` path
to a single Skia-painted canvas, unifying headless + GUI paint. Add a `UIView`
backed by a `CAMetalLayer` (Graphite → Dawn → Metal renders straight into it)
plus a MAUI handler. The hard part is **hit-testing, not drawing**: all of
hover / link-activation / drag-select / Cmd-F must be re-derived from the
laid-out box tree instead of the native MAUI view tree. Delete
`BoxTreeRenderer.cs`.

## Inputs

- `wp:M3-06j-skia-fonts` complete: the full Skia paint + font path works
  headless; `SkiaGraphiteBackend` renders the display list.
- The laid-out box tree returned by `LayoutDocumentWithStyle` (the new
  hit-testing source of truth).

## Outputs

- `src/Tessera.Gui/Platforms/MacCatalyst/SkiaCanvasView.cs` — a `UIView` backed
  by a `CAMetalLayer`; Graphite renders straight into it.
- A MAUI `SkiaCanvasViewHandler` registering the canvas view.
- Shim addition: `ts_surface_create_from_metal_layer(...)` (in
  `native/shim/tessera_skia.{h,cpp}` + its `Tessera.Skia` binding).
- `src/Tessera.Gui/MainPage.cs` — heavily edited: render flow becomes
  `Engine.LayoutPageAsync → BlockBox → DisplayListBuilder.Build →
  SkiaGraphiteBackend` into the layer-backed surface; hover / link-activation /
  drag-select / Cmd-F re-derived from the box tree; `:hover` re-cascade moves
  from per-`Label` `PointerGestureRecognizer` to a single canvas-level pointer
  handler that hit-tests the box tree and repaints.
- **Deleted:** `src/Tessera.Gui/BoxTreeRenderer.cs`.

## Acceptance

- The Mac Catalyst app launches and renders a page through the Skia canvas
  (`CAMetalLayer`-backed surface), not through native MAUI labels.
- Hover (`:hover` re-cascade), link activation, drag-select, and Cmd-F all still
  work — re-derived from the laid-out box tree.
- `BoxTreeRenderer.cs` is deleted; `MainPage.cs` no longer references it or
  per-`Label` gesture recognizers for hover.
- Headless + GUI both paint through the same `DisplayList` →
  `SkiaGraphiteBackend` path.
- The packaged `.app` is tested (not just `dotnet run`) — the native lib
  resolves under the bundle layout.
- Full repo `dotnet build && dotnet test` green.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 7).
- Hit-testing is the genuine effort sink — drawing is the easy half. Budget
  accordingly.
- Mac Catalyst relocates `runtimes/` inside the `.app` bundle — the
  `SetDllImportResolver` fallback from `06h` must cover it; verify on the
  packaged app.
- This is the last critical-path package; the final integration merge
  (flag flip + `ImageSharpBackend.cs` deletion) happens after this lands.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
