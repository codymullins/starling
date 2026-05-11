---
id: "wp:M1-01h-tokenizer-html5lib"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T16:27:14Z"
branch: "wp-M1-01h-tokenizer-html5lib"
completed_at: "2026-05-11T16:40:11Z"
depends_on:
  - "wp:M1-01b-tokenizer-tag-states"
  - "wp:M1-01c-tokenizer-rcdata-rawtext"
  - "wp:M1-01d-tokenizer-script"
  - "wp:M1-01e-tokenizer-comment-cdata"
  - "wp:M1-01f-tokenizer-doctype"
  - "wp:M1-01g-tokenizer-entities"
blocks:
  - "wp:M1-02-html-tree-builder"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
  - "browser-plan/12_TESTING.md"
---

# wp:M1-01h — html5lib tokenizer suite to 100%

## Goal
Wire `testdata/spec/html5lib-tests/tokenizer/` (vendored subtree) as an xUnit
data-driven test. Drive every red case to green. Flip the public
`HtmlParser.Parse` façade to the WHATWG tokenizer + tree builder pipeline.

## Acceptance
- html5lib tokenizer suite: **100%**.
- `HtmlParser.Parse` now uses the WHATWG tokenizer. The M0
  `MinimalHtmlParser` stays for benchmarking and is removed in a follow-up.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T16:27Z — claimed on main by agent-copilot-gpt-5.5 after M1-01g landed.
- 2026-05-11T16:40Z — vendored html5lib tokenizer fixtures, expanded the named entity table from WHATWG `entities.json`, added a generator, fixed the final tokenizer conformance gaps, and flipped `HtmlParser.Parse` to the tokenizer-backed parser. Validation: html5lib tokenizer suite 7032/7032, HTML tests 7132/7132, full solution 7167/7167.
