---
id: wp:M5-css-12-text-3-inline
milestone: M5
status: "complete"
claimed_by: "agent-claude-cody-text3"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T23:30:00Z"
branch: "worktree"
depends_on:
  - wp:M1-08-layout-block-inline
subsystem: Starling.Layout
plan_refs:
  - browser-plan/07_LAYOUT.md#inline-formatting
  - browser-plan/06_CSS.md
---

# wp:M5-css-12 — CSS Text 3 inline text properties

## Goal

Apply the CSS Text Module Level 3 inline properties that are already in the
`PropertyId` enum but currently parsed-and-ignored. Today inline layout
(`src/Starling.Layout/Inline/InlineLayout.cs`) reads only `text-align` and
`line-height`; `NormalizeWhitespace` **always collapses** runs, so
`white-space: pre/pre-wrap/pre-line` does not preserve whitespace, and
`text-transform`, `letter-spacing`, `word-spacing`, `text-indent`, `tab-size`,
`overflow-wrap`, and `word-break` have no effect. Make them real.

## Inputs

- `src/Starling.Layout/Inline/InlineLayout.cs` — `NormalizeWhitespace` (~798),
  `SplitToWords` (~820), the line-breaking loop (~112–145), `ResolveAlign`
  (~598), `ResolveFontSize`/`ResolveLineHeight` (~840+). This is the file you
  own; it is disjoint from the paint WPs running concurrently.
- `src/Starling.Css/Cascade/ComputedStyle.cs` — `Get(PropertyId)` / `GetColor`.
- `src/Starling.Css/Properties/PropertyRegistry.cs` — confirm/extend typed
  parse + UA defaults for the properties below (most already tokenize).
- `PropertyId`: `WhiteSpace`, `WhiteSpaceCollapse`, `TextWrap`, `TextTransform`,
  `LetterSpacing`, `WordSpacing`, `TextIndent`, `TabSize`, `OverflowWrap`,
  `WordBreak`, `LineBreak`, `TextAlignLast`.

## Outputs

- `white-space` honored end-to-end: `normal`/`nowrap` collapse; `pre`/`pre-wrap`
  preserve spaces+newlines (forced line breaks at `\n`); `pre-line` collapses
  spaces but keeps newlines; `nowrap`/`pre` suppress soft wrapping. Wire the
  modern `white-space-collapse` + `text-wrap` longhands to the same engine.
- `text-transform: uppercase | lowercase | capitalize | none` applied to text
  runs (culture-invariant; `capitalize` at word boundaries).
- `letter-spacing` / `word-spacing` add advance between glyphs / at spaces and
  feed into measured run width and `DrawText` positioning.
- `text-indent` indents the first line of a block container.
- `tab-size` expands `\t` in preserved-whitespace contexts.
- `overflow-wrap: anywhere|break-word` and `word-break: break-all|keep-all`
  affect the soft-wrap break opportunities when a word overflows the line.
- Tests: `tests/Starling.Layout.Tests/` behavioral cases; mirror typed-parse
  asserts into `tests/Starling.Css.Spec.Tests/CssText3/` (create folder),
  `[Spec("css-text-3", "https://www.w3.org/TR/css-text-3/", §)]` +
  `[SpecFact]`. Promote any matching `[PendingFact]` stubs.

## Acceptance

- `dotnet build && dotnet test` green (sandbox: `-p:UseAppHost=false`, repo-root
  `sixlabors.lic`).
- A `white-space: pre` block preserves two consecutive spaces and a newline in
  the laid-out fragments; a `normal` block collapses them.
- `text-transform: uppercase` produces uppercased fragment text; width grows
  with `letter-spacing`.
- `text-indent: 2em` offsets the first line's start x by 2×font-size.
- A long unbreakable token wraps under `overflow-wrap: anywhere`.

## Notes

- Keep all edits inside `Starling.Layout` + `Starling.Css` parse hooks. Do **not**
  touch `Starling.Paint` display-list/backend files — three sibling paint WPs
  are editing those concurrently.
- `DrawText` already carries text+position; transformed/spaced text just changes
  the string and advances you emit, so no display-item schema change is needed.

## Handoff log

- 2026-05-20 — created + claimed by agent-claude-cody-text3 (orchestrated batch).
- 2026-05-20 — implemented in `src/Starling.Layout/Inline/InlineLayout.cs`:
  rewrote `LayoutText` into a white-space-aware tokenizer (`Tokenize`) +
  `LayoutToken`/`LayoutBrokenWord` pipeline that preserves the per-run
  shape-once-and-slice optimisation. New `WhiteSpaceMode.Resolve` reads the
  legacy `white-space` keyword and the modern `white-space-collapse` +
  `text-wrap` longhands; `TextTransformer` (none/upper/lower/capitalize, culture-
  invariant) rewrites text before shaping; `letter-spacing`/`word-spacing` feed
  the advance; `text-indent` offsets the first line only (em vs. font-size, %
  vs. available width); `tab-size` expands `\t` at tab-stops in preserved
  contexts; `overflow-wrap: anywhere|break-word` and `word-break: break-all`
  drive per-character emergency breaking of an over-long token. In
  `src/Starling.Css/Properties/PropertyRegistry.cs`, `white-space` now expands
  to the `white-space-collapse` + `text-wrap` longhands (CSS Text 4 §3) while
  keeping the legacy longhand set (BoxTreeBuilder's whitespace-collapse check
  still reads it). Tests: `tests/Starling.Layout.Tests/CssText3InlineTests.cs`
  (19 behavioral cases) + `tests/Starling.Css.Spec.Tests/CssText3/PropertyTests.cs`
  (19 typed-parse `[SpecFact]`s) + `_spec.md`. Layout.Tests 155/155, Css.Spec
  72 pass/13 skip (incl. 19 new), Css.Tests 505/1-skip, Paint.Tests 138/138
  (fixed the 3 inline-shape-optimisation regressions by shaping the whole
  segment once and slicing), Engine.Tests 122/122. NOTE: started in a worktree
  branched from a stale pre-scaffold commit (9d3ec43); reset the branch to the
  current `main` (40d1f02, which contains this WP file) since the branch had
  zero unique commits. `Starling.Gui.Headless.Tests` cannot build under the
  sandbox `-p:UseAppHost=false` flag (xUnit v3 needs an apphost) — pre-existing
  sandbox limitation, unrelated. Completed by agent-claude-cody-text3.
