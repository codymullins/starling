---
id: "wp:M3-04f-js-computed-class-keys"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-classkeys"
claimed_at: "2026-05-21T01:01:13Z"
branch: "main"
depends_on:
  - "wp:M3-02c-js-parser-classes-modules"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#classes"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04f — JS: computed class member keys

## Goal
Support computed keys on class members:
`class C { [expr]() {} ; static [a+b] = 1 ; get [k]() {} ; set [k](v) {} }`,
including `[Symbol.iterator]`. The parser + AST already carry `Computed` on
`MethodDefinition` and `PropertyField`; the compiler currently throws for them.
Optionally also lift the computed `super[expr]` blocker if cheap.

## Inputs
- Parser/AST already parse computed keys (`Computed=true`):
  `src/Starling.Js/Parse/JsParser.Classes.cs` `ParseClassElementKey` (~225–261);
  `src/Starling.Js/Ast/Expressions.cs` `MethodDefinition` (~227–235),
  `PropertyField` (~255–261).
- The working model to mirror is **object-literal computed keys**, which already
  work: `src/Starling.Js/Bytecode/JsCompiler.cs` `EmitObjectLiteral`
  (~1841–1881) emits `Opcode.StoreComputed`; runtime uses
  `AbstractOperations.ToPropertyKey` (`src/Starling.Js/Runtime/AbstractOperations.cs`).

## Scope / where to work
- `src/Starling.Js/Bytecode/JsCompiler.Classes.cs`:
  - Remove the throws: `EmitClassValue` (~72–73, computed method),
    `CompileFieldEntry` (~249–250, computed field). The static-key switches in
    `CompileMethodTemplate` (~215–222) and `CompileFieldEntry` (~260–266) must
    learn to defer computed keys to a runtime expression.
  - Emit the computed-key expression at the class-definition site (after
    `BuildClass`) and install the member with a runtime key — mirror the
    object-literal `StoreComputed` flow.
- `src/Starling.Js/Bytecode/ClassTemplate.cs`:
  - `MethodEntry` (~84–94) and `FieldEntry` (~106–111) only carry a static
    string key. Extend them to represent a computed key (e.g. a flag + a
    compiled key thunk/sub-chunk, or by installing computed members via new
    post-`BuildClass` opcodes — your call; keep it consistent with how method
    templates + upvalues are already threaded).
- `src/Starling.Js/Bytecode/Opcode.cs`: add the new opcode(s) you need (e.g.
  `DefineComputedMethod [u8 kind]`, `DefineComputedField`) after the existing
  class opcodes (~149–214). Document the stack effect in the same comment style.
- `src/Starling.Js/Runtime/JsVm.cs`:
  - Add handlers for the new opcode(s) near the class-opcode region (`BuildClass`
    ~1237–1284). Overload/extend `InstallMethodOrAccessor` (~1788–1811) to accept
    a runtime `JsPropertyKey` (string **or** symbol) rather than only a string;
    `BuildClassRuntime` (~1635–1786) should skip computed entries so they're
    installed by the new opcodes.
- Order of evaluation matters: computed keys evaluate in source order, after the
  `extends` clause and before/with member definitions per spec — keep methods
  before instance-field installation as the runtime already does.
- Optional (only if low-risk): `JsCompiler.Classes.cs` `super[expr]` throw
  (~322–323) — lift if it falls out naturally; otherwise leave it and note it.

## Outputs
- Computed method names, accessors, static + instance fields, and symbol keys
  (`[Symbol.iterator]`) all work on classes.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/ComputedClassKeysTests.cs`:
  - `const k='m'; class C { [k](){return 1} } new C().m()===1`
  - static computed method + computed field (instance and static).
  - getter/setter with computed key.
  - `[Symbol.iterator]` method usable via `for...of` or manual `[Symbol.iterator]()`.
  - key expression evaluated once, in source order (observable via a counter).
  - regression: existing static-named class members still work (run the existing
    class tests).
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green.

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- Put new tests in a NEW test file.
- You share `src/Starling.Js/Runtime/JsVm.cs` with a concurrent async-generators
  WP, but in a different region (class opcodes vs async/suspend machinery). Add
  your opcode handlers in the class-opcode region and your enum entries grouped
  with the other class opcodes to minimize merge friction.

## Handoff log
- 2026-05-21T01:01:13Z — created + claimed for agent-claude-cody-classkeys (orchestrated Wave 1)
