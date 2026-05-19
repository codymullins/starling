---
id: css-color-5
url: https://www.w3.org/TR/css-color-5/
title: CSS Color Level 5
status: WD
fetched: 2026-05-19
sections_covered:
  - "3 — Mixing colors: the color-mix() function"
  - "4 — Relative color syntax"
implementation:
  module: src/Starling.Css/Values (ColorParser.cs, ColorConversion.cs)
  status: partial
  notes: |
    color-mix() parsing and interpolation are partially covered by
    ColorMixHueHintTests in Starling.Css.Tests. Relative color syntax
    (rgb(from c r g b), oklch(from c l c h)) is not implemented.
---

# CSS Color Level 5

Spec: <https://www.w3.org/TR/css-color-5/>

## Section map

| Section | Covered by |
|---|---|
| 3 — `color-mix()` | `ColorMixTests` |
| 4 — Relative color syntax | `RelativeColorSyntaxTests` |
| 5 — `color-contrast()` | (not started) |
