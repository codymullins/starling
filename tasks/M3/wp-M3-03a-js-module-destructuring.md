---
id: "wp:M3-03a-js-module-destructuring"
parent: "wp:M3-03-js-compiler"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-moddestruct"
claimed_at: "2026-05-21T01:01:13Z"
branch: "main"
depends_on:
  - "wp:M3-02d-js-parser-destructuring"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#modules"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03a — JS compiler: module-scope destructuring binding patterns

## Goal
Allow destructuring binding patterns (`const { a, b } = obj`, `let [x, ...rest] = arr`,
with nested patterns and defaults) in `const`/`let`/`var` declarations at ES
module top level. Today the module compiler explicitly rejects them. After this
WP, module-scope destructuring binds the extracted names as live module bindings
(upvalue cells), exactly like a plain `const name = …` at module scope.

## Inputs
- The parser already produces `BindingPattern`/`ArrayPattern`/`ObjectPattern`
  AST (`src/Starling.Js/Ast/Patterns.cs`) — done in wp:M3-02d.
- Function-scope destructuring lowering already exists:
  `src/Starling.Js/Bytecode/JsCompiler.cs` — `EmitPatternFromStack` (~lines
  2024–2061) and the broader destructuring helpers (~2024–2216).
- The module compiler is `src/Starling.Js/Bytecode/JsCompiler.Modules.cs`.

## Scope / where to work
- `JsCompiler.Modules.cs`:
  - `EmitModuleVarDecl` (lines ~367–387) currently `throw new
    NotSupportedException("destructuring binding patterns at module top level
    are not yet supported")` (lines ~383–384). Replace that with real lowering.
  - `CollectLocalBindingNames` (~228–250) already reserves names from patterns
    via `PatternNames()` (~197–225) as module upvalue slots — verify; likely no
    change needed.
  - Add a helper (e.g. `EmitModuleDestructuringPattern`) that mirrors
    `JsCompiler.EmitPatternFromStack` but routes each extracted name's final
    store through the **module binding store** (the same path
    `EmitModuleVarDecl` uses for the simple `Identifier` case — find how it
    stores a single binding and reuse it) instead of `StoreLocal`/`StoreGlobal`.
- Reuse the existing pattern-walking logic where you can; do not duplicate the
  array/object/default/rest handling if it can be shared. If sharing requires a
  small refactor of `JsCompiler.cs` to expose a store-callback seam, that's fine
  — keep it minimal and don't change function-scope behavior.

## Outputs
- `const`/`let`/`var` with array/object patterns (nested, with defaults and rest)
  works at module top level and produces correct live bindings, including when
  the bound names are re-exported (`export const { a } = obj`).

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/ModuleDestructuringTests.cs`, covering:
  - `const { a, b } = obj;` at module scope, values readable.
  - `const [x, , z] = arr;` with elision.
  - nested + default: `const { a: { b = 5 } = {} } = obj;`
  - rest: `const [head, ...tail] = arr;` and `const { x, ...rest } = obj;`
  - `export const { a, b } = obj;` — exported names resolve from another module
    (or via the module namespace) to the destructured values.
  - regression: simple `const name = …` module bindings still work.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green.

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- Put new tests in a NEW test file (avoid editing shared test files) to keep
  parallel work merge-clean.
- The live-binding subtlety the original `throw` warned about: destructuring
  must write to the module's upvalue **cells**, not shadowing locals. The
  single-identifier module path already does this correctly — match it.

## Handoff log
- 2026-05-21T01:01:13Z — created + claimed for agent-claude-cody-moddestruct (orchestrated Wave 1)
