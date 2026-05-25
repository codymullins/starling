---
id: "wp:M3-71-js262-direct-eval-context"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main 5c235dd (also js262-phase0-2; pushed origin/js-262). 12 cherry-pick conflicts hand-resolved across JsParser.cs/.Classes/.Statements (merged WP-69/70 _moduleTopAwait save/reset/restore with WP-71 _superPropertyDepth in every method/function scope)."
impact: "language 91.75%->92.17% (+0.42pp, +182). super-keyword-unexpected 62->0; eval-code 44%->46%. ZERO regressions. Added DirectEval opcode + direct/indirect detection (compile + runtime %eval% identity), caller lexical-context threading (strict/super-homeobject/new.target/this), _superPropertyDepth super-gating. DEFERRED: caller variable-scope access (EvalDeclarationInstantiation) — separate larger sub-feature."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-71 — JS: direct eval inherits the caller's lexical context

## Why (evidence)
The last high-value lever. `eval-code` is ~44%. The big concentrated failures:
- **139** `eval-code/direct` `Expected a SyntaxError to be thrown` — early errors
  in the evaluated code that only fire when it is compiled with the caller's
  context (in-function, in-method, strict, derived-ctor).
- **62** `'super' keyword unexpected here` — almost all
  `…direct-eval-contains-superproperty…`: a direct-eval of `super.x` inside a
  method needs the caller's `[[HomeObject]]`.
- **~28** class direct-eval TypeError cases.

Root cause: the engine doesn't distinguish a **direct** eval (§19.2.1.1, callee
is the intrinsic `eval` invoked directly) from an indirect one, and the evaluated
code is compiled standalone without the caller's lexical context. (WP-67 tried
caller-strictness only and was reverted because, lacking the direct/indirect
distinction, it wrongly made the *indirect* form inherit strictness.)

## Scope (tractable, high-value sub-piece — defer the rest)
- **Direct-eval detection:** a CallExpression whose callee is an `eval`
  IdentifierReference resolving to the realm `%eval%` (not shadowed) is direct.
  Emit a distinct path that passes the current execution context.
- **Inherit lexical context** into the evaluated code's parse+compile: caller
  strictness (plus its own `"use strict"`); whether inside a function / method
  (so a SuperProperty is allowed and uses the caller's `[[HomeObject]]`);
  `new.target` availability; `this`; derived-constructor-ness. This makes the
  early errors fire correctly AND a direct-eval SuperProperty resolve against the
  caller's home object.
- Keep the **indirect** form sloppy-by-default (no strictness inheritance) — the
  direct/indirect distinction is what makes this correct.
- **DEFER (note as follow-up):** full caller *variable-scope* access (the
  evaluated code reading/declaring the caller's let/const/var locals by name,
  EvalDeclarationInstantiation). That is a separate, larger sub-feature.

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=eval-code` and the
  `*direct-eval-contains-superproperty*` subset improve sharply; report
  before/after; **regression-scan every category** (eval/call changes are broad).
- Focused unit tests: a direct-eval SuperProperty in a method resolves; the
  direct form inherits caller strict mode (early errors); the indirect form
  stays sloppy; `new.target`/`super` early errors fire by caller context.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
