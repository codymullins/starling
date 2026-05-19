---
id: wp:M5-css-01-transform-value
milestone: M5
status: complete
claimed_by: agent-copilot-claude-opus-4.7
claimed_at: 2026-05-19T02:23Z
branch: main
completed_at: 2026-05-19T02:23Z
depends_on:
  - wp:M1-05-css-tokenizer-parser
  - wp:M1-07-css-cascade
blocks:
  - wp:M5-css-02-transform-paint
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-01-transform-value — CSS `transform` value type + parser

## Goal

Add a strongly-typed representation for the CSS `transform` property and a parser
that turns the generic `CssValue` tree produced by `CssValueParser` into a
composed 2D affine matrix. 3D transforms are deliberately out of scope (they
need a 4×4 stack and a stacking-context flatten step).

## Inputs

- `Tessera.Css.Values.CssValueParser` — produces `CssFunctionValue` / `CssValueList`
  for each transform function in the declaration.
- `Tessera.Css.Values.CssLength` / `CssPercentage` / `CssNumber` / `CssAngle` —
  numeric argument types.

## Outputs

- `src/Starling.Css/Values/Matrix2D.cs` — `readonly record struct Matrix2D`
  with `Identity`, `Translate`, `Scale`, `Rotate`, `Skew`, `Multiply`, `Transform`.
- `src/Starling.Css/Values/CssTransform.cs` — `CssTransform : CssValue` plus
  `CssTransformFunction` hierarchy (`CssTranslate`, `CssScale`, `CssRotate`,
  `CssSkew`, `CssMatrix`) and `CssLengthOrPercent` for translate args.
- `src/Starling.Css/Values/CssTransformParser.cs` — `Parse(CssValue) → CssTransform`
  and `TryParseFunction(CssFunctionValue, out CssTransformFunction)`.
- `tests/Starling.Css.Tests/CssTransformParserTests.cs` — 14 tests covering
  translate (incl. percent + reference-box resolution), scale, rotate (deg/rad/turn),
  skew, matrix(), function-list composition, 3D rejection, unknown-function rejection,
  arity validation.

## Acceptance

- `Parse("translate(50%, 25%)").ToMatrix(200, 80)` returns `Matrix2D.Translate(100, 20)`.
- `Parse("translate(10px, 20px) scale(2)").ToMatrix(0,0).Transform(3, 4)` returns
  `(16, 28)` — confirming left-to-right composition.
- All 3D variants (`translate3d`, `rotateX`, `matrix3d`, `perspective`) fail-soft
  to `CssTransform.None`.
- 14/14 new tests pass.
- `dotnet build src/Starling.Css/Starling.Css.csproj` is green.

## Notes

- The transform property isn't yet **applied** anywhere — that's `wp:M5-css-02-transform-paint`.
  This package just establishes the value type and parser so layout/paint have a
  structured input to consume.
- `Matrix2D.Multiply(other)` is **`this × other`** — so `this` is the outer
  transform and `other` applies first. `CssTransform.ToMatrix` iterates the
  function list and post-multiplies each one onto the accumulator, matching the
  CSS Transforms 1 §6.1 rule that the leftmost function maps the box's local
  coordinate system first.
- Relative units (em/rem/vh) inside `translate(...)` currently resolve to 0
  because we don't have a resolution context at parse time. Wire a
  `CssResolutionContext` overload when paint integration lands.

## Handoff log

- 2026-05-19T02:23Z — created and completed in one session (agent-copilot-claude-opus-4.7)
