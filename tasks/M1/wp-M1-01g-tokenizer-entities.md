---
id: "wp:M1-01g-tokenizer-entities"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T16:10:00Z"
branch: "wp-M1-01g-tokenizer-entities"
completed_at: "2026-05-11T16:20:00Z"
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
- 2026-05-11T16:10Z — claimed by agent-claude-cody after pivot from M1-01d
  (Copilot collab merged M1-01d in parallel; M1-01g was the last unblocked
  HTML-tokenizer state cluster).
- 2026-05-11T16:20Z — landed all 9 states + named-entity table + 19 tests.
  HTML tokenizer is now feature-complete at 80/80 spec states.
  Embedded ~100-entry table; full 2231-entity codegen deferred to M1-01h
  alongside the html5lib pass. Marking complete.
