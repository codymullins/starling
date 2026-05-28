---
id: css-counter-styles-3
url: https://www.w3.org/TR/css-counter-styles-3/
title: CSS Counter Styles Level 3
status: CR
generated: false
---

# CSS Counter Styles Level 3

Spec: <https://www.w3.org/TR/css-counter-styles-3/>

Conformance tests in this folder are written by hand and tagged with
`[Spec(...)]` plus either `[SpecFact]` (passes today) or `[PendingFact]`
(a known gap).

Implemented (wp:spec-css-counter-styles-3):

- The `@counter-style <name> { ... }` at-rule, parsed into a descriptor model
  (`CounterStyleRule`) by `CounterStyleParser`.
- Descriptors: `system`, `symbols`, `additive-symbols`, `negative`, `prefix`,
  `suffix`, `range`, `pad`, `fallback`, plus `extends <name>`.
- The counter generation algorithm in `CounterStyleResolver` for the systems
  `cyclic`, `fixed`, `symbolic`, `alphabetic`, `numeric`, `additive`, and
  `extends`, with range and fallback handling.
- The predefined styles `decimal`, `decimal-leading-zero`, `lower-roman`,
  `upper-roman`, `lower-alpha`/`lower-latin`, `upper-alpha`/`upper-latin`,
  `lower-greek`, and the glyph styles `disc`/`circle`/`square`.
- `StyleEngine.CounterStyles` exposes a resolver built from the attached
  stylesheets so the cascade can render markers.
