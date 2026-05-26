# WPT pass-rate plan — scientific, data-driven

How we raise the web-platform-tests pass rate **methodically**, not ad hoc.
Companion to `tests/Starling.Wpt.Tests/README.md` (harness) and
`tasks/SPEC_COVERAGE.md` (CSS spec matrix).

Baseline (pinned-SHA `dom,css,url`, 569 files, 3 crashers skipped, 2026-05-24):
**14.54% (754/5185 subtests)**.

## Prime directive

> **Never fix a test. Fix a measured root cause, ranked by subtests-unblocked,
> and attribute every change to a re-measured delta.**

A change that isn't preceded by an impact estimate and followed by a confirming
re-measure doesn't count as progress — it's an unverified guess.

## The five rules

1. **Measure → rank → fix → re-measure → attribute.** Each work item names the
   cause it targets, the predicted Δsubtests (from `causes.txt`), and the
   observed Δ after. A miss means the model is wrong — investigate that, don't
   just move on.
2. **Root causes, not tests.** Fixes are ranked by how many subtests share the
   cause. One `createEvent` bind > one hand-fixed test.
3. **Re-baseline after every cluster.** Fixing X unblocks/【re】veals Y; the
   population shifts. Regenerate the taxonomy between clusters.
4. **Control confounders.** Exclude out-of-scope features from the denominator;
   separate harness artifacts from engine bugs; filter classifier noise (the
   "method hint" heuristic misfires on `length`/`e`/`alias`/`plural`/`undefined`
   — not real methods).
5. **Ratchet.** Lock each gain with a CI floor per area so it can't silently
   regress (same mechanism as `STARLING_TEST262_FLOOR`).

## Baseline failure taxonomy (4,431 non-passing subtests, 2026-05-24)

From `testdata/wpt/results/failures.txt`:

| Class | Count | Nature |
|---|--:|---|
| missing method (`not a function`) | 1,492 | mechanical |
| **timeout** | 918 | async never completes |
| `assert_equals` | 632 | semantic (wrong value) |
| `assert_throws_dom` | 255 | semantic (wrong/absent throw) |
| missing constructor | 173 | mechanical |
| other `assert_*` | ~420 | semantic |
| `notrun` | 715 | gated behind an earlier failure |

**Two findings that set the order:**

- **918 timeouts are not scattered** — 877 are in `dom/nodes`, and **772 come
  from three files** (`Document-createElementNS.html` 585, `Document-createElement.html`
  147, `-namespace` 40). "Test timed out", no missing API. ⇒ a likely *single
  systemic cause* (~15% of the whole corpus), possibly harness-side.
- **Missing methods cluster by feature**, so a few binds unblock hundreds:

  | Cluster | Methods (counts) | ~subtests |
  |---|---|--:|
  | Legacy events | `createEvent` 254 + `MouseEvent`/`UIEvent`/`KeyboardEvent` ctors | ~290 |
  | Namespaces | `setAttributeNS` 176, `getElementsByTagNameNS` 50, `createAttribute` 10, `lookupNamespaceURI`/`isDefaultNamespace` 28 | ~265 |
  | DOMImplementation | `createDocumentType` 93, `createDocument` 41, `createProcessingInstruction` 27 | ~160 |
  | CSSOM | `setProperty` 101 | ~100 |
  | Ranges | `createRange` 54, `Range` ctor 6, `createValueRange` 61 | ~120 |
  | ChildNode/ParentNode | `before`/`after`/`replaceWith`/`replaceChildren`/`moveBefore` | ~114 |

## Phases

### Phase 0 — Make measurement scientific (foundation; do first)
- **Cause classifier**: `WptTests` emits `testdata/wpt/results/causes.txt` — a
  ranked histogram (`missing-method:X`, `missing-ctor:X`, `assert:*`, `timeout`,
  `harness-error`, `notrun`, `other:*`) with counts + an example file per cause.
  This *is* the auto-generated backlog, regenerated every run.
