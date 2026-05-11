---
id: "wp:M1-05-css-tokenizer-parser"
milestone: "M1"
status: "claimed"
claimed_by: "agent-test-smoke"
claimed_at: "2026-05-11T15:31:10Z"
branch: "wp-M1-05-css-tokenizer-parser"
depends_on:
  - "wp:M0-02-common"
blocks:
  - "wp:M1-06-css-selectors"
  - "wp:M1-07-css-cascade"
subsystem: "Tessera.Css"
plan_refs:
  - "browser-plan/06_CSS.md#tokenizer"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-05-css-tokenizer-parser"
---

# wp:M1-05 — CSS tokenizer + parser

## Goal
CSS Syntax Module Level 3 tokenizer and parser; produces a tree of rules,
declarations, and component values.

## Outputs
- `src/Tessera.Css/Tokenizer/*`
- `src/Tessera.Css/Parser/*`

## Acceptance
WPT `css/css-syntax/**` ≥ 80%.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T15:31:10Z — claimed by agent-test-smoke, branch `wp-M1-05-css-tokenizer-parser`
