---
id: "wp:M1-01b-tokenizer-tag-states"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T15:32:56Z"
branch: "wp-M1-01b-tokenizer-tag-states"
depends_on:
  - "wp:M1-01a-tokenizer-scaffold"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01b — Tokenizer tag + attribute states

## Goal
Implement: TagOpen, EndTagOpen, TagName, BeforeAttributeName, AttributeName,
AfterAttributeName, BeforeAttributeValue, AttributeValueDoubleQuoted,
AttributeValueSingleQuoted, AttributeValueUnquoted, AfterAttributeValueQuoted,
SelfClosingStartTag.

## Outputs
- `src/Tessera.Html/Tokenizer/States/{TagOpen,EndTagOpen,TagName,...}.cs`
- Tests under `tests/Tessera.Html.Tests/Tokenizer/TagStateTests.cs`.

## Acceptance
Hand-written tests for each state's transitions; html5lib subset for plain
tags (no entities, no scripts) at 100%.

## Notes
Pick this up after a:
1. `git pull && cat tasks/M1/wp-M1-01a-tokenizer-scaffold.md` for the contract.
2. Follow the file layout doc-decision: one file per state under
   `Tokenizer/States/`.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T15:32:56Z — claimed by agent-claude-cody, branch `wp-M1-01b-tokenizer-tag-states`