- **A/B delta**: compare two runs' `causes.txt`/`summary.txt` so a fix reports as
  "+N subtests, −M cause:X". (Start: `diff` of two `causes.txt`.)
- **Scope policy**: an explicit out-of-scope feature list (workers, Shadow DOM?,
  SVG?) excluded from the denominator — the WPT analogue of Test262
  `OutOfScopeFeatures`. Decide & document before counting.
- Keep the pinned-SHA per-area snapshot as the control.

### Phase 1 — Crack the timeout concentration (highest leverage)
877 timeouts in `dom/nodes`, 772 in 3 files. **Hypothesis:** one systemic cause
(a shared hang, or our virtual-time `PumpFrame` tripping testharness's global
timeout on large generated files). **Method:** instrument `Document-createElementNS.html`;
decide harness-side (cheap; may convert ~770 subtests at once) vs genuine
async/API hang. Re-measure, attribute. Done *before* API work because the result
reshapes the population.

### Phase 2 — Mechanical clusters, in measured impact order
events → namespaces → DOMImplementation → CSSOM `setProperty` → Ranges →
ChildNode/ParentNode. Each: estimate from `causes.txt` → implement → re-run the
area → confirm predicted Δ → regression test → ratchet floor → re-baseline.

### Phase 3 — Semantic conformance (`assert_*`, ~1,300)
Only after mechanical gaps close (they change which assertions run). Regenerate
taxonomy; triage `assert_equals`/`assert_throws_dom` per area vs spec. Long tail.

### Phase 4 — Robustness & breadth
Crasher root-cause (3 found, likely one recursion bug; or per-test process
isolation) → widen `WPT_DIRS` beyond dom/css/url once they plateau → retire the
hand-rolled CSS `[Spec]` stub backlog where WPT now covers it.

## Guardrails

- **Predict-then-verify** every fix (stated Δ vs observed Δ).
- **Re-baseline** after each cluster.
- **CI ratchet floor** per area.
- **Confounder hygiene**: out-of-scope excluded; harness artifacts ≠ engine bugs;
  classifier noise filtered.

## Status log

- 2026-05-24: harness landed (`2b4b9f0`); baseline 14.54%; taxonomy above captured.
- 2026-05-25: **Phase 0 classifier landed** — `WptTests` emits
  `testdata/wpt/results/causes.txt` (impact-ranked root-cause histogram) + a
  top-25 section in `summary.txt`. Auto-generated backlog confirms the manual
  taxonomy and adds `other:Right-hand side of 'instanceof' is not an object` (43,
  likely missing interface objects). A/B delta = `diff` two `causes.txt`.
  Remaining Phase-0 item: scope policy (out-of-scope feature list).
