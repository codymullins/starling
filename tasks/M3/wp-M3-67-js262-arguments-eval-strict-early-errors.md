---
id: "wp:M3-67-js262-arguments-eval-strict-early-errors"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "deferred-not-integrated"
phase: "3"
finding: "Parser strict-binding checks for eval/arguments were ALREADY correct. The real gap is SCOPE-AWARE DIRECT EVAL (the bulk of the 139 are noStrict runtime EvalDeclarationInstantiation tests — `var arguments` colliding with a param named arguments). Agent's fix (propagate caller strictness into eval) netted only ~+2 test262 AND introduced a spec deviation: the engine can't distinguish direct vs indirect eval, so it wrongly made INDIRECT eval inherit caller strictness (regressed 1 indirect test). REVERTED. Real fix = a proper direct/indirect-eval + caller-scope feature (separate, larger). Branch worktree-agent-a4cf0d1fd278ba328 / d16ac54 if revisited."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-67 — JS: strict-mode early errors for bindings named `arguments`/`eval`

## Why (evidence)
**139** `eval-code/direct` `Expected a SyntaxError to be thrown but no exception`
— overwhelmingly `arguments`/`parameter-is-named-arguments` variants (arrow,
async-gen, function decls). In strict mode it's an early SyntaxError to use
`eval` or `arguments` as a BindingIdentifier (function/arrow parameter,
`let`/`const`/`var` in some forms, assignment target). These run via direct
`eval`, so the early error must fire when the eval'd code is compiled in a
strict context. The engine doesn't enforce these.

## Scope
- In the parser's strict-mode early-error checks (the `CheckBindingIdentifier`
  path / where strict reserved words are validated), reject `eval` and
  `arguments` as BindingIdentifiers in strict mode: function/arrow/method
  parameters, `let`/`const` declarations, catch bindings, and assignment
  targets where the spec says so. Match the exact forms the failing tests
  exercise (read them under
  `/Users/cody/code/starling/testdata/test262/test/language/eval-code/direct/`).
- Ensure direct `eval` compiles its code in the surrounding strict context so
  the early error fires (check how eval inherits strictness).
- Don't regress sloppy-mode (where `arguments`/`eval` as names are legal).

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=eval-code/direct` failures drop
  sharply (was ~139 SyntaxError-not-thrown); report before/after.
- Focused unit tests: strict-mode param/let/const/assignment named
  `arguments`/`eval` → SyntaxError; sloppy-mode equivalents still parse.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
