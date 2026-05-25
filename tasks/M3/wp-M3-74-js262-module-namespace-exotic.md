---
id: "wp:M3-74-js262-module-namespace-exotic"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-74 — JS: Module Namespace Exotic Object (§10.4.6)

## Why (evidence)
A coherent ~94+ cluster WP-70 deferred. `dynamic-import` SameValue failures are
overwhelmingly `namespace/*` / `ns-*` (56 "namespace" + 50 "ns-" themes):
`await-ns-define-own-property`, `await-ns-extensible`, `ns-delete-non-exported-*`,
`toStringTag`, ordering, get/set/has on the namespace object. Plus the analogous
`module-code` namespace tests. The module namespace object is currently an
ordinary object, not the §10.4.6 exotic.

## Scope
Make the namespace object built by `ModuleLoader` (`GetOrBuildNamespace`) a
§10.4.6 Module Namespace Exotic Object with the spec internal methods:
- `[[Get]]`: string key → the exported binding's current value (live); a
  non-export → undefined; `@@toStringTag` → "Module".
- `[[Set]]` / `[[DefineOwnProperty]]`: always fail (return false; throw in
  strict) — namespace is read-only/non-configurable.
- `[[Delete]]`: an existing exported name → false; a non-existent key → true.
- `[[HasProperty]]`: exports + `@@toStringTag`.
- `[[GetOwnProperty]]`: exports are enumerable, writable:true, configurable:false
  data props (value = live binding); `@@toStringTag` non-enumerable/non-writable/
  non-configurable "Module".
- `[[OwnPropertyKeys]]`: the exported names **sorted** (Array.prototype.sort
  order / code-unit), then `@@toStringTag`.
- `[[IsExtensible]]` → false; `[[PreventExtensions]]` → true; `[[GetPrototypeOf]]`
  → null; `[[SetPrototypeOf]]` → only accepts null.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262` so you have WP-70's
module loader + WP-73's eval work, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=namespace` (+ dynamic-import/module-code namespace)
  failures drop sharply; report before/after; regression-scan every category.
- Unit tests for each exotic behavior; live binding values via the namespace.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
