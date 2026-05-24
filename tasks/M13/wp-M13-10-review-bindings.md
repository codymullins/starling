---
id: "wp:M13-10-review-bindings"
milestone: "M13"
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
reviewed_commit: ""
depends_on: []
blocks: []
subsystem: "Starling.Bindings"
plan_refs:
  - "browser-plan/10_WEB_APIS.md"
  - "browser-plan/12_TESTING.md"
---

# wp:M13-10 — Code review: Starling.Bindings

## Goal

Run a focused code review of `src/Starling.Bindings` for web API correctness,
lifetime safety, and JS/DOM bridge behavior.

## Inputs

- Target module: `src/Starling.Bindings/`.
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

- Prioritize binding argument validation, observable API semantics, and
  exception/error propagation.

## Handoff log

- 2026-05-24T18:56Z — created (agent-copilot-gpt-5.3-codex)

