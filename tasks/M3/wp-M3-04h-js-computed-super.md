---
id: "wp:M3-04h-js-computed-super"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-compsuper"
claimed_at: "2026-05-21T02:01:39Z"
branch: "main"
depends_on:
  - "wp:M3-04c2-js-method-capture-cell"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#classes"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04h — JS: computed `super[expr]` member access

## Goal
Support computed super-property access/assignment: `super[expr]` (and
`super[expr] = v`) inside class methods/accessors, alongside the already-working
`super.name`. The computed-class-keys WP (`wp:M3-04f`) deliberately left this
throwing.

## Inputs / current shape
- Compiler throw to remove: `src/Starling.Js/Bytecode/JsCompiler.Classes.cs`
  rejects computed super (the `if (sp.Computed) throw new NotSupportedException(
  "computed super.[expr] is not supported in B1b-2a")` site, ~line 322).
- Current opcodes bake the name as a `[u16]` constant operand:
  `src/Starling.Js/Bytecode/Opcode.cs` `LoadSuperProperty` / `StoreSuperProperty`
  (~178–187). These can't express a runtime key.
- VM handlers for those opcodes live in `src/Starling.Js/Runtime/JsVm.cs`
  (super machinery — `LoadHomeObject`, `LoadSuperProperty`, `StoreSuperProperty`).
- The model: object computed access already works via `LoadComputed` /
  `StoreComputed` + `AbstractOperations.ToPropertyKey`. Super differs only in
  that the lookup starts at the home object's prototype with the correct
  receiver (`this`).

## Scope / approach
- Add stack-keyed super opcodes (e.g. `LoadSuperComputed`, `StoreSuperComputed`)
  in `Opcode.cs` that take the key from the stack (run it through
  `ToPropertyKey`) instead of a constant operand. Document stack effects in the
  existing comment style.
- Compiler: remove the throw; emit the key expression then the new opcode for
  `super[expr]` reads and writes (including compound assignment / update forms if
  the existing `super.name` path handles them — match it).
- VM: implement the new handlers — resolve via the home object's prototype with
  `this` as the receiver, mirroring the existing `LoadSuperProperty` /
  `StoreSuperProperty` semantics but with a runtime `JsPropertyKey`.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/Runtime/ComputedSuperTests.cs`:
  - `super[expr]()` method call resolves to the base-class method.
  - `super[k]` data-property read; `super[k] = v` writes onto `this` (per spec
    super-set semantics, receiver is `this`).
  - computed super with a Symbol key.
  - in a static method, `super[expr]` resolves against the base constructor.
  - regression: existing `super.name` tests stay green.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green.

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- New tests in a NEW file.
- `depends_on wp:M3-04c2` is a SOFT sequencing dep: both edit
  `JsCompiler.Classes.cs` + `JsVm.cs`, so this runs after the capture-cell fix
  lands to avoid a same-file conflict — not a logical dependency.

## Handoff log
- 2026-05-21T01:51:31Z — created as blocked (sequenced after wp:M3-04c2 to avoid JsCompiler.Classes.cs/JsVm.cs conflict)
- 2026-05-21T02:01:39Z — unblocked (wp:M3-04c2 complete + landed on main) + claimed for agent-claude-cody-compsuper
