# WP-WPT-08: Selection API (W3C Selection API §3)

## Status: complete

## Goal
Replace the placeholder Selection stub (left over from WPT-02) with a
spec-faithful `Selection` host object so the vendored
`selection/` WPT subdirectory passes at >=95%. Baseline before this work
was 0.32% on selection/ (the stub responded to method calls but had no
real state, so every test's first `selection.removeAllRanges()` made the
test setup blow up).

## Scope

**In scope:**
- `SelectionBinding` — installs a `Selection` constructor + prototype per
  realm, wires `window.getSelection()` and `document.getSelection()` to
  return the same per-Document instance (the spec invariant
  `window.getSelection() === document.getSelection()`).
- `Starling.Dom.DomSelection` — host model: associated `Range` (modern
  spec: at most one), `direction` (Forwards / Backwards / Directionless),
  derived `anchorNode/anchorOffset/focusNode/focusOffset/isCollapsed/
  rangeCount/type`.
- Methods: `addRange`, `removeRange`, `removeAllRanges`, `empty`,
  `getRangeAt`, `collapse`, `setPosition` (alias of `collapse`),
  `collapseToStart`, `collapseToEnd`, `extend`, `setBaseAndExtent`,
  `selectAllChildren`, `containsNode`, `deleteFromDocument`, `toString`,
  `modify` (non-standard, no-op), `getComposedRanges` (stub).
- `document.defaultView` returns `null` for documents created via
  `DOMImplementation.createHTMLDocument` / `createDocument` — required
  by `getSelection.html` so foreign-doc selection tests see the right
  defaultView shape.
- Live identity: `addRange(r)` stores the same `DomRange` instance the
  caller passes in. `getRangeAt(0)` returns that same object, so mutating
  the range mutates the selection (and vice versa) — tests assert this.

**Explicitly out of scope (remain failing):**
- `selection/shadow-dom/` — needs ShadowRoot support.
- `selection/contenteditable/`, `selection/textcontrols/` — needs
  `contenteditable` editing + `HTMLInputElement.setSelectionRange`.
- `selection/bidi/` — needs bidi-aware caret movement (layout-driven).
- `selection/caret/`, `selection/canvas-*`, `selection/anonymous/` —
  testdriver `action_sequence` (mouse drag) is unimplemented.
- `selection/Document-open.html` — `document.open()` reset semantics.
- `selection/modify.tentative.html` etc. — `selection.modify()` is a
  layout-aware non-standard API; we expose it as a no-op only.

## Observed delta
- selection/ (focused): 77/23896 (0.32%) → 33244/33640 (**98.82%**)
  - `addRange-*` files: 100% (full 1624×11 + 116 subtest grid)
  - `collapse-*` files: 100%
  - `extend-*` files: 100% (down from 48.9% after fixing the
    cross-tree branch — see "Tricky bit" below)
  - `setBaseAndExtent.html`: 100%
  - `selectAllChildren.html`: 92.1% (remaining failures are layout-
    related: selection across `display:none`)
  - `removeAllRanges` / `removeRange` / `isCollapsed` / `type` /
    `collapseToStartEnd` / `getRangeAt` / `extend-exception` /
    `deleteFromDocument` / `toString-ff-bug-001`: 100%

## Tricky bit — `extend` across trees
The spec branches:
1. If node's root isn't the associated Document, silently abort.
2. Else if range is null, throw `InvalidStateError`.
3. Doctype → `InvalidNodeTypeError`; offset OOB → `IndexSizeError`.
4. If anchor's root differs from node's root (anchor got detached after
   selection was set, but the new focus is in the live document), set
   range start AND end to (node, offset) — the new range is just the
   focus point.
5. Else build the new range from anchor↔focus and set direction.

The first cut threw `InvalidStateError` in branch 4, which turned 2236
extend subtests red. Replacing the throw with "collapse the range onto
the new focus" took them all back to green.

## Files changed
- `src/Starling.Dom/DomSelection.cs` — new (host model)
- `src/Starling.Bindings/SelectionBinding.cs` — new (JS bindings)
- `src/Starling.Bindings/WindowBinding.cs` — wire
  `SelectionBinding.Install` after `RangeBinding.Install`. Removed the
  old `InstallSelectionStub` (the wp-WPT-07 stub that returned a
  freestanding plain object with method shims).
- `src/Starling.Bindings/RangeBinding.cs` — removed
  `InstallSelectionStub` and the `document.getSelection` shim;
  SelectionBinding owns both surfaces now.
- `src/Starling.Bindings/NodeBindings.cs` —
  `document.defaultView` returns null when the document isn't the
  realm's own (so foreign docs created via `createHTMLDocument` etc.
  report `defaultView === null` per spec).
- `tools/fetch-wpt.sh` — added `selection` to the default `WPT_DIRS`
  cone so the runner has the subdir to measure against.

## Deferred items
- Shadow-DOM-aware selection (`getComposedRanges` returns []).
- `selection.modify(alter, direction, granularity)` is exposed as a
  no-op; implementing it needs layout-aware caret movement.
- `selection/contenteditable/*` and `selection/textcontrols/*` need
  contenteditable editing + `setSelectionRange` on form controls.
- testdriver `action_sequence` so the click/drag-driven caret tests can
  run at all (currently produce promise-test "Unhandled rejection").
