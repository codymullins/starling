---
id: css-cascade-5
url: https://www.w3.org/TR/css-cascade-5/
title: CSS Cascading and Inheritance Level 5 — behavioral cascade suite
tracking: wp:spec-css-cascade-5
status: 🟢
---

# CSS Cascading and Inheritance Level 5

Spec: <https://www.w3.org/TR/css-cascade-5/>

This folder holds the **behavioral** cascade conformance suite (spec id `css-cascade-5`).
It tests the cascade algorithm itself via `StyleEngine.Compute` — origins, specificity,
@layer ordering, CSS-wide keywords, and inheritance — rather than property parsing.

The sibling `CssCascade/` folder (tagged `css-cascade`) covers property parsing.
Do NOT modify that folder; it tracks a different spec snapshot.

Add a test when you implement or discover a requirement. Flip `[PendingFact]` to
`[SpecFact]` once it passes.
