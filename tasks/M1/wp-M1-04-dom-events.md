---
id: "wp:M1-04-dom-events"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-03-dom-core"
blocks:
  - "wp:M4-02-dom-bindings-core"
subsystem: "Tessera.Dom"
plan_refs:
  - "browser-plan/05_DOM.md#events"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-04-dom-events"
---

# wp:M1-04 — DOM events

## Goal
EventTarget, Event, addEventListener/dispatchEvent, capture/target/bubble.

## Acceptance
WPT `dom/events/**` (core dispatch) ≥ 95%.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T19:58Z — unblocked by wp:M1-03-dom-core completion; available to claim.
