---
id: "wp:M3-64-js262-super-in-object-methods"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main 28f5466 (also js262-phase0-2; pushed to origin/js-262)"
finding: "restriction was runtime (HomeObject-null guard), not parser. Gave object-literal methods + arrows a [[HomeObject]] (SetHomeObject opcodes + arrow lexical capture). Also completed AbstractOperations.Set into proper OrdinarySet (writes land on receiver — fixes super[x]=v / Reflect.set). Caveat: arrow lexical `this` still not captured, so arrow-super through a this-reading getter uses wrong receiver (separate feature)."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-64 — JS: allow `super` property access in object-literal methods + lexical arrows

## Why (evidence)
**116** failures `SyntaxError: 'super' is only allowed inside class methods`,
spanning `expressions/super` (38), class (52), `expressions/object` (20),
`computed-property-names/object` (6), `arrow-function` (4), `eval-code/direct`
(2). Samples: `object/method/super.js`, `object/accessor/getter-super.js`,
`arrow-function/lexical-super-property.js`, `eval-code/direct/super-prop-method.js`.

`super.x` (SuperProperty) is valid in any concise **method/getter/setter**,
including **object-literal** ones (they carry a `[[HomeObject]]`), and in
**arrow functions** that lexically inherit `super` from an enclosing method.
The engine over-restricts it to class methods.

## Scope
- Parser: accept SuperProperty (`super.x` / `super[x]`) inside object-literal
  method definitions (concise methods + get/set), not just class methods. Keep
  `super(...)` (SuperCall) restricted to derived-class constructors.
- Compiler/runtime: set `[[HomeObject]]` on object-literal methods so
  `super.x` resolves against the object's prototype; ensure arrow functions
  lexically capture the enclosing method's super/home-object binding.
- Cover computed-key object methods/accessors too.

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=super` (and object/method, arrow
  lexical-super) subsets improve; report before/after (was 116 failing).
- Focused unit tests: `super.x` in an object method/getter/setter resolves via
  the object's prototype; `super.x` inside an arrow nested in an object method
  works; `super(...)` outside a derived constructor still errors.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
