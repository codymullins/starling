---
id: "wp:M3-76-js262-subclass-builtins"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-76 — JS: subclassing built-in constructors (derived super() with new.target)

## Why (evidence)
~80-100 coherent failures under `statements/class/subclass/builtin-objects` (46)
+ `subclass-builtins/*` (`subclass-WeakSet/WeakRef/WeakMap/URIError/TypeError`…
"Expected true but got false") + "`UintNArray`/`IntNArray` constructor requires
'new'" (26). Root cause: `class X extends <builtin> {}` then `new X()` — the
derived constructor's `super(...)` must invoke the BUILT-IN (native) constructor
as a **construct** with the derived class's `new.target`, producing an instance
whose `[[Prototype]]` is `X.prototype`. Currently super() to a native ctor either
throws "requires 'new'" (TypedArray/Map/etc. new.target check fails) or builds a
plain object (so `instanceof`/internal-slot checks fail → "Expected true but got
false").

## Scope
- Make `super(...)` to a native/built-in constructor a proper [[Construct]] with
  `newTarget` = the running derived class (so OrdinaryCreateFromConstructor uses
  `X.prototype`). Covers TypedArrays, Map/Set/WeakMap/WeakSet/WeakRef, Error
  subclasses, Array, etc. Look at how the VM dispatches `super(...)` / native
  constructor `[[Construct]]` and thread new.target.
- Native ctors that require `new` must accept the super()-construct path.
- Verify the instance gets the derived prototype + the built-in's internal slots.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=subclass` (statements/class/subclass*) failures drop
  sharply; report before/after; regression-scan every category.
- Unit tests: `class A extends Array{}; new A() instanceof Array && instanceof A`;
  `class M extends Map{}; new M().set(1,2).get(1)===2`; `class E extends TypeError{}`;
  a TypedArray subclass constructs.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.
