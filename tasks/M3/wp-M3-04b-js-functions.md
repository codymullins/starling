---
id: "wp:M3-04b-js-functions"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T19:05:00Z"
branch: "wp-M3-04b-js-functions"
completed_at: "2026-05-11T19:20:00Z"
depends_on:
  - "wp:M3-04-js-vm"
blocks:
  - "wp:M3-05-js-intrinsics"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#vm"
---

# wp:M3-04b — User-defined functions

## Goal
Make `function foo(...) { ... }` declarations callable from JS. The
M3-04 slice landed top-level execution; this slice adds frame-based
call/return so recursion + program structure work.

## Outputs
- `src/Tessera.Js/Runtime/JsFunction.cs` — function value carrying a
  sub-chunk + parameter count.
- `src/Tessera.Js/Bytecode/JsCompiler.cs` — emit sub-chunks for
  `FunctionDeclaration`; hoist declarations so functions are callable
  before their textual position.
- `src/Tessera.Js/Runtime/JsVm.cs` — extend `Call` to push a new frame
  for a `JsFunction` callee; `Return`/`ReturnUndefined` pop the frame.

## Out of scope (queued)

- Closures / lexical capture (M3-04c).
- `new` with user-defined constructors (M3-04d).
- Arrow function `this` binding (M3-04c).
- `arguments` object (M3-04c).
- Default + rest parameters (M3-04c).

## Acceptance
- 10+ unit tests including: factorial via recursion, function with
  parameters + locals, function returning a value, nested function
  calls, function values stored in variables.

## Handoff log
- 2026-05-11T19:05Z — created and claimed atomically by agent-claude-cody.
