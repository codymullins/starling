---
id: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M0-01-scaffold"
blocks:
  - "wp:M1-02-html-tree-builder"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-01-html-tokenizer"
---

# wp:M1-01 — HTML tokenizer (full WHATWG state machine)

## Goal
Spec-faithful WHATWG HTML tokenizer with all ~80 states, named character
reference resolution, and parse-error sink. Replaces the M0 `MinimalHtmlParser`.

## Inputs
- wp:M0-01-scaffold complete.

## Outputs
- `src/Tessera.Html/InputStream/{ByteSniffer,PreprocessedStream,CodePointReader}.cs`
- `src/Tessera.Html/Tokenizer/{HtmlTokenizer,HtmlToken,States,CharacterReference,NamedCharacterReferences}.cs`
- `tools/gen-entities/Program.cs` (build-time entity table generator)
- `testdata/spec/html-entities.json` (vendored from spec)

## Acceptance
- html5lib tokenizer suite: 100%.
- All public token types (`CharacterToken`, `StartTagToken`, `EndTagToken`,
  `CommentToken`, `DoctypeToken`, `EndOfFileToken`) exposed.
- `HtmlParseError` enum covers all ~85 spec error codes.
- Parser is restartable (push-driven) — verified by feeding the same source
  in arbitrary chunk sizes and asserting identical token streams.

## Decomposition

This package is split into sub-tasks. Each sub-task is a separate file and
can be claimed independently once its own `depends_on` are complete.

| Sub-task | Scope |
|---|---|
| wp:M1-01a-tokenizer-scaffold | Project layout, token types, state enum, parse-error enum, preprocessed stream, Data state, EOF handling. |
| wp:M1-01b-tokenizer-tag-states | TagOpen, EndTagOpen, TagName, BeforeAttributeName, AttributeName, AfterAttributeName, BeforeAttributeValue, AttributeValue\* (3 flavors), AfterAttributeValueQuoted, SelfClosingStartTag. |
| wp:M1-01c-tokenizer-rcdata-rawtext | RCDATA + RAWTEXT family (15 states), PLAINTEXT. |
| wp:M1-01d-tokenizer-script | ScriptData family (15 states) including the double-escape sub-states. |
| wp:M1-01e-tokenizer-comment-cdata | MarkupDeclarationOpen, Comment* family, BogusComment, CdataSection*. |
| wp:M1-01f-tokenizer-doctype | Doctype + 11 doctype sub-states + BogusDoctype. |
| wp:M1-01g-tokenizer-entities | CharacterReference family (8 states) + `NamedCharacterReferences` trie + `tools/gen-entities`. |
| wp:M1-01h-tokenizer-html5lib | Wire the html5lib test suite and drive to 100% pass. |

## Notes
The M0 `MinimalHtmlParser` is preserved during M1 development; the M1
tokenizer lives next to it and the `HtmlParser` façade flips over only once
M1-01h passes.

## Handoff log
- 2026-05-11T15:20Z — package created; decomposed into 8 sub-tasks (a–h).
- 2026-05-11T16:40Z — all tokenizer sub-tasks complete. html5lib tokenizer suite is 7032/7032 and `HtmlParser.Parse` now uses the tokenizer-backed parser.
