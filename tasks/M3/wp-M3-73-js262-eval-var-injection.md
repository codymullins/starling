---
id: "wp:M3-73-js262-eval-var-injection"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: ["wp:M3-72-js262-eval-scope-access"]
blocks: []
---

# wp:M3-73 — JS: direct eval injects its var/function bindings into the caller's scope

## Why (evidence)
The remaining `eval-code` gap (~48%) and the deferred half of WP-72. A direct
eval's own top-level `var`/function declarations must create bindings in the
**caller's** VariableEnvironment (§19.2.1.3 EvalDeclarationInstantiation), so
they're visible to the caller after the eval returns and to later code. This is
what the bulk of the remaining `eval-code` binding tests need. WP-72b did
caller-scope READ + conflict early-errors but DEFERRED injection, because the VM
uses fixed slot-based locals.

## Scope (the hard part — bounded)
- Add a dynamic per-frame binding store (e.g. `Dictionary<string, Cell>` on the
  call frame) that direct-eval'd `var`/function declarations populate in the
  CALLER's frame (function caller). A global/script-top caller already injects
  into the global object — keep that.
- Name resolution (in the caller AND in the eval'd code) consults: slots →
  upvalues → this dynamic store → global, in spec order. Extends WP-72b's
  `EvalScope` / `LoadEvalScope` / `StoreEvalScope`.
- Function declarations in the eval body hoist into the caller var-env;
  re-declaring an existing var is idempotent; a var colliding with a caller
  lexical is the SyntaxError WP-72b already handles.
- Keep STRICT eval's own var-env (no injection) — already correct.

## MUST build on current main (avoid the WP-72 stale-base trap)
The agent MUST first `git fetch origin && git reset --hard origin/js-262` and
verify WP-72b's `EvalScope`/`EvalScopeDescriptor`/`LoadEvalScope`/`StoreEvalScope`
+ WP-71's `DirectEval` are present, then EXTEND them.

## Acceptance
- `STARLING_TEST262_FILTER=eval-code` improves; report before/after; regression-
  scan every category.
- Unit tests: a direct eval of a `var`-declaration is visible in a function
  caller afterward; an eval'd function declaration becomes callable in the
  caller; strict eval doesn't leak; redeclare is idempotent.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
