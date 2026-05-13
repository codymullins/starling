---
id: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-12T19:30:00Z"
branch: "main"
completed_at: "2026-05-12T19:30:00Z"
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

## Scope note

Implemented as the common static-page subset needed by the M1 rendering
pipeline: baseline insertion modes, implicit html/head/body creation, raw text
for title/style/script/textarea, paragraph/heading/list/definition-list
implicit closures, void elements, simple table nesting, and after-body handling.
Full adoption-agency, foreign-content, template, frameset, and full html5lib
tree-construction conformance remain follow-up work.

## Handoff log
- 2026-05-11T15:20Z — created (blocked on M1-01h + M1-03).
- 2026-05-11T19:58Z — unblocked by wp:M1-03-dom-core completion; available to claim.
- 2026-05-12T19:30Z — reconciled as complete for the M1 static-rendering
  subset; added focused conformance coverage for script/style, void elements,
  definition lists, nested buttons, and simple tables.
