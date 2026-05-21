---
id: "wp:M1-noscript-scripting-flag"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T00:00:00Z"
branch: "main"
completed_at: "2026-05-20T00:00:00Z"
depends_on:
  - "wp:M1-02-html-tree-builder"
blocks: []
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
---

# wp:M1-noscript-scripting-flag — `<noscript>` inert when scripting is enabled

## Goal
Make `<noscript>` content inert / non-rendered when the scripting flag is
ENABLED, per WHATWG HTML §13.2 tree construction. The engine runs JavaScript,
so its document parse must use scripting = enabled; in that mode `<noscript>`
contents must not render. The html5lib conformance harness keeps scripting =
disabled (its tests assume that), so its expectations are unchanged.

## The bug
Starling rendered `<noscript>` contents even with JS enabled, so sites like
mcmaster.com showed their `<noscript>` "please enable JavaScript" fallback even
though Starling runs JS. Root cause: the parser was always built scripting-
disabled (to pass the html5lib suite, which assumes that), and the diagnostic
text extractor + UA stylesheet did not treat `<noscript>` as a hidden element.

## What the spec requires
- WHATWG HTML §13.2 has a **scripting flag** (enabled when scripting is active).
- §13.2.6.4.4 "in head", `<noscript>` start tag:
  - scripting **enabled** → generic raw text element parsing algorithm
    (switch tokenizer to RAWTEXT; contents become an inert text node).
  - scripting **disabled** → "in head noscript" insertion mode (contents parse
    as elements).
- "in body" does NOT special-case `<noscript>`: it is inserted as an ordinary
  element regardless of the scripting flag. Non-rendering there comes from CSS.
- §15.3.1 "Hidden elements" (rendering): when the scripting flag is enabled the
  UA stylesheet applies `noscript { display: none !important; }`.

## Implementation
- Threaded a `scriptingEnabled` flag (default `false`) through
  `HtmlParser.Parse` → `HtmlTreeBuilder.Parse` → `HtmlTreeBuilder` ctor.
- `HtmlTreeBuilder.HandleInHead`: when `scriptingEnabled`, a `<noscript>` start
  tag follows the generic raw text algorithm (insert element, original-mode →
  Text, tokenizer → RAWTEXT). Scripting-disabled path is unchanged (existing
  "anything else" fall-through preserved — the "in head noscript" sub-mode is
  not modeled, matching prior behavior).
- `Starling.Engine.Engine` — both `HtmlParser.Parse` call sites pass
  `scriptingEnabled: true` (the engine executes page JS).
- UA stylesheet (`Starling.Css/UserAgent/UaStyleSheet.cs`): added
  `noscript { display: none }` so the box tree drops it (BoxTreeBuilder skips
  `display:none`), preventing painted boxes. This is the spec-correct render
  mechanism (§15.3.1).
- `Engine.AppendDisplayText` diagnostic extractor: added `noscript` to the
  existing `display:none` skip-list (alongside `script`/`style`/`head`) so the
  `RenderOutcome.DisplayText` / headless "text length" diagnostic matches the
  painted output.

## Acceptance
- Parser: scripting enabled → in-head `<noscript><div>x</div></noscript>` is an
  inert text node, not a `<div>`; scripting disabled → the `<div>` element
  (existing behavior preserved).
- Engine render: `<noscript>` contributes no rendered text when scripting is on.
- html5lib tokenizer suite stays 100% (7032/7032).

## Tests
- `Starling.Html.Tests/TreeBuilderTests.cs`:
  - `Noscript_in_head_with_scripting_enabled_parses_contents_as_inert_text`
  - `Noscript_in_head_with_scripting_disabled_parses_contents_as_elements`
  - `Noscript_in_body_text_is_inert_text_child_when_scripting_enabled`
- `Starling.Engine.Tests/EngineJsExecutionTests.cs`:
  - `Noscript_contents_are_not_rendered_when_scripting_is_enabled`
- All tagged `[Spec("html", …, "13.2.6.4.4 in head — noscript / scripting flag")]`
  + `[SpecFact]`.

## Handoff log
- 2026-05-20 — implemented + tested; html5lib tokenizer suite still 100%
  (7032/7032). Reproduced via headless CLI: `text length` drops from including
  "HIDDEN" to 7 (just "VISIBLE").
