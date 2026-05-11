---
id: "wp:M1-01e-tokenizer-comment-cdata"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T15:55:00Z"
branch: "wp-M1-01ef-markup-declarations"
completed_at: "2026-05-11T16:00:00Z"
depends_on:
  - "wp:M1-01a-tokenizer-scaffold"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01e — Comment + CDATA states

## Goal
MarkupDeclarationOpen, Comment*, BogusComment, CdataSection family.

## Acceptance
Comment edge cases (`<!--->`, `<!-- foo --!>`, nested `--`) tokenize per spec;
`<![CDATA[…]]>` only in foreign-content path emits CDATA.

## Handoff log
- 2026-05-11T15:20Z — created.
