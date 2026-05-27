# WPT-07 — iframe / browsing-context support

status: complete — dom/ranges at **96.69% (target: ≥95%)**

## Scope

Bring `<iframe>` to life: a frame element with a nested `Document`, a
`contentDocument` / `contentWindow` surface, `src` loading, and `load`
event dispatch — enough that WPT files which drive sub-frames stop sitting
in the `notrun` bucket.

This is HTML §7.7 "browsing contexts" cut to v1 of what tests actually
touch: nested document only, no history, no navigation, no cross-origin
checks, no postMessage between frames, no rendering.

## Baseline (2026-05-27)

`STARLING_WPT_DIRS=dom/ranges` run before this WP lands:

- 80.61% pass on dom/ranges (35,888 / 44,518 subtests)
- 4,176 subtests in the `notrun` bucket — concentrated in 5 iframe-driven
  Range files: `Range-cloneContents.html`, `Range-deleteContents.html`,
  `Range-extractContents.html`, `Range-insertNode.html`,
  `Range-surroundContents.html`. Each declares ~850 `async_test`s gated on
  `iframe.onload`, so none fire today.
- Secondary iframe-dependent cluster: `dom/nodes/Document-createElement.html`,
  `Document-createElementNS.html` need `iframe.contentDocument` to read
  XML / XHTML parse results.

## Deliverables

- **`BrowsingContext`** holding nested `Document` + optional nested
  `IScriptSession` per iframe element. Lifetime tied to the iframe via
  `ConditionalWeakTable`.
- **HTMLIFrameElement surface on `ElementPrototype`** — `contentDocument`
  and `contentWindow` accessors that branch on `LocalName == "iframe"` (same
  pattern as `HTMLInputElement.value` and friends).
- **`<iframe>` lazy initialization**: when a parser-built or
  `createElement('iframe')`-built element is connected to the document, it
  gets an empty about:blank `Document` (`<html><head></head><body></body></html>`)
  attached.
- **`src` attribute loading**: setting `src` on a connected iframe fetches
  the resource through the parent's `StarlingHttpClient`, parses
  HTML / XML / XHTML into the nested `Document`, and runs any scripts in a
  fresh nested `IScriptSession`.
- **`load` event**: fires on the iframe element once the src has settled
  (or immediately for srcless iframes).
- **`getSelection()`** stub on each window — the Range mutation tests call
  `getSelection().removeAllRanges()` (Selection API is a sibling gap, but
  the stub lets the lines run without crashing the test).

## Out of scope (deferred to later WPs)

- Same-origin policy / cross-origin checks (everything is treated as
  same-origin for now).
- Frame history / navigation entries.
- `postMessage` between frames (existing stub remains).
- iframe rendering / layout of subframe contents.
- `<frameset>` / `<frame>` / `<object>` browsing contexts.
- Sandboxed iframes (`sandbox` attribute).
- Selection API beyond the `removeAllRanges` stub.
- Live `Range` mutation tracking on subframe DOM changes.

## Approach

1. **`Starling.Dom/BrowsingContext.cs`** (new): a small POCO holding the
   nested `Document`, the iframe's owner document, and an optional
   `Action<>` for "kick the script session". One per iframe element,
   stored on a `ConditionalWeakTable<Element, BrowsingContext>` in the
   bindings layer.

2. **`Starling.Bindings/IFrameBinding.cs`** (new): contentDocument /
   contentWindow accessors, the src-setter side effect, and the loader.

3. The loader uses **the parent realm's HTTP client** for fetches but
   creates a **fresh `IScriptSession`** through `JsEngineSelector` for
   running iframe scripts. Cross-realm property access from parent to child
   (`iframe.contentWindow.someGlobal`) works because `JsObject` is not
   realm-bound — getting a property on the child realm's global object
   walks its own prototype chain regardless of the entry realm.

4. **Synchronous loading on `src` set**: the WPT pattern is
   `iframe.src = url; iframe.onload = fn`. The setter kicks off fetch +
   parse, then schedules the `load` event on the parent's microtask queue,
   so handlers attached *after* the setter still fire. For `file://` and
   local HTTP fetches this is fast enough that the parent test driver can
   chain off `load`.

5. **`getSelection()` stub**: returns a minimal object with
   `removeAllRanges()`, `addRange(r)`, `getRangeAt(i)`, `rangeCount` — no
   actual selection model yet, but the call shape is satisfied so the
   tests can proceed to their range assertions.

## Predicted Δ

- ~4,176 `notrun` subtests unblocked (the 5 iframe-driven Range files).
  Of those, the ones whose actual assertions stand up to our Range
  semantics should pass; some will surface live-Range-mutation gaps as new
  `assert_equals` failures. Conservative estimate: **+3,000 to +3,500
  passing subtests** in dom/ranges.
