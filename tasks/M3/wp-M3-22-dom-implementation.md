---
id: "wp:M3-22-dom-implementation"
parent: ""
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-21T00:00:00Z"
completed_at: "2026-05-21T00:00:00Z"
depends_on:
  - "wp:M4-01-api-gap-mcmaster"
blocks: []
subsystem: "Starling.Dom / Starling.Bindings"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md"
spec_refs:
  - "https://dom.spec.whatwg.org/#interface-domimplementation (DOM §4.5)"
  - "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument (DOM §4.5.1)"
---

# wp:M3-22 — DOMImplementation + createHTMLDocument

## Why

mcmaster.com jQuery `parseHTML`/`buildFragment`/support-detection calls:
```js
document.implementation.createHTMLDocument("")
```
This was throwing `"not a function: undefined (method hint: 'createHTMLDocument')"`.
jQuery's support detection loop never completes without it, blocking all
subsequent jQuery functionality.

## What was done

### `src/Starling.Dom/Document.cs`
Added `Document.CreateHtmlDocument(string? title)` static factory method (DOM §4.5.1):
- Creates a new `Document`
- Appends `<!DOCTYPE html>` via `CreateDocumentType("html")`
- Appends `<html>` root
- Appends `<head>` (with `<title title_text>` if `title` arg is non-null)
- Appends `<body>`
- Returns the fully initialized Document

### `src/Starling.Bindings/NodeBindings.cs`
- Added `document.implementation` accessor on the document prototype (cached per-wrapper with `__domImpl__` key)
- Added `BuildDomImplementation(JsRealm)` private helper that creates a plain JS object with:
  - `createHTMLDocument([title])` — calls `Document.CreateHtmlDocument(title)` and wraps via `DomWrappers.Wrap(realm, doc)` so the returned document gets the full DocumentPrototype (enabling `.head`, `.body`, `.title`, `createElement`, `innerHTML`, etc.)
  - `createDocumentFragment()` — stub for symmetry
  - `hasFeature()` — returns `true` per DOM spec

## How innerHTML works in the new document

The `innerHTML` setter in `NodeBindings.cs` calls `ParseFragment(e, markup)` which uses `e.OwnerDocument` as the owner for the fragment. Since the new document's body has its `OwnerDocument` set correctly (via `CreateElement` which assigns `OwnerDocument = this`), the parsed nodes land in the right document. `DomWrappers.Wrap(realm, doc)` uses the realm's `DocumentPrototype` which already has the `innerHTML` accessor defined — no special wiring needed.

## Tests added

**`tests/Starling.Dom.Tests/DomImplementationTests.cs`** (8 tests):
- `CreateHtmlDocument_has_html_documentElement`
- `CreateHtmlDocument_has_head_and_body`
- `CreateHtmlDocument_with_title_creates_title_element`
- `CreateHtmlDocument_empty_string_title_creates_empty_title_element`
- `CreateHtmlDocument_null_title_has_no_title_element`
- `CreateHtmlDocument_has_doctype`
- `CreateHtmlDocument_body_ownerDocument_is_new_doc`
- `CreateHtmlDocument_createElement_returns_element_in_new_doc`

**`tests/Starling.Bindings.Tests/DomImplementationBindingTests.cs`** (13 tests):
- `Implementation_is_accessible_on_document`
- `Implementation_is_stable_identity`
- `CreateHTMLDocument_returns_object`
- `CreateHTMLDocument_documentElement_tagName_is_HTML`
- `CreateHTMLDocument_head_is_accessible`
- `CreateHTMLDocument_body_is_accessible`
- `CreateHTMLDocument_title_reflects_arg`
- `CreateHTMLDocument_empty_string_title_yields_empty_title`
- `CreateHTMLDocument_createElement_works`
- `CreateHTMLDocument_body_fragment_parse_works`
- `CreateHTMLDocument_body_innerHTML_parse_works`
- `CreateHTMLDocument_base_append_jQuery_pattern`
- `HasFeature_returns_true`

## Results

- Bindings.Tests: 189 green (was 176; +13 new)
- Dom.Tests: 37 green (was 29; +8 new)
- Js.Tests: 1232 green / 1 skipped (unchanged)
- mcmaster render: `createHTMLDocument` error **gone**; next blocker is optional-chaining `?.` parse error in a separate mcmaster script
