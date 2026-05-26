---
id: "wp:M3-79-js262-completion-values"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-79 — JS: statement completion values (what eval returns)

## Why (evidence)
A coherent ~110 cross-cutting cluster: an eval of a source string must return the
spec completion value of the last statement. Failures: `switch` 38, `for-of` 12,
`try` 10, `if` 8, `for-in` 8, `while`/`labeled`/`for`/`do-while`/`with` (~30).
Tests like `cptn-*`, "Non-empty value replaces previous non-empty value", "lone
case", "non-empy StatementList".

## Scope (§13.2.13 UpdateEmpty + per-statement completion)
The engine's eval returns a value but doesn't follow the completion-value rules.
Implement them so eval returns the right value:
- **Empty statements, declarations (var/let/const/function/class), `;`, and
  empty blocks produce an EMPTY completion** that does NOT replace the previous
  value (UpdateEmpty). E.g. source `1; ;` evaluates to 1; `1; var x` evaluates to 1.
- **Block / StatementList**: value of the last statement with a non-empty value.
- **`if` without `else`** whose test is false → undefined (UpdateEmpty).
- **switch**: the last non-empty case/default body value (the "replaces previous"
  / "lone case" tests).
- **Loops** (for/while/do-while/for-of/for-in): the body value of the last
  iteration; an abrupt/zero-iteration loop → undefined; UpdateEmpty per iteration.
- **try/catch/finally**, **labeled**, **with** completion per spec.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- The `cptn-*`/completion-value failures across switch/loops/if/try drop sharply;
  report before/after; regression-scan every category.
- Unit tests (use the engine's eval entry / CompileForEval): source `1; ;` → 1;
  `if(false) 1` → undefined; `switch(1){case 1: 5}` → 5;
  `for(var i=0;i<3;i++) i` → 2; `1; var x=2` → 1; `{ }` → undefined.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.
