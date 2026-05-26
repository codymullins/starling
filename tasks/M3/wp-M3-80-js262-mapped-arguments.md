---
id: "wp:M3-80-js262-mapped-arguments"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-80 — JS: mapped arguments object (live param↔arguments binding)

## Why (evidence)
`arguments-object/mapped` — 40 coherent failures: 27 `SameValue` (the live link
not working) + ~13 descriptor-attribute checks. In a non-strict function with a
simple (no default/rest/destructuring) parameter list, the `arguments` object is
a **mapped** exotic object (§10.4.4): `arguments[i]` and the i-th named parameter
share one binding — writing `arguments[0]` updates the param and vice-versa — and
the indexed properties are writable/enumerable/configurable. The engine's
arguments object isn't (fully) mapped.

## Scope
- Implement the mapped arguments exotic object for non-strict simple-param
  functions: indexed entries live-linked to the corresponding parameter slot
  (read + write both directions), correct property descriptors
  (writable/enumerable/configurable), `length`/`callee`, and the unmapping rules
  (deleting an index, or redefining it as an accessor/non-writable, breaks the
  link per §10.4.4.[[DefineOwnProperty]]/[[Delete]]).
- Strict functions / non-simple params keep the unmapped (ordinary) arguments
  object — don't change those.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=arguments-object/mapped` failures drop sharply (was
  40); report before/after; regression-scan every category.
- Unit tests: writing `arguments[0]` updates the named param and vice-versa
  (non-strict simple params); descriptor attributes correct; deleting/redefining
  an index unmaps it; strict fn args stay unmapped.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.
