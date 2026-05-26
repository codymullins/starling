---
id: WPT-03
title: DOMException-throwing methods + interface constructor objects on window
status: complete
area: wpt / dom / bindings
baseline: 27.79% (1459/5248, dom,css,url, sha-pinned, post-WP-01)
result: 20.92% (3813/18225); absolute +2354 passes; denominator grew +12977 (interface ctors unblocked many subtest expansions)
---

## Goal
Two paired plumbing tasks:
1. Expose missing **interface constructor objects** (Document, Element, Node,
   Event, MouseEvent, KeyboardEvent, NodeList, HTMLCollection, NamedNodeMap,
   Attr, NodeFilter, TreeWalker, NodeIterator, etc.) as `window` globals so
   `instanceof X` and `X.prototype.…` checks resolve.
2. Wire **`DOMException`-throwing** into methods spec'd to throw it
   (HierarchyRequestError, NotFoundError, WrongDocumentError, InvalidStateError,
   IndexSizeError, SyntaxError, InvalidModificationError, NoModificationAllowedError).

## Why these tests fail today (measured)
- `assert:assert_throws_dom` 244 — methods don't throw the spec'd DOMException
  (e.g. `dom/events/EventTarget-dispatchEvent.html` — dispatching an already-
  dispatched event must throw `InvalidStateError`).
- `other:Right-hand side of 'instanceof' is not an object` 48 — `instanceof X`
  where `X` (Document/Element/etc.) isn't defined as a global.
- `missing-ctor:window` 32 — same flavour, window-side interface lookup.
- `missing-ctor:(unknown)` 15.

Predicted Δ: **~150–200** real passes (the 244 assert_throws_dom cluster
dilutes — WP-01's 101→33 conversion ratio suggests similar here for
broad throwers; the 95 instanceof/ctor cluster should convert >70%).

## Scope (in)
1. **Window-global interface constructor objects**: produce a JS function/object
   per host interface, with `.prototype` chain matching the existing wrapper
   prototypes (so `el instanceof Element` works). Cover at minimum: `Node`,
   `Element`, `HTMLElement` (basic), `Document`, `HTMLDocument`, `DocumentType`,
   `DocumentFragment`, `CharacterData`, `Text`, `Comment`, `Attr`, `NamedNodeMap`,
   `NodeList`, `HTMLCollection`, `Event`, `CustomEvent`, `EventTarget`,
   `MouseEvent`, `KeyboardEvent`, `UIEvent`, `FocusEvent`, `MutationObserver`,
   `MutationRecord`, `Range` (if WPT-02 lands first; otherwise stub the
   constructor and let WPT-02 fill `.prototype`), `NodeFilter` constants
   object (if WPT-04 lands first), `DOMException`, `CSSStyleSheet`,
   `CSSStyleRule`, `CSSStyleDeclaration`, `StyleSheetList`, `CSSRuleList`.
   Use the existing prototype slots — don't create a parallel hierarchy.

2. **DOMException throwing** at every method spec'd to throw, prioritised by
   `causes.txt`:
   - Tree mutation (`appendChild`/`insertBefore`/`replaceChild`/`removeChild`):
     HierarchyRequestError (parent-type check, ancestor-loop check),
     NotFoundError (refChild not in parent), WrongDocumentError.
   - Event: `dispatchEvent` while already dispatching → InvalidStateError;
     `initEvent` after dispatch → no-op (not throw).
   - CharacterData substring/replaceData/insertData/deleteData → IndexSizeError
     when offset > length.
   - DOMTokenList add/remove/toggle/contains with empty string or whitespace
     → SyntaxError / InvalidCharacterError.
   - Attribute name validation (createAttribute/setAttribute) — already done
     for createElement in commit `4706ac9`; mirror for Attr family **only if
     WPT-05 isn't doing it**. Coordinate via PLAN.md / cause counts.

## Scope (out)
- Building a full Web IDL interface registry — minimal "constructor object with
  prototype" suffices.
- Methods spec'd to throw that no current WPT test exercises (predict-first).

## Acceptance
- Measured Δ on full suite; report `pass X→Y` and how the assert_throws_dom +
  instanceof clusters shrank.
- No regression elsewhere (instanceof checks may surface incorrect prototype
  wiring — guard).
- MSTest regression: a smoke test per added interface constructor confirms
  `instanceof` + `.prototype.<method>` resolves; a test per added throw
  confirms the right `DOMException.name`.
- PLAN.md status log; WP doc → `complete`.

## Notes (recon)
- DOMException type already exists (PLAN.md status `4706ac9`).
- Prototype hierarchy already exists in `src/Starling.Bindings/` — each binding
  family installs `XPrototype` on the realm. Walk the existing prototypes and
  expose them as `window.X = <ctor>` with `ctor.prototype = XPrototype`.
- **Run the WPT measurement AFTER any preceding WP (02/04/05/06) has been
  cherry-picked onto your tree** — the SME (Cody) will provide a merged base
  before this WP's measurement run. Until then, build against `main`
  (`c92c129`) and measure relative to that.
