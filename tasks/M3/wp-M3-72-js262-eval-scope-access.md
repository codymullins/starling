---
id: "wp:M3-72-js262-eval-scope-access"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "deferred-not-integrated"
phase: "3"
finding: "STALE-BASE COLLISION: the agent's worktree (branch worktree-agent-ad6eb6a393ebb46f6 / commit c8160d4, base 4ae0c46) PREDATES WP-71, so it rebuilt direct-eval detection from scratch — its own DirectEval opcode, EvalIntrinsic slot, PerformDirectEval, ParseEvalProgram — which DUPLICATES WP-71 (already on main: DirectEval opcode, EvalFunction, DirectEvalContext). Cherry-pick would be a duplicate-opcode compile error + two parallel eval paths. NOT integrated. Its reusable core (caller-scope READ/WRITE via EvalScope/EvalScopeDescriptor + LoadEvalScope/StoreEvalScope + EvalDeclarationInstantiation early errors) must be RE-IMPLEMENTED on top of WP-71's DirectEval/DirectEvalContext (extend, don't duplicate). It also DEFERRED the dominant gap (injecting eval'd var/function bindings into the caller var-env), so true yield is modest. Reference impl: c8160d4."
subsystem: "Starling.Js"
depends_on: ["wp:M3-71-js262-direct-eval-context"]
blocks: []
---

# wp:M3-72 — JS: direct eval caller variable-scope access + EvalDeclarationInstantiation

## Why (evidence)
The deferred half of WP-71 and the dominant remaining `eval-code` cluster
(~46%). Of the 245 eval-code failures:
- **137** `Expected a SyntaxError to be thrown` — §19.2.1.3 EvalDeclarationInstantiation
  early errors: a `var`/function declaration in the eval'd code that collides
  with the caller's lexical binding (let/const/class), or a `var arguments`
  colliding with a parameter named `arguments`, etc., is a SyntaxError. These
  need the eval compile to KNOW the caller's bindings.
- **15** `ReferenceError: x is not defined` + some SameValue — the eval'd code
  must READ the caller's local bindings (params, let/const/var) by name (WP-71
  left free identifiers resolving globally).

## Scope (hard — do the tractable sub-pieces; defer the rest)
- Thread the caller's lexical scope (binding names + a way to resolve them) into
  a DIRECT eval's compile (built on WP-71's `DirectEvalContext` / `DirectEval`
  opcode). The slot-based VM uses indexed locals; options: thread the caller's
  bindings as upvalues/name→cell map into the eval chunk, or a name-resolved
  scope handle.
- **EvalDeclarationInstantiation early errors** (the 137): detect var/function
  declarations in the eval body that conflict with the caller's lexical bindings
  → SyntaxError; the `arguments`/`eval`-named-binding rules in strict eval.
- **Caller-local READ access** (the 15 + SameValue): a free identifier in eval'd
  code resolves to the caller's local binding when present.
- DEFER if intractable: writing new `var`/function bindings INTO the caller's
  var-environment so they persist after the eval returns — note it.

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=eval-code` improves sharply (was 46%);
  report before/after; **regression-scan every category** (scope/eval changes
  are broad and risky).
- Focused unit tests: a direct eval reads a caller `let`/param; EvalDeclaration
  var/lexical conflict throws SyntaxError; indirect eval still global-scoped.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.

## REDO landed (WP-72b)
Re-done on the correct base (branch worktree-agent-a86b9b84abf5bae55, commit 0d5a9c2),
EXTENDING WP-71's DirectEval (no duplication). Integrated main 5b897fd / pushed
origin/js-262. Caller-scope read/write + EvalDeclarationInstantiation conflict
early-errors. Impact: language 92.17%->92.20% (+14; eval-code 46%->48%); ZERO
regressions. STILL DEFERRED: injecting the eval body's own new var/function
bindings into the caller var-environment (needs a dynamic frame binding store —
hard in the slot-based VM) — this is what the bulk of the remaining eval-code
SyntaxError early-errors need.
