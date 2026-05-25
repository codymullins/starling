---
id: "wp:M3-70-js262-module-code"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main 2ff66ed (also js262-phase0-2; pushed origin/js-262). Cherry-pick conflicts with WP-69 in JsParser.cs/Statements.cs resolved (both await/flag changes coexist; kept WP-69's broader return-outside-fn check)."
impact: "language 91.44%->91.75% (+0.31pp, +130). module-code 422->516 (88%); import 4/4, export 3/3 (100%). ZERO regressions. Added: module-goal ParseModule + top-level await, module early errors (new JsParser.ModuleEarlyErrors.cs), import-binding immutability (ThrowConstAssignment opcode) + TDZ. Skipped: module-namespace exotic objects (§10.4.6)."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-70 — JS: module-code conformance (top-level await, module early errors, bindings)

## Why (evidence)
`module-code` is the biggest concentrated remaining cluster: **161 failing**
(~422/584 = 72%). Breakdown:
- **24** `SyntaxError: await is only valid in async functions and async generators`
  — top-level `await` in a module is rejected, though the engine supports TLA
  elsewhere (`TopLevelAwaitTests` pass). The test262 module parse/compile path
  doesn't enable module-goal TLA.
- **39** `expected parse error, parsed OK` — module early errors (duplicate
  export names, escaped-keyword import/export specifiers, `import`/`export`
  binding rules). WP-69 skipped these (its worktree lacked the module path).
- **13** `expected SyntaxError, … should not be evaluated` — early errors that
  must prevent evaluation.
- **~21** module binding semantics: import binding TDZ ("created but not
  initialized" → ReferenceError), const-import "binding rejects assignment"
  → TypeError.

## Scope
- Enable top-level `await` when parsing/compiling a module (the test262 path
  runs modules via the runner's `CompileModule` / `ModuleLoader.LoadAndEvaluate`).
  Find why module-goal parsing rejects top-level await and fix it.
- Implement module early errors in `JsParser.Modules.cs` / `JsCompiler.Modules.cs`
  (duplicate exported names, invalid/escaped specifiers, importing an undefined
  export, etc.), driven by the failing tests under
  `/Users/cody/code/starling/testdata/test262/test/language/module-code/`.
- Module binding semantics: imported bindings are immutable (assignment →
  TypeError) and TDZ before init (→ ReferenceError) where the tests require.

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=module-code` (and `import`/`export`)
  failures drop sharply (was 161); report before/after; no regression elsewhere.
- Focused unit tests (there are existing module tests — `TopLevelAwaitTests`,
  `ModuleDestructuringTests`, `DynamicImportTests`, `Modules/JsModuleTests` — to
  match) for: module TLA accepted; representative module early errors rejected;
  import-binding immutability/TDZ.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
