---
id: css-variables-1
url: https://www.w3.org/TR/css-variables-1/
title: CSS Custom Properties for Cascading Variables Level 1
status: CR
fetched: 2026-05-19
sections_covered:
  - "2 — Defining Custom Properties: the --* family of properties"
  - "3 — Cascading Variables: the var() notation"
  - "3.1 — Invalid Variables"
  - "3.2 — Guaranteed-Invalid Values"
implementation:
  module: src/Starling.Css (Values/CssValueParser.cs, Cascade/StyleEngine.cs)
  status: partial
  notes: |
    Basic `--x: <value>` storage and `var(--x, fallback)` substitution are wired
    in StyleEngine. Cycle detection, IACVT (invalid-at-computed-value-time)
    fallback to the unset value, and registered properties (@property) are not
    yet implemented.
---

# CSS Custom Properties Level 1

Spec: <https://www.w3.org/TR/css-variables-1/>

## Section map

| Section | Covered by |
|---|---|
| 2 — `--*` parsing | `CustomPropertyParsingTests` |
| 3 — `var()` substitution | `VarSubstitutionTests` |
| 3.1 — Invalid variables (IACVT) | `InvalidVariableTests` |
| 3.2 — Guaranteed-invalid value | `GuaranteedInvalidValueTests` |
| 3.3 — Cycles | `VarCycleDetectionTests` |
