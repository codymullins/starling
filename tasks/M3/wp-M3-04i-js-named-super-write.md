---
id: "wp:M3-04i-js-named-super-write"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-21T02:51:14Z"
completed_at: "2026-05-21T02:51:14Z"
branch: "main"
depends_on:
  - "wp:M3-04h-js-computed-super"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#classes"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04i — JS: named super-property write (`super.name = v`)

## Goal
Wire `super.name = v` and compound `super.name op= v`. The VM already had
`StoreSuperProperty` / `LoadSuperProperty` handlers (name as a u16 constant,
write targets `this` per §13.3.4), but the compiler threw
`NotSupportedException` for the non-computed super-write — only `super[k] = v`
(computed, `wp:M3-04h`) was emitted.

## Fix
`JsCompiler.cs` `EmitAssignment`: in the `SuperPropertyExpression` target branch,
handle the non-computed case by emitting the key as a constant operand —
`StoreSuperProperty` for `=`, and `LoadSuperProperty`→op→`StoreSuperProperty`
for compound forms — mirroring the computed `super[k]` path.

## Acceptance
- `tests/Starling.Js.Tests/Runtime/NamedSuperWriteTests.cs` (4 tests): named
  super-write invokes the base setter; writes to `this` when no setter; compound
  `super.x += v` reads the inherited getter then writes via the inherited setter;
  `super.name` read/call regression. Full JS suite green (1232).

## Notes
- Done directly by the orchestrator (small, localized). Tested + committed on main.
- Remaining adjacent gap: `obj.x++` / `super[k]++` update forms only support
  identifier targets (`EmitUpdate` limitation) — separate follow-up.

## Handoff log
- 2026-05-21T02:51Z — created + completed (agent-claude-cody). Surfaced by wp:M3-04h.
