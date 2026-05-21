---
id: css-text-3
url: https://www.w3.org/TR/css-text-3/
title: CSS Text Module Level 3
status: CR
fetched: 2026-05-20
source: https://www.w3.org/TR/css-text-3/
generated: false
definition_counts:
  properties: 9
  at_rules: 0
  selectors: 0
---

# CSS Text Module Level 3

Spec: <https://www.w3.org/TR/css-text-3/>

Conformance tests in this folder are hand-written and tagged with
`[Spec(...)]` plus `[SpecFact]` (passes today). They cover the typed parsing /
shorthand expansion of the inline text properties applied by
`Starling.Layout.Inline.InlineLayout` for `wp:M5-css-12-text-3-inline`:

- `white-space` (and the CSS Text 4 `white-space-collapse` + `text-wrap`
  longhands it expands to)
- `text-transform`
- `letter-spacing`, `word-spacing`
- `text-indent`
- `tab-size`
- `overflow-wrap`, `word-break`