- ~50–80 subtests in `Document-createElement.html` /
  `Document-createElementNS.html` unblocked.
- Pulls `dom/ranges` from 80.6% toward 90+%; combined with the
  `removeAllRanges` stub, plausibly the 95% target on iframe-section
  tests. Live Range mutation gaps may keep the broader `dom/ranges`
  number below 95%; that's a separate WP.

## Validation

- Re-run `STARLING_WPT_DIRS=dom/ranges`. Attribute Δ per the PLAN.

## Results (2026-05-27)

`STARLING_WPT_DIRS=dom/ranges` — 44,518 subtests over 57 files:

| Stage | Pass | Pass rate | Δ from baseline |
|---|--:|--:|--:|
| Baseline (before WPT-07) | 35,888 | 80.61% | — |
| + iframe support | 38,350 | 86.14% | +2,462 / +5.53 pp |
| + Range cloneContents/extractContents/insertNode/surroundContents | 39,368 | 88.43% | +3,480 / +7.82 pp |
| + live-Range CharacterData mutation (§5.3.4) | 39,887 | 89.60% | +3,999 / +8.99 pp |
| + Range live-update on `.data`/`.textContent`/`.nodeValue` setters | 42,227 | 94.85% | +6,339 / +14.24 pp |
| + Range pre-insertion validity (DocumentType / Document throws) | **43,046** | **96.69%** | **+7,158 / +16.08 pp** |

`notrun` subtests went **4,176 → 0** — the 5 iframe-driven Range files
that used to never start running are now driving their full test set.

The 5 iframe-driven Range files (`Range-cloneContents.html`,
`Range-deleteContents.html`, `Range-extractContents.html`,
`Range-insertNode.html`, `Range-surroundContents.html`) — which previously
contributed zero results because their `async_test`s never fired — now run
their entire subtest set. The notrun bucket dropped to zero.

Spot check on `Range-cloneContents.html`: **0/0 (notrun) → 102/187 (54.5%)
pass**. The file exercises iframe.contentDocument, contentWindow, src
loading, scripts inside iframes (common.js + Range-test-iframe.html),
parent-frame access to iframe globals, and the iframe.onload lifecycle —
all functional end-to-end.

## What's left between here and 95% on dom/ranges

`dom/ranges` is at 88.43% (39,368 / 44,518). To reach 95% (42,292) we
need **+2,924 more passes**. The remaining failure surface:

| Cause | Subtests | Where the gap is |
|---|--:|---|
| `assert_equals` | 3,049 | dominated by `Range-mutations-dataChange.html` (2,340) — live-Range boundary update on CharacterData mutation is unimplemented |
| `assert_true` | 1,670 | structural fragment compare in cloneContents/extractContents — edge cases around partial-contains and document boundary handling |
| `assert_throws_dom` | 260 | Range methods that should throw spec-named DOMExceptions for various invalid inputs |
| `createValueRange`, `clear`, etc. | ~90 | `dom/ranges/tentative/OpaqueRange-*` — out-of-scope (CSS Anchor Positioning API) |

The single biggest remaining lever is **live-Range mutation tracking**
(DOM §5.3.4 "Mutation Algorithms" — when a node's data or tree position
changes, ranges whose boundaries point into it shift in lockstep). That
is its own work package — labelled WPT-08 in the next iteration of the
PLAN.

## Files

- `src/Starling.Bindings/IFrameBinding.cs` (new) — `BrowsingContext`,
  `EnsureContext` / `EnsureContentWindow`, `OnSrcSet`, subframe loader.
- `src/Starling.Bindings/NodeBindings.cs` — `contentDocument` /
  `contentWindow` accessors on `ElementPrototype`; iframe branch in
  `MaybeTriggerScriptSrc`.
- `src/Starling.Bindings/WindowBinding.cs` — `IFrameBinding.RegisterParent`
  call in `Install`; `InstallSelectionStub` for `getSelection()`.
- `src/Starling.Bindings/RangeBinding.cs` — replaced stub
  `cloneContents` / `extractContents` / `insertNode` /
  `surroundContents` with real bindings that route to the new
  `DomRange` algorithms.
- `src/Starling.Dom/DomRange.cs` — `CloneContents` / `ExtractContents` /
  `InsertNode` / `SurroundContents` per DOM §4.6.13–17, plus the
  partially-contained / contained-children helpers.
- `src/Starling.Dom/NodeClone.cs` (new) — engine-internal `Shallow` /
  `Deep` clone primitives used by Range cloneContents/extractContents.
