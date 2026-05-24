---
id: "wp:M13-04-review-html"
milestone: "M13"
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
reviewed_commit: ""
depends_on: []
blocks: []
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md"
  - "browser-plan/12_TESTING.md"
---

# wp:M13-04 — Code review: Starling.Html

## Goal

Run a focused code review of `src/Starling.Html` for tokenizer/tree-builder
correctness and malformed-input robustness.

## Inputs

- Target module: `src/Starling.Html/`.
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

- Prioritize state-machine transitions, insertion-mode handling, and error
  recovery correctness.

## Handoff log

- 2026-05-24T18:56Z — created (agent-copilot-gpt-5.3-codex)

