---
id: "wp:M3-75-js262-compound-assignment"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-75 — JS: compound-assignment correctness (§13.15) — the biggest remaining cluster

## Why (evidence)
`expressions/compound-assignment` has **325** failures — the largest remaining
cluster (hidden earlier by SameValue normalization). Sub-patterns:
1. **~44+ `scope.x === N. Actual: undefined`** (`S11.13.2_A5.*`): `x *= y` inside
   `with(scope)` where `scope` has a self-deleting getter. §13.15.2 requires the
   compound assignment to evaluate the LHS Reference ONCE and `PutValue` to that
   SAME reference — even if the binding vanished mid-evaluation. The engine
   re-resolves the name for the store (the `with` load/store are separate
   lookups), so the write misses `scope` and hits the outer `x`.
2. **operator-value bugs**: `x >>>= new Boolean(true)`, `x %= new String("1")`,
   `x %= new Number(1)` → wrong/NaN — object operand coercion in the compound
   operator path.
3. **~44 expected-TypeError / true-false** cases.

## Scope
- Make a compound assignment to a Reference evaluate the reference once and
  store back through it (capture the with/object base from the load, reuse for
  the store). Look at how `with` compiles compound assignment (`WithLoadOrMiss`
  /`WithStoreOrMiss` in `JsCompiler`/`JsVm`) — the two must share one resolved
  base, not two name lookups.
- Fix operator-value coercion so object operands go through the same
  ToPrimitive/ToNumeric path as binary operators (reuse the binary-op seam).
- Compound-assign to const/non-writable/getter-only → TypeError where required.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=compound-assignment` failures drop sharply (was 325);
  report before/after; regression-scan every category (operator/with paths are broad).
- Unit tests for each sub-fix (with-reference reuse; object-operand coercion;
  const-target TypeError).
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author "Cody Mullins <1738479+codymullins@users.noreply.github.com>"
  (the no-reply email — GitHub blocks the plain gmail one).
