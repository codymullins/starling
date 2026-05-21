---
id: "wp:M3-03c-js-dynamic-import"
parent: "wp:M3-03-js-compiler"
milestone: "M3"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on:
  - "wp:M3-03b-js-top-level-await"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#modules"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03c — JS: dynamic `import()` + `import.meta`

## Goal
Support `import(specifier)` call expressions (returning a Promise of the module
namespace) and the `import.meta` meta-property (at least `import.meta.url`) in ES
modules. Blocked on `wp:M3-03b` because dynamic import resolves through the
loader's async-evaluation path (a dynamically-imported TLA module must settle
before the returned Promise resolves).

## Scope (to be detailed when unblocked)
- Parser: recognize `import` `(` as an expression (dynamic import) and `import`
  `.` `meta` as a meta-property, distinct from static `import` declarations.
  `src/Starling.Js/Parse/JsParser.cs` / `JsParser.Modules.cs`.
- AST: new nodes in `src/Starling.Js/Ast/Modules.cs` (e.g. `ImportCallExpression`,
  `ImportMetaExpression`).
- Compiler: `src/Starling.Js/Bytecode/JsCompiler.Modules.cs` — emit a runtime
  call into the loader for `import()`; resolve `import.meta` to the module's meta
  object.
- Loader/runtime: `ModuleLoader` public async "load + evaluate to Promise" entry
  (reuse the `wp:M3-03b` path); `ModuleRecord` gains a `Meta` object
  (`import.meta`); `JsVm` binds the active loader so the import opcode can call
  it.

## Acceptance (draft)
- `await import('./m.js')` resolves to the namespace; `.default`/named exports
  readable.
- dynamic import of a module with top-level await resolves only after it settles.
- `import.meta.url` returns the module's URL.
- rejected/missing specifier → rejected Promise.
- New tests in `tests/Starling.Js.Tests/DynamicImportTests.cs`.

## Handoff log
- 2026-05-21T01:26:13Z — created as blocked (await spine, step 2 of 2); unblocks when wp:M3-03b completes
