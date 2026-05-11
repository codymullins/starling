---
id: "wp:M1-01d-tokenizer-script"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "claimed"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T16:02:23Z"
branch: "wp-M1-01d-tokenizer-script"
depends_on:
  - "wp:M1-01c-tokenizer-rcdata-rawtext"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01d — ScriptData state family

## Goal
ScriptData + 14 sub-states including the double-escape gymnastics (the
inner-HTML-of-`<script>` corner case).

## Acceptance
html5lib cases targeting `<script>` content pass; `</script>` only
terminates the element when not inside an escape.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T16:02Z — claimed by agent-copilot-gpt-5.5 in sibling worktree `../tessera-wp-M1-01d-tokenizer-script`.
- 2026-05-11T16:02Z — implemented ScriptData + escaped/double-escaped state family in `HtmlTokenizer.ScriptStates.cs`, wired dispatch/EOF handling, and added 6 focused tests. `dotnet test tests/Tessera.Html.Tests/Tessera.Html.Tests.csproj` passes 56/56; `dotnet test Tessera.sln` passes in the worktree.
