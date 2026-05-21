---
id: wp:M5-css-14-radius-shadow-paint
milestone: M5
status: "complete"
claimed_by: "agent-claude-cody-radshadow"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T23:30:00Z"
branch: "worktree"
depends_on:
  - wp:M5-skia-removal
  - wp:M1-09-paint-display-list
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md#raster-backend
  - browser-plan/06_CSS.md
---

# wp:M5-css-14 — border-radius painting + box-shadow

## Goal

Round corners and cast box shadows. `border-*-radius` already parses to longhand
lengths (`PropertyRegistry.cs:308`, `998`) but paint only emits square
`FillRect`/`StrokeRect` — corners are never rounded. `box-shadow` is not even a
recognized property. Add a rounded-rect paint path that respects the corner
radii on backgrounds and borders, and add `box-shadow` (property + parse +
outer drop-shadow paint).

## Inputs

- `src/Starling.Css/Properties/PropertyId.cs` + `PropertyRegistry.cs` — add
  `BoxShadow` and its typed parse (offset-x, offset-y, blur, spread, color,
  optional `inset`; comma-separated multi-layer). Border radius longhands
  already exist: `BorderTopLeftRadius` … `BorderBottomRightRadius`.
- `src/Starling.Paint/DisplayList/DisplayItem.cs` — add your records here
  (append at end; shared with two sibling paint WPs).
- `src/Starling.Paint/DisplayList/DisplayListBuilder.cs` —
  `PaintBoxAndChildren` (~194: background fill ~209, calls `EmitBorders` ~219)
  and `EmitBorders` (~415). **These are your designated methods.** Read the
  four radius longhands off the style and thread them into the emitted fills.
- `src/Starling.Paint/Backend/ImageSharpBackend.cs` — the `DisplayItem` switch
  (~287). ImageSharp.Drawing builds rounded paths via
  `IPathCollection`/`PathBuilder` arcs; box-shadow blur ≈ a `BoxBlur`/
  `GaussianBlur` over an offset shadow shape, or a soft-edge fill.

## Outputs

- A `CssBoxShadow` typed value (struct/record list) + parser.
- New `DisplayItem` record(s): a rounded fill (`FillRoundedRect(Rect, radii,
  CssColor, …)`) and a `DrawBoxShadow(Rect, radii, offset, blur, spread, color,
  inset)`; backend cases that build the rounded path / blurred shadow.
- `DisplayListBuilder` reads `border-*-radius` and emits rounded fills for
  background-color and (where applicable) border sides; emits box-shadow layers
  **behind** the box (outer) before the background.
- Tests: `tests/Starling.Css.Tests/` parse + `tests/Starling.Css.Spec.Tests/CssBackgrounds3/`
  (existing `BorderRadiusTests.cs` / add `BoxShadowTests.cs`) `[SpecFact]`;
  `tests/Starling.Paint.Tests/` pixel probes (a `border-radius` corner pixel is
  transparent/background, not the fill color; a drop shadow darkens pixels
  outside the box on the offset side).

## Acceptance

- `dotnet build && dotnet test` green (sandbox: `-p:UseAppHost=false`, `sixlabors.lic`).
- `border-radius: 16px` on a filled box leaves the extreme corner pixel unpainted
  by the fill while the center is filled.
- `box-shadow: 4px 4px 8px rgba(0,0,0,.5)` parses (incl. multi-layer + `inset`
  recognized) and paints a softened dark region offset down-right of the box.
- A box with both radius and shadow clips the shadow to the rounded silhouette.

## Notes

- Scope first cut: uniform + per-corner radii, elliptical (`rx/ry`) if cheap;
  outer drop shadow solid+blur. `inset` shadow may be deferred (parse it, paint
  best-effort or document the gap). Border-side rounding can start with the
  background/outer silhouette if per-side rounded strokes are too costly —
  document any deferral.
- Shared-file etiquette: append-only records in `DisplayItem.cs`, switch cases
  grouped near the existing ones, edit only `PaintBoxAndChildren`/`EmitBorders`
  in the builder (gradients owns `EmitBackgroundImage`, text-decoration owns
  `EmitTextFragments`). Orchestrator reconciles.

## Handoff log

- 2026-05-20 — created + claimed by agent-claude-cody-radshadow (orchestrated batch).
- 2026-05-20 — **complete** (agent-claude-cody-radshadow).
  - Worktree base was stale (branched from `9d3ec43`); fast-forwarded the
    worktree branch to local `main` (`40d1f02`) which carries this WP's
    scaffolding before starting.
  - **CSS:** added `PropertyId.BoxShadow` (appended at enum end) + `none`
    initial value; `box-shadow` flows through the registry default case as a
    raw `CssValueList`. New typed value `CssBoxShadow`/`CssShadow`
    (`src/Starling.Css/Values/CssBoxShadow.cs`) + fail-soft
    `CssBoxShadowParser` (`CssBoxShadowParser.cs`): offset-x/y, optional
    blur (non-negative) + spread, optional color (null = currentColor
    sentinel), optional `inset`, comma-separated multi-layer. Layer splitting
    keys on the value parser's empty-name comma keyword.
  - **Paint records (appended to `DisplayItem.cs`):** `CornerRadii` struct,
    `FillRoundedRect`, `StrokeRoundedRect`, `DrawBoxShadow`.
  - **Builder (`PaintBoxAndChildren` + `EmitBorders` only):** reads the four
    `border-*-radius` longhands, clamps overlaps (§5.1), emits a
    `FillRoundedRect` background when rounded; emits outer box-shadow layers
    behind the box (back-to-front, currentColor resolved against the box
    `color`); paints a uniform rounded border as a single centre-line
    `StrokeRoundedRect`.
  - **Backend:** rounded path via `PathBuilder` cubic-bezier corners; box
    shadow rasterized into an offscreen transparent `Image<Rgba32>`,
    `GaussianBlur(σ=blur/2)`, blitted via `canvas.DrawImage` so the device
    scale composes through the canvas transform.
  - **LayerTreeBuilder:** added the three new records to `TryItemBounds` so
    compositor layer AABBs cover rounded fills + shadow spread/blur/offset.
  - **Tests:** `CssBoxShadowParserTests` (9, Css.Tests), promoted
    `BorderRadiusTests` to 2 `[SpecFact]` + new `BoxShadowTests` (4
    `[SpecFact]`, Css.Spec.Tests), `RoundedRectAndShadowTests` (3 pixel
    probes, Paint.Tests).
  - **Golden:** `testdata/golden/snapshots/nginx.org.png` regenerated — nginx
    CSS uses `box-shadow` + `border-radius:3px`, previously no-ops; the new
    render is correct (rounded logo corners, soft drop shadow), verified
    visually.
  - **Deferred (documented gaps):** inset shadows (parsed, not painted);
    per-side mixed (different width/color) rounded borders fall back to square
    strokes; elliptical per-corner rx/ry from the slash syntax (the longhands
    store a single length here); percentage radii use min(width,height) as a
    circular approximation; box-shadow `currentColor` resolves to the box's
    own `color` (no inheritance edge cases).
