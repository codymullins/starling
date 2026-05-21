---
id: "wp:M3-04c2-js-method-capture-cell"
parent: "wp:M3-04c-js-closures-snapshot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-capturecell"
claimed_at: "2026-05-21T01:51:31Z"
completed_at: "2026-05-21T02:01:39Z"
branch: "main"
depends_on: []
blocks:
  - "wp:M3-04h-js-computed-super"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#closures"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04c2 — JS: fix mutation-through-upvalue of a local declared in a class-method body

## Goal
Fix a real, pre-existing correctness bug: a local binding declared inside a
**class method (or accessor/constructor) body** that is **captured by a nested
closure and mutated** is not boxed into a shared `Cell`, so the nested closure
reads/writes the wrong storage and the VM throws
`InvalidOperationException: value is Number, not Object`. Plain (non-class)
functions handle this correctly; only class-method bodies are broken.

## Minimal repro (currently THROWS; should return 2)
```js
class C { m(){ let i = 0; let f = () => i++; f(); f(); return i; } }
new C().m();   // throws "value is Number, not Object"; expected 2
```
More observable shapes that all fail today (all reduce to the same cause):
```js
// object-method captures + mutates a class-method-body let
class C { m(){ let i=0; return { inc(){ return i++; } }; } }
let o = new C().m(); o.inc(); o.inc();   // throws; expected last call → 1

// the shape that surfaced this (iterator):
class C { [Symbol.iterator](){ let i=0; return { next(){ return { value:i, done:i++>=3 }; } }; } }
let s=0; for (const v of new C()) s+=v; s;   // throws; expected 6
```
NON-failing controls (must STAY green): plain-function equivalents; class-method
captures that are **read-only** (e.g. `m(){ let x=5; return { get(){ return x+x; } }; }`
returns 10 fine).

## Diagnosis (already done — start here)
- The capture/`Cell`-promotion analysis lives in
  `src/Starling.Js/Bytecode/JsCompiler.CaptureAnalysis.cs`. A local that is both
  **captured by a nested function AND assigned after capture** must be promoted
  to a `Cell` (mutable upvalue) instead of snapshot-copied. This works for
  regular function bodies.
- Class method/accessor/constructor bodies are compiled via the class-template
  path (`src/Starling.Js/Bytecode/JsCompiler.Classes.cs`, `CompileMethodTemplate`
  and the constructor/field compile helpers). That path does not appear to run
  the same mutated-capture → `Cell` promotion for the method body's own locals
  that `JsCompiler.cs` does for plain functions. When the nested closure then
  performs a cell op on what it expects to be a `Cell`, it finds a raw `Number`
  → `value is Number, not Object`.
- Likely fix: ensure a class member body is compiled through the same
  function-body pipeline (same capture analysis + `Cell` allocation +
  `InitCellLocal`/`LoadCellLocal`/`StoreCellLocal` emission) that plain functions
  use. Find where plain functions decide to box a local into a `Cell` and apply
  the identical treatment to method/accessor/constructor bodies. Confirm the
  root cause with the debugger/disassembler before patching; cite the spec/AO if
  relevant.

## Outputs
- Class-method-body locals captured + mutated by nested closures work
  identically to plain functions.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/Runtime/MethodCaptureCellTests.cs`:
  - the minimal repro returns 2.
  - object-method `inc()` mutating a captured class-method `let` increments.
  - the `[Symbol.iterator]` + `for…of` shape sums to 6.
  - read-only class-method capture still works (control).
  - getter/setter and constructor bodies with the same pattern work.
  - regression: existing closure + class tests stay green.
- Write the FAILING test first (watch it throw), then fix, then green — per
  AGENTS.md bug-fix workflow.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green
  (full suite, no regressions).

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- New tests in a NEW file.
- A sibling WP `wp:M3-04h-js-computed-super` also edits `JsCompiler.Classes.cs`
  and `JsVm.cs`; it is sequenced AFTER this one (it `depends_on` this) so you own
  those files for now.

## Handoff log
- 2026-05-21T01:51:31Z — created + claimed for agent-claude-cody-capturecell. Root cause localized to class-member body compile not running mutated-capture → Cell promotion (orchestrator diagnosis included above).
- 2026-05-21T02:01Z — COMPLETE (cherry-picked to main as `ea25fa3`). Confirmed root cause: class member bodies (`CompileMethodTemplate`/`CompileConstructorTemplate`/`CompileStaticBlockEntry`/`CompileFieldEntry`) skipped the three steps `EmitFunctionBody` runs for plain functions — `RunCaptureAnalysisForFunction` (before `BindFunctionParameters` so captured+mutated params promote via `PromoteParamCell`), `PreallocateCapturedVarBindings`, `HoistFunctionDeclarations`. Fix routes all four class-member paths through the identical pipeline (32 lines in `JsCompiler.Classes.cs`). 13 tests in `MethodCaptureCellTests.cs`; full JS suite 1196 green; downstream Bindings 136 + Engine 121 green.
  - **WP expectation corrected:** the iterator shape sums to **3**, not 6 (`done: i++>=3` reads value pre-increment → values 0,1,2; verified vs Node). The fix is proven (no longer throws; spec-correct value). My WP had the wrong expected number.
  - **Two pre-existing, out-of-scope bugs found + flagged (see INDEX follow-ups):** (1) class name not yet bound to global inside a static block (`C.x` undefined in `static { … }`; use `this`); (2) a captured+mutated `var` declared in an *inner block* mis-binds to a block-local slot (NaN) — reproduces in plain functions too, so it's a general `var`-in-block hoisting/capture interaction, not class-specific.
