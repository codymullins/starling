---
id: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-01h-tokenizer-html5lib"
  - "wp:M1-03-dom-core"
blocks:
  - "wp:M1-08-layout-block-inline"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-02-html-tree-builder"
---

# wp:M1-02 — HTML tree builder

## Goal
WHATWG tree construction: all insertion modes, OpenElementStack, ActiveFormattingElements,
the adoption agency algorithm, foreign content handling.

## Acceptance
- html5lib tree-construction suite ≥ 95%.
- Adoption agency cases in `tests1.dat` 100%.

## Handoff log
- 2026-05-11T15:20Z — created (blocked on M1-01h + M1-03).
- 2026-05-11T19:58Z — unblocked by wp:M1-03-dom-core completion; available to claim.
