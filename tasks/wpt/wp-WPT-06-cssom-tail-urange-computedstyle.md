---
id: WPT-06
title: CSSOM tail — urange canonicalization + getComputedStyle serialization
status: in_progress
area: wpt / css / cssom
baseline: 27.79% (1459/5250, dom,css,url, sha-pinned, post-WP-01)
---

## Goal
Close the two deferred CSSOM tails from WP-01:
1. **`<urange>` token canonicalization** (CSS Syntax §4.3.10) — so
   `css/css-syntax/urange-parsing.html` (~85 subtests) passes.
2. **Spec-correct value serialization on `getComputedStyle` /
   custom-property reads** — so `css/css-syntax/serialize-consecutive-tokens.html`
   (~72) and `css/css-syntax/declarations-trim-whitespace.html` (~9) pass.

## Why these tests fail today (measured)
- `urange-parsing.html` (and its subset failures): `<urange>` isn't a normal
  number token — it's a special grammar (`U+0..FF`, `U+0-FF`, `U+0?-FF`) that
  WP-01's `CssValueSerializer` doesn't canonicalize. They show up as
  `setProperty(unicode-range, "U+...")` round-trips returning the wrong text.
- `serialize-consecutive-tokens.html` / `declarations-trim-whitespace.html`:
  these read serialized values back via `getComputedStyle(el).getPropertyValue
  ('--foo')` — custom-property values get their declared cssText, not their
  raw text. Our `getComputedStyle` doesn't surface the *declared* value text
  correctly (or doesn't expose custom properties at all).

Predicted Δ: **~120–160** (urange ~70 of 85 + computed/custom ~55 of 81 after
WP-01 dilution lessons; verify against causes.txt before committing scope).

## Scope (in)
1. **`<urange>` recognizer + serializer** (CSS Syntax §4.3.10):
   - Detect `unicode-range` (and any property whose grammar contains `<urange>`)
     in `CssValueSerializer` / the property-value parser.
   - Parse `U+<hex>`, `U+<hex>-<hex>`, `U+<hex>?+` (question-mark form).
   - Canonical serialization per CSSOM §6.7.3 (lowercase `u+`, no leading
     zeros in trivial cases per the spec — match WPT expectations).
2. **`getComputedStyle` value-text path** for **declared property reads**:
   - `getPropertyValue('--foo')` on a getComputedStyle decl must return the
     *declared* value text (after value canonicalization, per CSSOM §6.7.4).
   - `serialize-consecutive-tokens` checks consecutive idents/numbers get the
     right whitespace insertion in `cssText` round-trips.
   - `declarations-trim-whitespace` checks declaration value text is trimmed
     per CSS Syntax §5.4.4.
3. Reuse WP-01's `CssomDeclarationBlock` / `CssValueSerializer` — extend, don't
   parallel-build.

## Scope (out)
- Full cascade re-trigger / layout invalidation. `getComputedStyle` reads the
  matched declared value for these tests, not actual layout-resolved values.
- New @-rule support (still out per WP-01).

## Acceptance
- Measured Δ on full suite; report `pass X→Y` and `css/css-syntax A%→B%`.
  Predict-then-verify.
- `urange-parsing.html`, `serialize-consecutive-tokens.html`,
  `declarations-trim-whitespace.html` reach high pass rate (don't need 100%
  unless cheap — partial conversion is fine if attributed in PLAN.md).
- No regression in existing WP-01 css-syntax passes (135 stay passing).
- MSTest extensions to `CssomValueTests`/new `UrangeTests`: round-trip the
  WPT urange tables exactly; spec-test custom-property declared-value reads.
- PLAN.md status log; WP doc → `complete`.

## Notes (recon)
- WP-01 commit `c92c129` (on `main`):
  `src/Starling.Css/Cssom/CssValueSerializer.cs` is where `<urange>`
  recognition belongs.
- `getComputedStyle` lives in `src/Starling.Bindings/WindowBinding.cs`
  (per WP-01 recon, ~line 600+) — the *declared-value path* is what needs to
  surface canonical text.
- CSS Syntax §4.3.10 is short — port the grammar literally. Don't invent.
