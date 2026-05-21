---
id: "wp:M3-04j-js-static-block-class-name"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-21T02:51:14Z"
completed_at: "2026-05-21T02:51:14Z"
branch: "main"
depends_on: []
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#classes"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04j — JS: class name bound before static elements run

## Goal
Fix a pre-existing bug (surfaced during `wp:M3-04c2`): inside a `static {}` block
(or a static field initializer), the class name was `undefined` — e.g.
`class C { static { C.x = C.foo(); } }` threw "not a function: undefined". Per
§15.7.14 the class name binding is initialized to the constructor BEFORE static
elements evaluate.

## Cause
For a class declaration the compiler emitted `StoreGlobal <name>` AFTER
`BuildClass`, but `BuildClass`/`BuildClassRuntime` runs the static fields and
static blocks. So static elements ran before the name was bound.

## Fix
- `ClassTemplate` gains `bool BindNameToGlobal` (true only for class
  declarations; false for class expressions so a named class expression's inner
  name does not leak to the global).
- `JsCompiler.Classes.cs`: `EmitClassValue` takes `bindNameToGlobal`;
  `EmitClassDeclaration` passes true, `EmitClassExpression` leaves it false; the
  flag is forwarded into the `ClassTemplate`.
- `JsVm.cs` `BuildClassRuntime`: after the constructor + methods are built and
  BEFORE static fields/blocks run, if `BindNameToGlobal`, bind the constructor
  to its name on the global object.

## Acceptance
- `tests/Starling.Js.Tests/Runtime/StaticBlockClassNameTests.cs` (4 tests): static
  block references the class by name; static field initializer references the
  class; a later static block sees an earlier one's writes; a named class
  expression's name does NOT leak to the global. Full JS suite green (1232).

## Notes
- Done directly by the orchestrator (small, localized). Tested + committed on main.
- Consistent with the existing B1b simplification of binding class declarations
  to the global; revisit when block-scoped class bindings are implemented.

## Handoff log
- 2026-05-21T02:51Z — created + completed (agent-claude-cody). Surfaced by wp:M3-04c2.
