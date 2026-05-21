---
id: "wp:M3-03e-js-switch-statement"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-switch"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T00:30:00Z"
branch: "main"
depends_on:
  - "wp:M3-03-js-compiler"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#bytecode-ir"
---

# wp:M3-03e — JS bytecode compiler: switch statement

## Goal

Add bytecode-compiler support for JavaScript `switch` statements
(ECMA-262 §14.12). Real sites (e.g. mcmaster.com) fail to boot because
their inline bootstrap script uses `switch (true) { case cond: ... }`.
The parser already produces a `SwitchStatement` AST node; the compiler
was throwing `NotSupportedException`.

## Outputs

- `src/Starling.Js/Bytecode/JsCompiler.cs` — `EmitSwitch` method;
  `LoopFrame.IsSwitch` flag so `continue` skips switch frames; labeled
  break/continue support in `EmitBreakOrContinue` + `EmitStatement`.
- `tests/Starling.Js.Tests/Runtime/JsSwitchTests.cs` — 8 spec tests.

## Acceptance

- Correct strict-equality (`===`) discriminant comparison.
- Discriminant evaluated once.
- Fall-through when no `break`.
- `default` clause works wherever it appears (including middle).
- `continue` inside a switch inside a `for` loop continues the `for`.
- `break` inside a switch breaks only the switch.
- Labeled `break label` out of a switch.
- `let`/`const` in switch body scoped to the whole switch with TDZ.
- `dotnet build && dotnet test` green.

## Handoff log

- 2026-05-20T00:00Z — created and implemented atomically by
  agent-claude-cody-switch. All 8 tests green; full sln green.
