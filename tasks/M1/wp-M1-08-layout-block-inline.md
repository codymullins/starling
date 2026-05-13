---
id: "wp:M1-08-layout-block-inline"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-12T19:30:00Z"
branch: "main"
completed_at: "2026-05-12T19:30:00Z"
depends_on:
  - "wp:M1-07-css-cascade"
  - "wp:M0-03-paint-stub"
blocks:
  - "wp:M1-09-paint-display-list"
subsystem: "Tessera.Layout"
plan_refs:
  - "browser-plan/07_LAYOUT.md#block-formatting-context-bfc"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-08-layout-block-inline"
---

# wp:M1-08 — Block + inline layout

## Goal
Block + inline formatting; margin collapse; basic text wrap; sizing.

## Acceptance
20 golden tests pass (paragraphs, headings, lists, nested divs, margin
collapse, text wrap).

## Scope note

Implemented for the M1 static pipeline: block stacking, anonymous block
wrapping for inline runs, basic text wrapping, box model sizing, adjacent
vertical margin collapse, display:none/display:contents, inherited text
properties, and start/center/right inline alignment. Floats, positioning, flex,
grid, tables as layout algorithms, and advanced white-space behavior remain
deferred.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-12T19:30Z — reconciled as complete for the M1 static-rendering
  subset; layout tests plus the 20-case GoldenImage suite cover the accepted
  paragraph/heading/list/background/margin/text-wrap cases.
