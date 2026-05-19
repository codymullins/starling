---
id: "wp:M3-02d-js-parser-destructuring"
parent: "wp:M3-02-js-parser"
milestone: "M3"
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7-destruct"
claimed_at: "2026-05-19T21:14:48Z"
branch: "main"
depends_on:
  - "wp:M3-02b-js-parser-statements"
blocks:
  - "wp:M3-02e-js-parser-test262"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#parser"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-02-js-parser"
completed_at: "2026-05-19T21:20:50Z"
---

# wp:M3-02d — JS parser: destructuring patterns

## Goal
Parse ES2024 destructuring binding patterns in variable declarations,
function parameters, and assignment targets. Compiler/VM lowering is
out of scope; this WP delivers AST + parsing only.

## Scope
1. AST in `src/Starling.Js/Ast/` (a new `Patterns.cs` file is cleanest):
   - `BindingPattern` base.
   - `ArrayPattern(IReadOnlyList<ArrayPatternElement> Elements, …)` —
     elements include hole, identifier with optional default, nested
     pattern with optional default, and rest element (`...x` /
     `...{a}` / `...[a]`).
   - `ObjectPattern(IReadOnlyList<ObjectPatternProperty> Properties,
     RestElement? Rest, …)` — supports shorthand (`{a}`), renamed
     (`{a: b}`), computed keys (`{[k]: v}`), defaults (`{a = 1}`,
     `{a: b = 1}`), and rest (`{...rest}`).
   - `AssignmentPattern(BindingPattern Target, Expression Default, …)`.
2. Parser changes in `src/Starling.Js/Parse/JsParser.cs` /
   `JsParser.Statements.cs`:
   - `ParseBindingPattern()` recognising `[` and `{` at binding sites.
   - Wire into:
     - `var` / `let` / `const` declarators
     - Function & arrow function parameter lists
     - `for (const {a} of xs)` / `for (let [a] of xs)` heads
     - Catch clause parameter (`catch ({message})`)
   - Assignment-pattern reinterpretation: when an expression on the LHS
     of `=` turns out to be an `ArrayExpression` / `ObjectExpression`
     literal, convert it to the corresponding pattern (ESTree's
     "cover" trick). A helper `ReinterpretAsPattern(Expression)` is
     fine.
3. Tests in
   `tests/Starling.Js.Tests/Parse/JsParserDestructuringTests.cs`
   covering every form above plus negative cases (e.g. rest must be
   last in array; rest without default).

## Out of scope
- Bytecode lowering (separate WP).
- Default values that reference other bindings in the same pattern at
  runtime (parser only records the AST).

## Acceptance
- `dotnet build` clean; `dotnet test --filter Parse` green.
- New destructuring tests pass.
- No regression in existing parser tests.

## Handoff log
- 2026-05-19 — filed by agent-copilot-claude-opus-4.7 from the
  unfiled 02d sub-task in `wp:M3-02-js-parser`.
- 2026-05-19T21:14:48Z — claimed by agent-copilot-claude-opus-4.7-destruct, working on main
- 2026-05-19T21:20:50Z — merged; complete
