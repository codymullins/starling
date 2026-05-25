---
id: "wp:M3-68-js262-unresolved-read-referenceerror"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main fa8bbce (also js262-phase0-2; pushed origin/js-262)"
impact: "language 85.47% -> 90.89% (+5.42pp, +2330 tests). ReferenceError-not-thrown 2196->130. ZERO category regressions (all improved). Browser page realm kept lenient (ThrowOnUnresolvedGlobalRead=false) so rendering is protected; test262/default strict."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-68 — JS: unresolved bare global READ throws ReferenceError (with embedder allowlist)

## Why (evidence)
THE dominant remaining cluster: **1,716 sync + 480 async = ~2,196** failures
`Expected a ReferenceError to be thrown but no exception`. Reading a bare
undeclared global currently compiles to `Opcode.LoadGlobal`, which silently
returns `undefined` instead of throwing ReferenceError (§9.1.1 GetIdentifier
Reference → §6.2.5.5 GetValue on an unresolvable Reference is a ReferenceError).
WP-53 added `Opcode.LoadGlobalChecked` (throws when `globalObj.Has(name)` is
false) but only wired it for computed property keys. This generalizes it.

## Scope (engine)
- Compiler: emit `LoadGlobalChecked` (not `LoadGlobal`) for free-identifier
  **reads**. EXCLUDE the operands of `typeof` and `delete` (they must keep the
  silent `LoadGlobal` so `typeof undeclared === "undefined"` and `delete
  undeclared` don't throw). Do NOT touch the **store** path (`StoreGlobal`
  already does the right thing: ReferenceError in strict, global create in
  sloppy).
- Realm config for the embedder allowlist: add `JsRealm.ThrowOnUnresolvedGlobalRead`
  (bool, **default true** = spec-correct, so test262 benefits) and
  `JsRealm.LenientGlobalNames` (HashSet<string>, default empty). In the VM's
  `LoadGlobalChecked`: when the global is unresolvable, throw ReferenceError
  UNLESS `!ThrowOnUnresolvedGlobalRead` OR the name is in `LenientGlobalNames`
  (then push `undefined`).
- This lets the browser realm stay lenient (graceful degradation of
  unimplemented host globals) while test262 / default is strict.

## Out of scope (integrator handles)
- Wiring the GUI/browser realm to lenient mode — the agent must ENUMERATE every
  `new JsRuntime()` / realm-creation site it finds (esp. outside tests) so the
  integrator can opt the browser out.

## Acceptance
- Maintainer-run full `language` sweep: the ~2,196 ReferenceError-not-thrown
  failures drop sharply; report before/after and watch for regressions (e.g.
  code that legitimately read undefined globals).
- Focused unit tests: bare undeclared read throws ReferenceError; `typeof
  undeclared` → "undefined" (no throw); `delete undeclared` doesn't throw;
  declared/var/global reads still work; with `ThrowOnUnresolvedGlobalRead=false`
  (or name in LenientGlobalNames) an undeclared read returns undefined.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work (WP-53 LoadGlobalChecked, WP-66
  JsVm); keep changes isolated; integrator cherry-picks + reconciles.
