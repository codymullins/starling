---
id: "wp:M1-04-dom-events"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-12T19:30:00Z"
branch: "main"
completed_at: "2026-05-12T19:30:00Z"
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
- 2026-05-12T19:30Z — reconciled as complete: `Tessera.Dom.Events`
  provides EventTarget/Event dispatch with capture, target, bubble, once,
  cancelation, propagation stops, listener removal, and MouseEvent/CustomEvent
  coverage in `DomEventTests`.
