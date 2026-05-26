---
id: WPT-02
title: Standard DOM Range + Selection (DOM §4.6, §5)
status: in_progress
area: wpt / dom
baseline: 27.79% (1459/5250, dom,css,url, sha-pinned, post-WP-01)
---

## Goal
Build the standard DOM `Range` model (DOM Living Standard §4.6) and a minimal
`Selection` (§5), wire `document.createRange()` and `window.getSelection()`, and
expose `Range` as a window interface constructor. Targets `dom/ranges`
(currently 1/224, 0.4%) plus `createRange` uses scattered across `dom/nodes`.

## Out of scope — IMPORTANT
- `dom/ranges/tentative/OpaqueRange-*` files: this is the experimental **CSS
  Anchor Positioning** `OpaqueRange` / `createValueRange` API (~109 subtests:
  `createValueRange` 61 + `clear` 19 + `length` 13 + `e` 16). **NOT standard
  Range** — skip these. They will continue to fail; that is correct.
- `HTMLInputElement.setSelectionRange` (15) — text-input selection, not Range.
- Live boundary-point updates under DOM mutation (DOM §4.6.1) — first-pass
  implementation may defer. Only implement if `causes.txt` shows assert_equals
  failures that depend on it (predict-then-verify).

## Why these tests fail today (measured)
- `missing-method:createRange` (54), e.g. `dom/nodes/MutationObserver-characterData.html`.
- ~30–50 downstream `assert_equals` in `dom/ranges` once `createRange` works.

Predicted Δ subtests: **~70–100** (createRange unblocks + assert_equals cascade).

## Scope (in)
1. **`Range`** (§4.6) as a JS-visible host object: ctor, getters
   (`startContainer`/`startOffset`/`endContainer`/`endOffset`/`collapsed`/
   `commonAncestorContainer`), boundary methods (`setStart`/`setEnd`/
   `setStartBefore`/`setStartAfter`/`setEndBefore`/`setEndAfter`/`selectNode`/
   `selectNodeContents`/`collapse`), introspection (`isPointInRange`/
   `comparePoint`/`intersectsNode`/`compareBoundaryPoints`/`toString`/
   `cloneRange`/`detach`).
2. **Mutation-content methods** (`extractContents`/`cloneContents`/
   `deleteContents`/`insertNode`/`surroundContents`) — **only** if causes.txt
   shows tests that need them. Predict-first; do not build speculatively.
3. **`document.createRange()`** rooted at the document.
4. **`Range` as a window interface constructor** so `instanceof Range` works.
5. **`Selection` (§5) minimal**: `window.getSelection()` returning a Selection
   with `anchorNode`/`focusNode`/`anchorOffset`/`focusOffset`/`isCollapsed`/
   `rangeCount`/`addRange`/`removeRange`/`removeAllRanges`/`getRangeAt`/
   `empty`/`toString`. Default empty state is sufficient — no need to track
   user selection.

## Acceptance
- Measured Δ on full `dom,css,url` suite; report `pass X→Y` and `dom/ranges
  A%→B%`. Predict-then-verify against causes.txt.
- No regressions in css/url/dom-nodes (Range methods may interact with MO).
- MSTest regression tests (AwesomeAssertions, `[Spec]`/`[SpecFact]` per §4.6
  steps): boundary-point invariants, cloneRange, comparePoint matrix,
  `instanceof Range` smoke.
- PLAN.md status log entry; WP doc → `status: complete`.

## Notes (recon)
- DOM types: `src/Starling.Dom/`. Search for any existing Range scaffolding first.
- Binding pattern: follow WPT-01 / `EventTargetBinding.DefineAccessor/DefineMethod`
  + `DomWrappers` identity map. The Bindings backend is **Starling.Bindings**
  (the one WPT runs on).
- Range needs identity (one JS wrapper per host Range); use `DomWrappers` for that.
