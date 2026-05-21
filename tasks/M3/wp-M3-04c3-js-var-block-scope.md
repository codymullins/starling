---
id: "wp:M3-04c3-js-var-block-scope"
parent: "wp:M3-04c-js-closures-snapshot"
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
  - "browser-plan/09_JS_ENGINE.md#closures"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04c3 — JS: `var` is function-scoped (block-declared var capture fix)

## Goal
Fix a pre-existing bug (surfaced during `wp:M3-04c2`): a `var` declared inside a
block got a block-local slot, which shadowed the function-top cell that capture
analysis reserves for a captured `var`. The initializer wrote the block-local
slot while a closure read the never-initialized cell → `undefined`/`NaN`. `var`
must be function-scoped (§14.3.2).

## Repro (was NaN; now 7)
```js
function f(){ { var total = 0; } let add = (n) => { total += n; }; add(3); add(4); return total; }
```

## Fix
`JsCompiler.cs`:
- `DeclarePatternBindings` gains a `bool functionScoped` param. For `var`
  (functionScoped), bindings are declared in the function-variable scope
  (`_scopes[0]`) and reuse an existing binding (e.g. a preallocated captured-var
  cell) instead of creating a shadowing block-local slot. `let`/`const`,
  catch params, and function params stay block-scoped (false). The flag is
  threaded through the pattern recursion.
- `EmitVarDecl` passes `functionScoped: vd.Kind == "var"`.

This also fixes the latent "var in a block is lost after the block" bug
(non-captured var in a block was previously block-local).

## Acceptance
- `tests/Starling.Js.Tests/Runtime/VarBlockScopeTests.cs` (5 tests): captured var
  in block mutated through a closure; var visible after its block; read-only
  capture; nested blocks share one binding; var-in-block inside a class method
  mutated through a closure. Full JS suite green (1232), no regressions.

## Notes
- Done directly by the orchestrator (small, localized). Tested + committed on main.
- Related: `wp:M3-04c2` fixed the class-member-body capture pipeline; this fixes
  the orthogonal var-in-block scoping issue.

## Handoff log
- 2026-05-21T02:51Z — created + completed (agent-claude-cody). Surfaced by wp:M3-04c2.
