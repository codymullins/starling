---
id: "wp:M3-02c-js-parser-classes-modules"
parent: "wp:M3-02-js-parser"
milestone: "M3"
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7-modules"
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
completed_at: "2026-05-19T21:21:59Z"
---

# wp:M3-02c — JS parser: ES modules (import / export)

## Goal
Add ES module syntax to the parser: `import` and `export` declarations.
Classes, `async` / `await` / `yield`, and generator function syntax are
**already implemented** by `JsParser.Classes.cs` and the async-arrow /
yield logic in `JsParser.cs`; do not rewrite them. The remaining gap in
this sub-task is purely modules.

## Scope (do this)
1. AST nodes in `src/Starling.Js/Ast/Statements.cs` (or a new
   `Modules.cs` file alongside):
   - `ImportDeclaration(IReadOnlyList<ImportSpecifier> Specifiers, string Source, …)`
   - `ImportSpecifier` variants: default import, namespace import
     (`* as x`), named import (`{ a, b as c }`), side-effect-only
     (`import "x"`).
   - `ExportDeclaration` variants:
     - `export <Declaration>` (var/let/const/function/class)
     - `export { a, b as c }` (named re-exports, optional `from "src"`)
     - `export default <expr-or-decl>`
     - `export * from "src"` and `export * as ns from "src"`
2. Parser methods in `src/Starling.Js/Parse/JsParser.cs` (or a new
   partial `JsParser.Modules.cs`):
   - `ParseImportDeclaration()` — consumes the `import` keyword token,
     parses the bindings + the `from "source"` string literal + ASI.
   - `ParseExportDeclaration()` — handles all `export` forms above.
   - Wire both into the top-level statement dispatch so they are only
     valid at Program scope (top-level), throwing a `JsParseException`
     otherwise.
3. Tests in `tests/Starling.Js.Tests/Parse/JsParserModuleTests.cs`
   covering each variant above (happy path + at least one negative test
   per form). Aim for ~25–40 focused tests.

## Out of scope
- Bytecode / VM support for modules (separate WP).
- Module loader / resolution.
- Dynamic `import()` expression (separate, parser-level only if trivial).

## Acceptance
- `dotnet build` clean; `dotnet test --filter Parse` green.
- All new module-syntax tests pass.
- No regressions in existing parser tests.

## Handoff log
- 2026-05-19 — filed by agent-copilot-claude-opus-4.7 after spotting
  that 02c was unfiled in `wp:M3-02-js-parser`. Reduced scope to
  modules only because classes/async/yield are already in the parser.
- 2026-05-19T21:14:48Z — claimed by agent-copilot-claude-opus-4.7-modules, working on main
- 2026-05-19T21:21:59Z — merged; complete
- 2026-05-19 — completed by agent-copilot-claude-opus-4.7-modules. Assumptions: module records are parser/AST metadata only (no loader/compiler semantics), `from`/`as` remain contextual identifiers, and static module declarations are accepted only through Program-scope dispatch. Validation: `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug --nologo --verbosity:quiet`; `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug --nologo --verbosity:quiet`.
