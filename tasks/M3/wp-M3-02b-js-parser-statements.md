---
id: "wp:M3-02b-js-parser-statements"
parent: "wp:M3-02-js-parser"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T18:00:00Z"
branch: "wp-M3-02b-js-parser-statements"
depends_on:
  - "wp:M3-02a-js-parser-expressions"
blocks:
  - "wp:M3-02c-js-parser-classes-modules"
  - "wp:M3-03-js-compiler"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#parser"
---

# wp:M3-02b — JS parser: statements + program

## Goal
Bring the parser from expression-level to "can parse complete JS source
files". Adds Statement AST + parser methods + the top-level Program node.

## Outputs
- `src/Tessera.Js/Ast/Statements.cs` — Statement node hierarchy.
- `src/Tessera.Js/Ast/Program.cs` — Program / Module root.
- `src/Tessera.Js/Parse/JsParser.cs` — extend with statement parsing
  and `ParseProgram()` entry.

## Acceptance
- All these parse without error and produce expected AST shape:
  ```js
  var x = 1;
  let y = 2, z = 3;
  if (a) { b(); } else c();
  while (i < 10) i++;
  for (let i = 0; i < 10; i++) f(i);
  for (const k of arr) g(k);
  for (const k in obj) h(k);
  function add(a, b) { return a + b; }
  function* gen() { yield 1; }   // function decl with yield body
  try { throw new Err(); } catch (e) { log(e); } finally { cleanup(); }
  switch (x) { case 1: a(); break; default: b(); }
  do { i--; } while (i > 0);
  ```
- ASI: blank line between expression and `(...)` doesn't fold into
  a call.

## Out of scope
- Async functions (M3-02c).
- Class declarations (M3-02c).
- Module import/export (M3-02c).
- Destructuring patterns (M3-02d).

## Handoff log
- 2026-05-11T18:00Z — created and claimed atomically by agent-claude-cody.
