---
id: "wp:M3-02-js-parser"
milestone: "M3"
status: "in_progress"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M3-01-js-lexer"
blocks:
  - "wp:M3-03-js-compiler"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#parser"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-02-js-parser"
---

# wp:M3-02 — JavaScript parser

## Goal
Parse ES2024 source into an AST consumable by the bytecode compiler
(wp:M3-03). The grammar is large; this package is decomposed into
sub-tasks so multiple agents can land slices in parallel.

## Decomposition

| Sub-task | Scope |
|---|---|
| wp:M3-02a-js-parser-expressions | AST types for expressions, parser scaffold, precedence-climbing for binary/unary/ternary/assignment ops, member access, calls, primary expressions. |
| wp:M3-02b-js-parser-statements  | Statement AST + parser: ExpressionStatement, BlockStatement, IfStatement, WhileStatement, ForStatement, VariableStatement (var/let/const), ReturnStatement, FunctionDeclaration. |
| wp:M3-02c-js-parser-classes-modules | ClassDeclaration, import/export, async/await/yield, generators. |
| wp:M3-02d-js-parser-destructuring | Array + object destructuring patterns in declarations and parameters. |
| wp:M3-02e-js-parser-test262 | Drive Test262 valid+invalid suites to ≥ 80% per plan acceptance. |

## Inputs
- wp:M3-01-js-lexer complete (JsTokenKind, JsToken, JsLexer).

## Acceptance
Sub-tasks have their own narrower acceptance criteria. The parent
package is complete when 02e drives Test262 to ≥ 80% on the valid +
invalid sets per the plan.

## Handoff log
- 2026-05-11T17:45Z — created and decomposed into 5 sub-tasks. The
  first (02a-expressions) is being claimed immediately.
