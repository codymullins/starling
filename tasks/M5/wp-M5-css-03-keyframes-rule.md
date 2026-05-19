---
id: wp:M5-css-03-keyframes-rule
milestone: M5
status: complete
claimed_by: agent-copilot-claude-opus-4.7
claimed_at: 2026-05-19T02:23Z
branch: main
completed_at: 2026-05-19T02:23Z
depends_on:
  - wp:M1-05-css-tokenizer-parser
blocks:
  - wp:M5-css-05-animations-engine
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-03-keyframes-rule — `@keyframes` typed at-rule

## Goal

Add a strongly-typed `KeyframesRule` representation and a parser that extracts
them from a parsed stylesheet. Mirrors the existing `FontFaceParser` pattern.

## Inputs

- `Tessera.Css.Parser.CssParser` — already routes `@keyframes` through the
  generic nested-rule `AtRule` branch, so the bodies are parsed as `StyleRule`s.
- `Tessera.Css.Values.CssValueParser` — turns each declaration's tokens into a `CssValue`.

## Outputs

- `src/Starling.Css/Animations/KeyframesRule.cs` — `KeyframesRule`, `Keyframe`,
  `KeyframeDeclaration` records.
- `src/Starling.Css/Animations/KeyframesParser.cs` — `ParseAll(StyleSheet)`
  and `TryParse(AtRule, out KeyframesRule?)`.
- `tests/Starling.Css.Tests/KeyframesParserTests.cs` — 7 tests covering
  `from`/`to`, percentage selectors, grouped selectors (`0%, 100%`),
  out-of-range pruning, vendor-prefixed aliases (`-webkit-keyframes`,
  `-moz-keyframes`), multi-rule stylesheets, and anonymous-at-rule rejection.

## Acceptance

- `@keyframes fade { from { opacity: 0 } to { opacity: 1 } }` parses to one
  rule named `"fade"` with frames at offsets `[0.0, 1.0]`.
- `@keyframes pulse { 0%, 100% { ... } 50% { ... } }` produces 3 frames
  sharing declarations between the 0% and 100% entries.
- Out-of-range selectors (`-10%`, `150%`) are dropped.
- 7/7 new tests pass.

## Notes

- `!important` inside a keyframe is ignored per CSS Animations 1 §4.1 — the
  declaration is kept but its importance bit is lost (we don't surface it in
  `KeyframeDeclaration`).
- Frames are stably sorted by offset so consumers can step linearly.
- The rules are not yet **applied** anywhere — that's `wp:M5-css-05-animations-engine`.

## Handoff log

- 2026-05-19T02:23Z — created and completed in one session (agent-copilot-claude-opus-4.7)
