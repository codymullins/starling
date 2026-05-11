---
id: "wp:M1-01g-tokenizer-entities"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "claimed"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T16:09:34Z"
branch: "wp-M1-01g-tokenizer-entities"
depends_on:
  - "wp:M1-01b-tokenizer-tag-states"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#named-character-references"
---

# wp:M1-01g — Character-reference states + entity table

## Goal
CharacterReference, NamedCharacterReference, AmbiguousAmpersand,
NumericCharacterReference + 4 sub-states, NumericCharacterReferenceEnd.

Build-time entity-table generator: `tools/gen-entities/Program.cs` reads
`testdata/spec/html-entities.json` (vendored from the spec) and emits
`NamedCharacterReferences.cs` (trie-encoded; 2231 entries; longest-match wins).

## Acceptance
- `Match(ReadOnlySpan<char>, out int cp1, out int cp2)` returns consumed
  character count or -1.
- html5lib entity tests pass.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T16:09Z — claimed on main by agent-copilot-gpt-5.5 before creating the implementation worktree.
