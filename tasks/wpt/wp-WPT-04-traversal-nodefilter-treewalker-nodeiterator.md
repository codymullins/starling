---
id: WPT-04
title: dom/traversal — NodeFilter + TreeWalker + NodeIterator (DOM §6)
status: in_progress
area: wpt / dom
baseline: 27.79% (1459/5250, dom,css,url, sha-pinned, post-WP-01)
---

## Goal
Build DOM Traversal (DOM Living Standard §6): `NodeFilter` constants object,
`TreeWalker`, `NodeIterator`, plus `document.createTreeWalker(root, whatToShow,
filter?)` and `document.createNodeIterator(root, whatToShow, filter?)`.

## Why these tests fail today (measured)
- `dom/traversal` is **0/52 (0%)** — entirely absent subsystem.
- `missing-method:SHOW_ELEMENT` 15 surfaces in the cause histogram (tests
  reading `NodeFilter.SHOW_ELEMENT`).

Predicted Δ: **~40–50** (most of the 52 once the subsystem exists; expect a
handful of edge cases left in assert_equals tail).

## Scope (in)
1. **`NodeFilter`** as a window-global constants object exposing
   `SHOW_ALL`/`SHOW_ELEMENT`/`SHOW_ATTRIBUTE`/`SHOW_TEXT`/`SHOW_CDATA_SECTION`/
   `SHOW_ENTITY_REFERENCE`/`SHOW_ENTITY`/`SHOW_PROCESSING_INSTRUCTION`/
   `SHOW_COMMENT`/`SHOW_DOCUMENT`/`SHOW_DOCUMENT_TYPE`/`SHOW_DOCUMENT_FRAGMENT`/
   `SHOW_NOTATION` (per §6.1), and `FILTER_ACCEPT`/`FILTER_REJECT`/`FILTER_SKIP`.
2. **`TreeWalker`** (§6.2): readonly `root`, `whatToShow`, `filter`;
   mutable `currentNode`; navigation: `parentNode`, `firstChild`, `lastChild`,
   `previousSibling`, `nextSibling`, `previousNode`, `nextNode`. Filter
   invocation per spec (catch filter-thrown exceptions and surface as the
   walk function's exception, per §6.2.1).
3. **`NodeIterator`** (§6.3): same readonly props + `referenceNode`/
   `pointerBeforeReferenceNode`; navigation: `nextNode`, `previousNode`,
   `detach()` (no-op per spec for compatibility).
4. **Factory methods**: `document.createTreeWalker(root, whatToShow=0xFFFFFFFF,
   filter=null)` and `document.createNodeIterator(...)`. Both throw `TypeError`
   if `root` is missing (per Web IDL).
5. **Live updates for NodeIterator**: §6.3.1 "removing steps" — NodeIterator
   adjusts referenceNode on node removal. Only implement if tests depend on
   it (predict-then-verify).

## Scope (out)
- TreeWalker live-update on mutation (spec says implementations may diverge;
  most tests assume static walks).

## Acceptance
- Measured Δ on full suite; report `pass X→Y` and `dom/traversal A%→B%`.
- No regression elsewhere.
- MSTest: per-method spec test ([Spec(...,"6.2.1"...)]), filter-throw
  propagation, NodeFilter constant values match spec.
- PLAN.md status log; WP doc → `complete`.

## Notes (recon)
- DOM types: `src/Starling.Dom/`. No existing TreeWalker — fresh subsystem.
- Binding: follow WP-01 pattern; interface objects on window can use the
  existing prototype-installation pattern (see Bindings family files).
- The DOM tree-order traversal algorithms (§6.2.2 "Traverse children/siblings")
  are precise — port them literally, don't reinvent. Easy to get wrong.