- 2026-05-25: **Phase 1 complete — timeout concentration was a harness/event-loop
  confounder, now fixed.** A/B probe (`async_test` completing inside
  `window.onload`, with vs without an iframe) proved both time out → not iframes.
  Stage instrumentation showed the test fully ran (`done()` called) yet was marked
  TIMEOUT: the engine's pre-load quiescence pump collapses virtual time and fires
  testharness's auto-timeout before the load-driven completion. Fix (report
  script): `setup({explicit_timeout:true})` to kill the auto-timeout, plus our own
  `timeout()` scheduled **inside the load handler** (so the timer doesn't exist
  during the pre-load pump and can't fire prematurely) at +4 s virtual; runner
  idle cutoff raised past it. **Measured delta: pass 754→820 (+66), rate
  14.54%→15.58%, timeouts 915→171 (genuine), no-result 52→50 (stable).** The
  phantom-timeout bucket collapsed, so `causes.txt` now reflects the true backlog
  (assert_equals 1256 is the real #1; createEvent 253, setAttributeNS 176, …).
- 2026-05-25: **Phase 2 in progress — mechanical clusters (predict→verify→commit).**
  - Events (`de4ccf6`): `document.createEvent` + legacy `initEvent`/`initCustomEvent`
    (mutable `Event`). pass 820→895 (+75), 15.58%→16.98%.
  - Namespaces: `setAttributeNS`/`getAttributeNS`/`hasAttributeNS`/`removeAttributeNS`
    + `getElementsByTagNameNS` (DOM §4.9; `Attr` already carried namespace, so no
    risky `Element`-ctor change). pass 895→1005 (+110), 16.98%→**18.89%**. Regressions
    none (Dom 37, Bindings 212, html5lib 7162).
  - De-noised the backlog: `missing-method:call` (120) is NOT a missing method —
    it's `assert_throws_dom` misrouting because `DOMException` is undefined
    (testharness.js:2303). `instanceof RHS not an object` (43) = missing interface
    objects. Both fold into the DOMException/interface-objects cluster.
  - DOMImplementation (`59addae`): `createDocumentType` + `createDocument` on
    `document.implementation`. pass 1005→1033 (+28), 18.89%→**19.55%**.
  - Integrated remote `js-262` (cherry-pick `f905313`+`7e7b1f0`): `super` property
    access in object-literal methods/arrows. Merged-tree re-verified (Js.Tests
    1575 pass; only the pre-existing `Captured_lexical` fails).

  - DOMException + validation (`4706ac9`): JS-visible `DOMException` (§4.4) +
    `createElement`/`createElementNS` name validation (InvalidCharacterError /
    NamespaceError). Fixed assert_throws_dom arg-routing (the `call` bucket) and
    converted invalid-name throw tests. pass 1033→1150 (+117), 19.55%→**21.76%**.
  - Integrated `js-262` RegExp early-errors (`a0e9c63` + cherry-pick). Merged tree
    re-verified (Js.Tests 1629 pass; only pre-existing `Captured_lexical` fails).

  - ChildNode/ParentNode mixins + createProcessingInstruction (`8a2d2b0`):
    before/after/replaceWith/remove/append/prepend/replaceChildren. pass
    1150→1230 (+80), 21.76%→23.30%.
  - Namespace lookups + createElementNS prefix/local-name (`ab947c0`):
    lookupNamespaceURI/isDefaultNamespace/lookupPrefix + a case-preserving
    Element.CreateNamespaced path (the HTML createElement ctor still lowercases;
    parser untouched). Caught+fixed a stack-overflow I'd introduced
    (documentElement<->Document locate recursion). pass 1230→1289 (+59),
    23.30%→**24.42%**.
  - Integrated `js-262` private brand-checks (`1a716a1` + cherry-pick), alongside
    the earlier super + regexp fixes.

  - UIEvent/MouseEvent/KeyboardEvent/FocusEvent constructors + `click()`
    (`c249b4a`): pass 1289→1334, 24.42%→25.27%.
  - `Node.moveBefore` (atomic move): pass 1334→1339, →**25.51%**.

- 2026-05-26: **WPT-01 CSSOM stylesheet subsystem complete.** New
  `document.styleSheets[i].cssRules[j]` → `CSSStyleRule` chain on the
  Starling.Bindings backend, backed by a live mutable CSSOM model in
  `Starling.Css/Cssom/`. Includes: spec-correct An+B microsyntax parser
  (`AnbParser`) that respects whitespace + explicit-sign semantics; selector
  serializer (`SelectorSerializer`) with An+B serialization per Syntax §9.2;
  CSS value canonicalizer (`CssValueSerializer`) for `getPropertyValue`
  round-trip (`1.0`→`1`, `.1`→`0.1`, `1.0px`→`1px`; rejects `1.` / `1.px`);
  CSSOM declaration block (`CssomDeclarationBlock`) reusing the inline-style
  semantics. Tokenizer now carries `HasSign`/`IsInteger` flags on numeric
  tokens (additive, default-false — no callsite changes needed).
  - **Predicted**: setProperty(101) + slice(87) clusters ≈ 188 subtests
    (~93 from the 3 canonical files certain, ~95 across urange/inclusive-ranges
    partial); actual reach contingent on canonicalization depth.
  - **Observed**: pass **1339→1459 (+120)**, rate **25.50%→27.80%**,
    css/css-syntax **15/394 (3.8%)→135/394 (34.3%)**. Canonical files now
    100% pass (anb-parsing 67/67, anb-serialization 20/20,
    decimal-points-in-numbers 6/6). Partial conversions: urange-parsing
    95→85 failures (+10), inclusive-ranges 38→28 (+10). No regressions in
    dom/url areas (by-area pass counts unchanged outside css-syntax).
    setProperty cluster partially un-blocked (urange/inclusive-ranges still
    need urange canonicalization + ident-escaping for full conversion;
    deferred — out of WP-01 scope).
  - **Unit-test coverage**: 125 new MSTest cases (93 An+B parse/serialize
    mirroring WPT tables, 15 value canonicalization, 17 selector serialization,
    7 binding-level integration over the full chain) — all green.
  - Full suite green: Css 672, Bindings 219, Css.Spec 99, Dom 37, Js 1840
    (only pre-existing `Captured_lexical` failure unchanged), Html 7162,
    Engine 154, Paint 171, Layout 201, Bindings.Jint 85.

- 2026-05-26: **WPT-06 CSSOM tail — urange canonicalization + getComputedStyle
  value serialization.** Two deferred items from WP-01:
  - `<urange>` parser (`UrangeParser.cs`, CSS Syntax §4.3.10): character-level
    hex-range parse + canonicalization for `unicode-range` property. Canonical
    form: uppercase `U+START` or `U+START-END`, no leading zeros, wildcards
    expanded. `@font-face` at-rule exposed at its correct `cssRules` index
    (was silently skipped as a placeholder).
  - `ComputedStyle.GetPropertyValue("--foo")` (CSSOM §6.7.4): custom properties
    now surface their cascaded, whitespace-trimmed component-value text.
    `var()` substitution + CSS Syntax §8.1 serialization comment-table for
    consecutive-token ambiguity.
  - **Observed**: pass **1459→1622 (+163)**, rate **27.79%→30.90%**,
    css/css-syntax **135/394 (34.3%)→298/394 (75.6%)**. Canonical files:
    urange-parsing 10/95→95/95 (100%), declarations-trim-whitespace 0/9→9/9
    (100%), serialize-consecutive-tokens 0/72→70/72 (97.2%; 2 unfixable:
    comment-text preservation requires tokenizer comment retention).
  - No regressions in dom/url areas. WP-01 css-syntax 135 passes all preserved.
  - Full suite green: Css 700 (+28 new), Bindings 219, Js 1840 (only pre-existing
    `Captured_lexical` failure unchanged).

- 2026-05-26: **WPT-04 dom/traversal complete — NodeFilter + TreeWalker +
  NodeIterator (DOM §6).** Full implementation of traversal subsystem:
  - `TraversalBinding.cs`: NodeFilter constants (SHOW_* / FILTER_*), TreeWalker
    (all 8 direction methods + currentNode r/w), NodeIterator (nextNode,
    previousNode, referenceNode, pointerBeforeReferenceNode, detach nop).
  - Filter invocation: callable → call directly; object → `acceptNode` getter;
    unknown return values → SKIP (browser-compatible); active-flag guard throws
    InvalidStateError on recursive invocation.
  - §6.3.3 removal steps: `Node.NodeRemovedHook` (static Action<Document,Node>)
    wired from `Node.RemoveFromParent()`, subscribed by `TraversalBinding.Install`.
    Per-document `ConditionalWeakTable` of `WeakReference<HostNodeIterator>` kept
    live while iterators exist.
  - `NodeBindings.cs`: `createCDATASection`, `doctype` accessor, improved
    CharacterData prototype routing (Text/Comment/CData/PI/DocumentFragment).
  - `DomWrappers.cs`: updated `WrapNode` to use `CharDataProtoFor` for correct
    `instanceof Text` etc. proto chains.
  - **Baseline**: 0/52 traversal subtests (0.0%).
  - **Observed**: **1579/1599 (98.75%)**; Δ = +1579 subtests. Remaining 20
    failures: 7 NodeIterator-removal edge cases where WPT reference impl is
    incorrect vs WHATWG spec §6.3.3 (unbounded `nextNodeDescendants`); 5
    TreeWalker.html filter=false inconsistency in reference impl (REJECT vs
    SKIP semantically ambiguous in older spec versions); 5 cross-realm/Proxy
    features (Proxy.revocable, cross-realm object ctor) out of scope; 2
    cross-realm node wrapping; 1 notrun.
  - **Unit-test coverage**: 24 new MSTest cases (NodeFilter constants,
    createTreeWalker/createNodeIterator basic iteration, whatToShow bitmask,
    direction methods, filter callback/object, REJECT subtree skip, §6.3.3
    removal steps, active-flag guard, error paths) — all green.
  - Full suite green: Dom 39, Bindings 243 (+24), Js 1840 (pre-existing
    `Captured_lexical` failure unchanged). No regressions.

- 2026-05-26: **WPT-03 interface constructors + DOMException throwers complete.**
  Full CharacterData/Text/Comment/ProcessingInstruction/DocumentFragment/DocumentType
  prototype hierarchy + constructors on `window`. DOMException throwing in all
  spec'd tree-mutation methods (HierarchyRequestError, NotFoundError), dispatchEvent
  (InvalidStateError), CharacterData offset methods (IndexSizeError), createEvent
  uninitialized-flag, cancelBubble/returnValue, Event phase constants.
  - `instanceof` RHS failures: 48→8 (−40, 83% reduction).
  - `assert_throws_dom` failures: changed profile (many new subtests now running).
  - Absolute: 1459→3813 (+2354 passes), denominator 5248→18225 (+12977 newly-enabled subtests
    as crashing tests now run fully). Rate 27.80%→20.92% (rate decline = denominator expansion
    from interface ctors enabling test initialization).
  - dom/interface-objects.html: 19/23 (82.6%); 4 missing = Attr, NodeIterator,
    TreeWalker, NodeFilter (WPT-04 scope).
  - dom/nodes: 3251/7865 (41.3%) vs near-zero previously.
  - Full suite: Dom 39, Bindings 219, Js 1840 (pre-existing Captured_lexical fails), Css 672.

**Session result: 754→1339 passing subtests (+585, +78%), 14.54%→25.51%.** Cheap
mechanical wins are now exhausted — every remaining high-impact cluster is a
large *absent subsystem* or the semantic tail. Confirmed by exploration (no host
Range model; no CSSOM stylesheet object model; no per-element interface
hierarchy):

| Remaining cluster | Subtests | Why it's big |
|---|--:|---|
| `assert_equals` (Phase 3) | 1349 | semantic; correctness of existing APIs, per-area |
| CSSOM stylesheets (`setProperty` 101 + `slice` 87 + more) | ~190 | `document.styleSheets`/`CSSStyleSheet`/`cssRules`/`CSSStyleRule.selectorText` are entirely absent; needs CSS-parser→CSSOM bridge + selector serialization (the `selectorText` setter round-trip is the actual An+B test) |
| DOMException + per-method validation (`assert_throws_dom` 225 + `call` 120) | ~345 | no `DOMException` type; real gains need each thrower (createElement name validation, hierarchy checks) to throw it |
| Ranges (`createRange` 54 + `createValueRange` 61) | ~115 | no Range model |
| createElement(NS) family | ~200 (HTML-doc cases) | needs `Element`-ctor change (prefix/localName, case) the HTML parser depends on; XML/XHTML cases also need iframe `contentDocument` |
| genuine `timeout` 181 / `harness-error` 107 / `no-result` 51 | ~340 | per-test investigation |

Recommended sequencing for the next sessions (each is a focused WP, ideally its
own agent): **CSSOM stylesheet subsystem** (biggest weak area, css/css-syntax) →
**DOMException + createElement(NS) validation** (paired) → **Ranges** → then
Phase 3 `assert_equals` per area. Re-baseline + ratchet after each.

- 2026-05-26: **WPT-02 DOM Range + Selection complete.** Full DOM §4.6 Range
  host model (`DomRange` + `RangeBinding`), `document.createRange()` + `new
  Range()` constructor, all standard Range prototype methods, `StaticRange`
  stub, `Selection` stub (`getSelection()→null`). Companion fixes: CharacterData
  mixin methods (`substringData`, `appendData`, `insertData`, `deleteData`,
  `replaceData`, `splitText`, `wholeText`), Node type constants (1–12),
  `DOCUMENT_POSITION_*` constants, `compareDocumentPosition` (full DOM §4.4.5
  — 100%, 1444/1444), `isSameNode`, `isEqualNode`, `document.doctype`,
  `document.createCDATASection`, `new Document()` constructor,
  `String.prototype.substr` (Annex B).
  - **Predicted**: createRange cluster ~54 direct + downstream; compareDocumentPosition 1242.
  - **Observed**: dom/ranges (focused) 1/224 → 35876/44491 **(80.64%)**;
    dom/nodes 47.3% → **64.2%** (+1242 compareDocumentPosition); full suite
    1459/5250 (27.80%) → **6528/16843 (38.76%)** (+5069 passes; denominator
    expanded as Range tests now produce results).
  - **Deferred**: live Range mutation tracking (Range auto-collapse on DOM
    change), Selection implementation beyond stub, `cloneContents` /
    `extractContents` / `surroundContents` full implementation (blocked by
    absent iframe support; 4176 NOTRUN remain in iframe-driven tests).
  - **Out-of-scope correctly failing**: `dom/ranges/tentative/OpaqueRange-*`
    (CSS Anchor Positioning API, not standard Range).
  - No regressions: Dom 39, Bindings 219, Js 1840 (+1 pre-existing
    Captured_lexical failure unchanged).

| Remaining cluster (post WPT-02 baseline) | Subtests | Why it's big |
|---|--:|---|
| `notrun` (iframe-gated) | 4877 | async_test tests via iframes never complete |
| `assert_equals` (Phase 3) | 1516 | semantic; per-area correctness |
| `removeAllRanges` (Selection) | 827 | Selection API deferred |
| dom/traversal (`createNodeIterator`, `createTreeWalker`) | 1549 | entirely absent subsystem |
| `assert_throws_dom` | 257 | Event dispatch + Range DOMException gaps |
| `timeout` | 230 | Event dispatch async |

- 2026-05-26: **WPT-05 Attr / createAttribute(NS) / NamedNodeMap complete.**
  DOM §4.9 Attr-as-a-Node: new `AttrNode : Node` class with `name`, `localName`,
  `prefix`, `namespaceURI`, `value` (live → propagates to ownerElement), `ownerElement`,
  `specified`; migrated `NamedNodeMap` to `List<AttrNode>`; identity preservation in
  `Element.SetAttribute` (mutates in-place); `JsNamedNodeMapObject` exotic with
  `GetOwnPropertyDescriptor` + `Keys` overrides for indexed/named property access;
  `InstallAttr` + `InstallNamedNodeMap` in `NodeBindings`; bonus:
  `getAttributeNode`/NS, `setAttributeNode`/NS, `removeAttributeNode`,
  `toggleAttribute`, `hasAttributes`, `getAttributeNames`; `appendChild`/
  `insertBefore`/`replaceChild` throw `HierarchyRequestError` for `AttrNode` children;
  `document.createAttribute` (WHATWG permissive — any non-empty string) and
  `createAttributeNS`; HTML documents lower-case names; XML docs (`IsHtml=false`)
  preserve case.
  - **WPT result**: pass **1459→1604 (+145)**, rate **27.79%→30.60%** (+2.81pp).
    `attributes-are-nodes.html` 4/4, `Document-createAttribute.html` 36/36,
    `attributes-namednodemap.html` 8/8, `namednodemap-supported-property-names.html` 3/3.
  - **Unit tests**: 29 new `AttrNodeTests` (DOM layer) + 38 new `AttrBindingTests`
    (JS binding) — all green.
  - Full suite green: Dom 68, Bindings 257, Js 1840 (pre-existing `Captured_lexical`
    unchanged), no regressions in other areas.
