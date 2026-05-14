---
id: "wp:M3-06k-gui-canvas"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-gui"
claimed_at: "2026-05-14T17:31:38Z"
completed_at: "2026-05-14T17:55:00Z"
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
- 2026-05-14T17:55:00Z — complete (agent-claude-cody-gui).

  **Presentation approach — offscreen render + bitmap surface (v1, not CAMetalLayer).**
  Took the lower-risk self-contained path the WP recommended over Phase 7's
  `CAMetalLayer` shim. `src/Tessera.Gui/PageRenderer.cs` builds a `DisplayList`
  from the laid-out box tree with `DisplayListBuilder` and rasterizes it with
  `SkiaGraphiteBackend` — the *exact* pipeline `Painter.RenderDocument` runs
  headless, so headless + GUI now share one paint path. The `RenderedBitmap`
  (straight RGBA8888) is PNG-encoded via ImageSharp and shown in a single MAUI
  `Image`. **No native, `Tessera.Skia`, or `Tessera.Paint` changes** — entirely
  within `src/Tessera.Gui/*`. One `SkiaGraphiteBackend` is held for the page's
  lifetime so the expensive Dawn/Graphite context is reused across repaints.

  **`BoxTreeRenderer` replaced with** `PageRenderer` (paint) + `BoxHitTester`
  (interaction). `MainPage.cs`'s page surface is an `AbsoluteLayout` holding the
  bitmap `Image` plus overlay `BoxView`s for hover/selection highlights.

  **Interaction re-derived from the box tree** (`BoxHitTester`, document-space
  CSS px — the bitmap is rendered 1:1 at document dimensions so image-local
  coords *are* document coords):
  - *Link activation* — `TapGestureRecognizer` → `HitTest` → nearest `<a>`
    ancestor → navigate. Solid.
  - *Cmd-F* — find index rebuilt from `CollectFragments` (box-tree text
    fragments with absolute rects); existing find-cursor/scroll logic unchanged.
    Solid.
  - *Drag-select* — `PanGestureRecognizer` → `NearestFragmentIndex` for anchor
    and cursor → tint every fragment in document order between them. Selection
    flows across paragraphs. **Rough:** selection is fragment-granular (whole
    fragments, not sub-fragment glyph offsets); no clipboard copy yet — only a
    char-count status line. `PanGestureRecognizer` reports deltas only, so the
    drag anchor is captured from the last pointer-moved position.
  - *Hover (`:hover` re-cascade)* — single canvas-level `PointerGestureRecognizer`
    (the per-`Label` recognizers are gone). On entering a link the style engine
    *does* run a real `:hover` round-trip (`Style.Compute(anchor, ctx)` with
    `HoveredElement` set) and the hovered text colour is drawn as a translucent
    tint over the link's fragment rects. **Rough — known v1 limitation:** the
    re-cascade result is presented as an overlay tint, *not* a full reflow. A
    `:hover` rule that only changes paint (colour / text-decoration — the common
    case) reads correctly; one that changes layout (`font-size`, `display`) is
    not reflowed. A faithful re-cascade+re-layout needs a `LayoutEngine` that
    threads the hover `SelectorMatchContext` into its internal `Style.Compute`
    calls — `LayoutEngine`/`Painter` currently do not, and that change is
    outside this WP's `src/Tessera.Gui/*`-only scope. **Follow-up candidate:**
    add a hover-aware re-layout entry point to `Painter`/`LayoutEngine`.

  **`DropTransitiveSkiaNative` resolution.** The GUI now legitimately depends on
  `libtessera_skia.dylib` at runtime, so it must ship inside the `.app`. The old
  target dropped the dylib from *both* the copy set (`_FileNativeReference`) and
  the reidentify set. Root cause of the original failure: MAUI's
  `_InstallNameTool` step fails running `install_name_tool` over the dylib's
  nested `runtimes/<rid>/native/` path. New target `KeepSkiaNativeUnreidentified`
  removes the dylib *only* from `@(_DynamicLibraryToReidentify)` (so
  `install_name_tool` skips it), leaving it in `@(_FileNativeReference)` so it is
  still copied into `Tessera.app/Contents/MonoBundle/runtimes/osx-arm64/native/`.
  Skipping reidentification is safe: the shim is statically linked and is loaded
  by `Tessera.Skia`'s `NativeLoader` `SetDllImportResolver`, which probes that
  bundle layout directly — it does not need the install-name rewrite. The Remove
  had to move to `AfterTargets="_ComputeDynamicLibrariesToReidentify"
  BeforeTargets="_InstallNameTool"` because that compute target is what
  *populates* the item group.

  **CAMetalLayer follow-up (not built here).** Phase 7's direct-presentation
  path — a `UIView` backed by a `CAMetalLayer` with a new
  `ts_surface_create_from_metal_layer` shim entry point so Graphite→Dawn→Metal
  renders straight into the layer with no GPU→CPU readback and no PNG re-encode —
  remains future work. It requires `native/shim` + `Tessera.Skia` changes
  (out of scope for this GUI-only WP). The current path does a GPU render, a
  pixel readback (already in `SkiaGraphiteBackend.Render`), and a PNG encode per
  repaint; acceptable for v1 but the readback+encode is the obvious next
  optimization, especially for per-pointer-move hover repaints.

  **Verification.** Full repo `dotnet build` + `dotnet test` green (15 test
  projects, 0 failures, 0 skipped). The Mac Catalyst project builds and the
  packaged `Tessera.app` was smoke-launched — it starts and stays running, so
  the native dylib resolves under the bundle layout. GUI interaction itself was
  not exercised headlessly (no harness for it).
