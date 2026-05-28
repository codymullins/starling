---
id: "wp:spec-css-v1-behavior-layer"
milestone: "ongoing"
status: "blocked"
claimed_by: ""
claimed_at: ""
subsystem: "Starling.Layout / Starling.Paint / Starling.Css.Cascade"
depends_on:
  - "wp:spec-css-v1-systemic-fixes"
plan_refs:
  - "tasks/SPEC_COVERAGE.md"
  - "browser-plan/06_CSS.md"
---

# wp:spec-css-v1-behavior-layer

Most CSS-V1 specs are 🟢: their parse + cascade conformance passes, but the
**layout/paint behavior** they describe is not yet implemented or tested, so
they are not ✅. This WP drives the behavior layer so the 🟢 specs reach ✅.
Large — split into per-area sub-WPs as work is claimed. Several parsed-but-inert
at-rules also need wiring into the engine here (see "Wiring" below).

## Wiring (parsed but behavior-inert today)

These have parsers but are not consumed by `StyleEngine`/layout. The seam is the
`AddStyleSheet`/`RemoveStyleSheet` → `RebuildX()` lifecycle (see
`RebuildCounterStyles` / the new `RebuildRegisteredProperties` as templates):

- **`@property`** — collection is wired (`StyleEngine.RegisteredProperties`).
  Remaining: use a registered property's `initial-value` when unset, and honor
  its `inherits` descriptor in the custom-property cascade (today all custom
  props inherit). Highest-leverage; builds on the var() machinery.
- **`@scope`** (`ScopeParser`) — wire scope-start/end bounds into selector
  matching + the proximity step in `Cascade`.
- **`@container`** (`ContainerQueryParser`) — evaluate the parsed condition
  against a container's size (the `ContainerSizeLookup` hook already exists) and
  conditionally apply the inner rules.
- **`@view-transition`** (`ViewTransitionParser`) — hook `navigation` to the
  navigation/paint path (depends on view-transition capture).
- **`@container`** size queries already EVALUATE (`StyleEngine.ContainerQueryMatches`);
  what remains is **named-container matching** (the lookup only reports size, not
  `container-name` — needs the engine to expose ancestor container-names) and
  **`style()` queries**.

## Scoped next step — intrinsic block sizing (Sizing 3, fully analyzed)

`width: min-content | max-content | fit-content | fit-content(<len>)` all PARSE
(stored as `CssKeyword`/`CssFunctionValue`) but `BlockLayout.ContentWidth`
(BlockLayout.cs ~439) sends unknown width keywords through the `_ => null` arm of
`ResolveLength`, i.e. they currently fill like `auto` (correct only for
`stretch`). To resolve them:
- Reuse the flex pattern (`FlexLayout.NaturalWidth`): lay the box out at a huge
  width with `measure: true`, then read its content extent — that is
  **max-content**; lay out at width 0 for **min-content**; `fit-content` =
  `clamp(min-content, stretch, max-content)`.
- The hard part is **re-entrancy**: `ContentWidth` runs during layout, so the
  intrinsic measure must use a non-re-entrant measurement helper (don't re-trigger
  width resolution). Add `BlockLayout.MeasureIntrinsicWidth(box, mode)` and call it
  from `ContentWidth` only when `width` is an intrinsic keyword.
- Tests MUST live in `Starling.Layout.Tests` (assert box dimensions) — the
  `Starling.Css.Spec.Tests/CssSizing3/` project references only `Starling.Css` and
  cannot exercise layout, so its current `Layout_resolves_*` PendingFacts are
  parse-only placeholders. Add `[Spec("css-sizing-3")]` behavioral tests in the
  layout project, then this drives sizing-3 (and intrinsic sizing for flex/grid)
  toward ✅. Verify the full 211-test layout suite stays green (core width path).

## Behavior areas (each → per-spec ✅)

- **Layout behaviors:** vertical writing-mode layout (writing-modes-4),
  multi-column fragmentation (multicol-1), scroll-snap positioning
  (scroll-snap-1), size containment placeholders (sizing-4 / contain-2),
  float-area shaping (shapes-1), anchor-relative positioning (anchor-position-1),
  baseline shift (inline-3), `text-wrap: balance/pretty` (text-4).
- **Paint behaviors:** filter rendering (filter-effects-1), mask painting
  (masking-1), blend-mode compositing (compositing-1), scrollbar painting
  (scrollbars-1), outline + text-overflow rendering (ui-4), 3D transform
  resolution (transforms-2).
- **Value behaviors:** `color-mix()` (color-5), conic gradients + `object-fit`
  (images-3), `image-set()` resolution + `cross-fade()` compositing (images-4).

## Done when

Each listed 🟢 spec gains behavioral `[Spec]` SpecFacts (layout box / paint /
computed-value assertions) and moves to ✅; the wired at-rules affect the cascade
/ layout / paint as specified. Depends on `wp:spec-css-v1-systemic-fixes` so
value validation + comma-splitting are in place first.
