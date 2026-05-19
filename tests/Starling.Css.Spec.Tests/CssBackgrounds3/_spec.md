---
id: css-backgrounds-3
url: https://www.w3.org/TR/css-backgrounds-3/
title: CSS Backgrounds and Borders Module Level 3
status: CR
fetched: 2026-05-19
sections_covered:
  - "3 — Backgrounds"
  - "4 — Borders"
  - "5 — Rounded Corners (border-radius)"
  - "7 — Box Shadow"
implementation:
  module: src/Starling.Css (parsing) + src/Starling.Paint (rendering)
  status: minimal
  notes: |
    Parsing exists for the basic longhand properties; the `background` and
    `border` shorthands, multi-layer backgrounds, and full `border-radius` /
    `box-shadow` parsing are not yet covered by dedicated spec tests.
---

# CSS Backgrounds and Borders 3

Spec: <https://www.w3.org/TR/css-backgrounds-3/>

## Section map

| Section | Covered by |
|---|---|
| 3.4 — `background` shorthand | `BackgroundShorthandTests` |
| 3.5 — Multi-layer backgrounds | `BackgroundShorthandTests` |
| 3.10 — `background-position` | (not started) |
| 4.x — Border longhands + `border` shorthand | `BorderShorthandTests` |
| 5 — `border-radius` | `BorderRadiusTests` |
| 7 — `box-shadow` | `BoxShadowTests` |
