---
id: "wp:M1-01c-tokenizer-rcdata-rawtext"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T15:40:00Z"
branch: "wp-M1-01c-tokenizer-rcdata-rawtext"
depends_on:
  - "wp:M1-01a-tokenizer-scaffold"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01c — RCDATA / RAWTEXT / PLAINTEXT states

## Goal
RCDATA, RCDATALessThanSign, RCDATAEndTagOpen, RCDATAEndTagName; RAWTEXT and
its three sub-states; PLAINTEXT.

## Acceptance
Each state has dedicated transition tests; html5lib cases that target
`<textarea>`, `<title>`, `<style>`, `<xmp>` pass.

## Handoff log
- 2026-05-11T15:20Z — created.
