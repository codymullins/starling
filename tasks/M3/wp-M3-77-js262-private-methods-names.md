---
id: "wp:M3-77-js262-private-methods-names"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-77 — JS: private methods/accessors + private-name scoping + `#x in obj` crash

## Why (evidence)
~171 failures in the `private` cluster:
- **18 CRASH** `host:InvalidCastException: PrivateNameExpression → Identifier` —
  a code path expects an `Identifier` but gets a `PrivateNameExpression` (likely
  `#x in obj` ergonomic-brand-check, or a private name used in an unhandled
  expression position).
- **52** `Cannot read/write private member … did not declare it` thrown on
  tests that should SUCCEED — over-strict brand check for private
  **methods/accessors** (WP-66 covered fields/brands; this is the method/getter/
  setter access path or `obj.#m()` calls / private access in subtler positions).
- **24** `private name '#m' is not declared in any enclosing class scope`
  (SyntaxError) thrown when it should be valid — private-name resolution
  (forward refs / nested-class enclosing scope / `#x in obj`).

## Scope
- Fix the `PrivateNameExpression`→`Identifier` cast crash: handle private names
  in `#x in obj` (RelationalExpression brand check) and any member/assignment
  positions that assume `Identifier`.
- Correct the private brand check for private methods/getters/setters so valid
  access on an instance that carries the brand works (don't over-throw).
- Fix private-name scope resolution so a `#name` declared in any enclosing class
  body is in scope (incl. forward references and nested classes).

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend
(WP-66 added private brand-checks via `JsObject._privateBrands`; build on it).

## Acceptance
- `STARLING_TEST262_FILTER=private` (class elements private + `#x in obj`)
  failures drop sharply; report before/after; regression-scan every category.
- Unit tests: `#x in obj` true/false (no crash); valid private method/getter/
  setter access works; wrong-receiver still throws; nested-class/forward private
  name resolves.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.
