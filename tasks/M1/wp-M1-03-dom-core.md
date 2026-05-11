---
id: "wp:M1-03-dom-core"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T15:21:34Z"
branch: "wp-M1-03-dom-core"
completed_at: "2026-05-11T19:58:00Z"
depends_on:
  - "wp:M0-02-common"
blocks:
  - "wp:M1-02-html-tree-builder"
  - "wp:M1-04-dom-events"
  - "wp:M1-06-css-selectors"
  - "wp:M4-02-dom-bindings-core"
subsystem: "Tessera.Dom"
plan_refs:
  - "browser-plan/05_DOM.md"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-03-dom-core"
---

# wp:M1-03 — DOM core (Node / Element / Document / Text / Comment / Attr)

## Goal
Full Node/Element/Document hierarchy with mutation primitives.

## Outputs
- `src/Tessera.Dom/{Node,NodeKind,Element,Document,Text,Comment,Attr,NamedNodeMap,DocumentFragment}.cs`

## Acceptance
WPT `dom/nodes/Node-*` ≥ 90% (excluding events).

## Notes
The M0 minimal `Document`/`Element`/`Text` already exist; this package extends
them. Keep backward-compatibility with `MinimalHtmlParser` (still used until
wp:M1-01h flips the façade).

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T15:21Z — claimed locally by agent-copilot-gpt-5.5 for implementation on branch `wp-M1-03-dom-core`.
- 2026-05-11T19:58Z — completed DOM core: Node mutation primitives,
  owner-document propagation, Element attributes/NamedNodeMap, live tag/class
  collections, DocumentFragment/DocumentType, CharacterData/Text/Comment/CDATA,
  ProcessingInstruction, and 15 DOM tree tests.
