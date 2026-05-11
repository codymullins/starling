---
id: "wp:M1-01c-tokenizer-rcdata-rawtext"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T15:40:00Z"
branch: "wp-M1-01c-tokenizer-rcdata-rawtext"
completed_at: "2026-05-11T15:50:00Z"
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
- 2026-05-11T15:40Z — claimed by agent-claude-cody, branch `wp-M1-01c-tokenizer-rcdata-rawtext`.
- 2026-05-11T15:50Z — landed:
  - 9 states implemented in new partial `HtmlTokenizer.RawStates.cs`.
  - New public `HtmlTokenizer.SetState(TokenizerState)` seam for the
    future tree builder to flip the tokenizer into RCDATA / RAWTEXT /
    PLAINTEXT after parsing `<textarea>`, `<style>`, `<plaintext>`, etc.
  - `_lastStartTagName` tracker added to support the
    appropriate-end-tag rule from §13.2.5.11 / §13.2.5.14.
  - `StepEndTagNameCommon(c, returnState)` extracted as a shared body —
    M1-01d ScriptData will plug into it.
  - 11 new tests in `RawStateTests.cs`.
- 2026-05-11T15:50Z — `dotnet test` 87/87 green (was 70; +11 from M1-01c,
  +6 from the parallel wp:M1-03 DOM work that merged independently).
  Marking complete.
