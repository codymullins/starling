---
id: "wp:M3-83-js262-cross-realm-eval-context"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-83 — JS: cross-realm execution context (`$262.createRealm`)

## Why (evidence)
28 failures all surfacing as `TypeError: eval requires an active execution
context`, from ~18 distinct tests that use `$262.createRealm()` and then run
code in the foreign realm. Representative:
- `types/reference/{get,put}-value-prop-base-primitive-realm.js`:
  `other = $262.createRealm().global; other` runs `value.test262` via the
  foreign realm's eval.
- `expressions/class/private-*-brand-check-multiple-evaluations-of-class-realm*`
  (13) — class private brand checks across realms.
- `expressions/{super,generators,async-generator,tagged-template,call}/*realm*`,
  `eval-code/indirect/realm.js`.

Root cause: when code enters a **second realm** (via that realm's global eval,
or a function/class defined in it), the engine has no execution context pushed
for that realm, so the eval op (and presumably any context-dependent op) fails
the "active execution context" assert. The `$262.createRealm` host hook creates
the realm object but execution into it doesn't establish/restore the running
execution context + realm.

## Scope
- Make entering another realm push a proper execution context bound to that
  realm (and pop/restore on exit), so the eval op and other ops that read the
  running context's realm work. Cover at minimum: the foreign realm's global
  eval, and invoking functions/constructors that close over the foreign realm.
- Verify `$262.createRealm()` returns a realm whose `global`, eval, and
  intrinsics are distinct from the parent's (cross-realm identity) and whose
  evaluated code resolves globals against the foreign global environment.
- Keep single-realm behavior unchanged; this is additive context management.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=realm` failures drop sharply (the "active execution
  context" cluster, was 28); report before/after; regression-scan every
  category. (`realm` also matches a few unrelated passing tests — confirm no new
  regressions.)
- Unit tests: a realm from `$262.createRealm()` whose `global.x = 1` evaluates
  the source `x` (via the realm's eval) to `1`, resolving against the foreign
  global (not the host's); the realm's `Object` intrinsic is not identical to the
  host `Object` (distinct intrinsics); a function returned from the realm's eval
  runs with the foreign realm active.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.

## Done
Landed in `8201ca8 fix(js): cross-realm execution context for $262.createRealm (§9.3)`.
"eval requires an active execution context" 28 → **0** (cleared). `realm` filter
0/30 → 20/30 (remaining 10 are unrelated: generator proto identity, super
SameValue, put-value on primitive base). Implementation: `JsRealm.OwnerRuntime`
back-reference, `JsFunction.Realm` stamped at construction, `JsVm.CallFunction`
and `ConstructFunction` route to the callee's realm VM when it differs from the
running VM's realm, and `AbstractOperations.Call`/`Construct` fall back to the
callee realm's runtime when invoked without an active VM. Foreign-realm
`global.eval` recovers a VM via `OwnerRuntime.WithActiveVm`.
