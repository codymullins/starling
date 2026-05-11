---
id: "wp:M1-06-css-selectors"
milestone: "M1"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-05-css-tokenizer-parser"
  - "wp:M1-03-dom-core"
blocks:
  - "wp:M1-07-css-cascade"
subsystem: "Tessera.Css"
plan_refs:
  - "browser-plan/06_CSS.md#selectors"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-06-css-selectors"
---

# wp:M1-06 — CSS selectors

## Goal
Selectors Level 4 (v1 subset: no `:has`, no full `:nth-*`).

## Acceptance
WPT `css/selectors/**` ≥ 80% on the v1 subset.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T19:58Z — wp:M1-03-dom-core completed; still blocked on wp:M1-05-css-tokenizer-parser.
