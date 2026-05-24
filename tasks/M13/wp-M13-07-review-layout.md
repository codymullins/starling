---
id: "wp:M13-07-review-layout"
milestone: "M13"
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
reviewed_commit: ""
depends_on: []
blocks: []
subsystem: "Starling.Layout"
plan_refs:
  - "browser-plan/07_LAYOUT.md"
  - "browser-plan/12_TESTING.md"
---

# wp:M13-07 — Code review: Starling.Layout

## Goal

Run a focused code review of `src/Starling.Layout` for box-tree invariants,
geometry correctness, and invalidation interactions.

## Inputs

- Target module: `src/Starling.Layout/`.
- A concrete commit SHA must be chosen and written to `reviewed_commit`.

## Outputs

- A review pass documented in this file's Handoff log with findings and
  severity.
- `reviewed_commit` filled with the exact reviewed commit SHA/ref.

## Acceptance

- `reviewed_commit` is non-empty and points to the reviewed commit.
- Handoff log lists either:
  - concrete findings with file paths, or
  - an explicit "no issues found" note.
- `status` is set to `complete` when the review is finished.

## Notes

- Prioritize formatting-context correctness, overflow/clipping behavior, and
  cross-module contracts with paint.

## Handoff log

- 2026-05-24T18:56Z — created (agent-copilot-gpt-5.3-codex)

