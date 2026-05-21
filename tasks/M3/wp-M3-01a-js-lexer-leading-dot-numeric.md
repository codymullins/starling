---
id: "wp:M3-01a-js-lexer-leading-dot-numeric"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-21T00:00:00Z"
branch: "main"
completed_at: "2026-05-21T00:00:00Z"
depends_on:
  - "wp:M3-01-js-lexer"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#lexer"
---

# wp:M3-01a — JS lexer: leading-dot numeric literal

## Goal

Fix the JS lexer so that numeric literals beginning with a decimal point
(e.g. `.5`, `.25e3`, `.0`) are scanned as `NumericLiteral` tokens instead of
being mis-tokenized as a `Dot` punctuator. Real-world bundles (mcmaster.com
app bundle) use `.5`-style literals and previously threw:

```
engine.js: Script compile/run failure (<inline>): unexpected token Dot '.' (at 1:7)
```

## Spec reference

ECMAScript §12.9.3 `DecimalLiteral` production:
`. DecimalDigits ExponentPart?`
https://tc39.es/ecma262/#prod-DecimalLiteral

## Fix

**`src/Starling.Js/Lex/JsLexer.cs`** — two changes:

1. In `ScanPunctuator`, before the 3-char punctuator checks: when `c == '.'`
   and `p1` is an ASCII digit (0–9), delegate to `ScanLeadingDotNumber`.
   The check is ordered before `...` (Ellipsis) because `...` requires
   `p1 == '.'`, not a digit — no conflict.

2. New private method `ScanLeadingDotNumber`: consumes `.`, scans fractional
   `DecimalDigits`, and optionally an `ExponentPart` (`[eE][+-]?Digits`).
   Mirrors the existing fractional-part logic in `ScanNumber`.

No changes to `JsTokenKind.cs` (no new token kind needed).

## Tests added (16 new)

All in `tests/Starling.Js.Tests/Lex/JsLexerTests.cs`, tagged
`[Spec("ecma262", "...", "12.9.3 Numeric Literals")]` + `[SpecFact]`:

- `Leading_dot_simple_fraction` — `.5` → NumericLiteral 0.5
- `Leading_dot_multi_digit_fraction` — `.25` → 0.25
- `Leading_dot_with_exponent` — `.25e3` → 250.0
- `Leading_dot_with_negative_exponent` — `.5e-1` → 0.05
- `Leading_dot_zero` — `.0` → 0.0
- `Leading_dot_in_expression_context` — `(.5).toFixed(1)` tokenizes correctly
- `Leading_dot_in_var_initializer` — `var x = .5;` tokenizes correctly

No-regress (ensure existing behavior preserved):
- `Member_access_dot_not_followed_by_digit_is_Dot_punctuator`
- `Chained_member_access`
- `Ellipsis_spread_not_affected_by_leading_dot_fix`
- `Normal_float_1_dot_5_still_works`
- `Normal_float_0_dot_5_still_works`
- `Trailing_dot_still_works` — `3.` → 3.0
- `Dot_at_end_of_source_is_Dot_punctuator`
- `Dot_followed_by_non_digit_is_Dot_punctuator`
- `Double_dot_member_call_1_dot_dot_toString`

## Handoff log

- 2026-05-21T00:00Z — created + completed by agent-claude-cody.
  Full `Starling.Js.Tests`: 1248/1249 green (1 pre-existing skip).
  Baseline was 1232; +16 new tests.
  End-to-end `.5` repro confirmed renders without compile failure.
