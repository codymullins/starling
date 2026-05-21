---
id: "wp:M3-03d-js-async-module-cycles"
parent: "wp:M3-03-js-compiler"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-modcycles"
claimed_at: "2026-05-21T01:51:31Z"
branch: "main"
depends_on:
  - "wp:M3-03b-js-top-level-await"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#modules"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03d — JS: async-module cycle ordering (`[[CycleRoot]]` / `[[AsyncEvaluation]]`)

## Goal
Improve spec fidelity of async ES-module evaluation in the presence of cycles.
The top-level-await WP (`wp:M3-03b`) made acyclic async graphs correct but
explicitly did NOT model the spec's cyclic async-evaluation ordering: a cyclic
back-edge into a module that later turns out to be async does not currently
await that module's completion (it observes partially-published bindings). This
WP brings cyclic async graphs in line with §16.2.1.5.2–.3 as far as is
reasonable for this engine.

## Inputs / current shape
- `src/Starling.Js/Modules/ModuleLoader.cs` — `EvaluateToPromise(ModuleRecord)`,
  the private `JsPromise? Evaluate(ModuleRecord)` (null = fully synchronous
  subtree), the depth-first `Evaluating`/`Evaluated` guard, and async-dependency
  join logic added in `wp:M3-03b`.
- `src/Starling.Js/Modules/ModuleRecord.cs` — `Status`, `HasTopLevelAwait`,
  `EvaluationPromise`, `EvaluationError`.
- The spec's machinery to approximate: `[[AsyncEvaluation]]`,
  `[[PendingAsyncDependencies]]`, `[[CycleRoot]]`, `[[DFSIndex]]`/
  `[[DFSAncestorIndex]]` (the Tarjan-style SCC grouping that picks a cycle root
  and gathers a strongly-connected component's modules for joint async
  completion).

## Scope / approach (pragmatic)
- Implement enough of the async-module SCC handling that a cycle containing an
  async (TLA) module evaluates with correct ordering: members of a cycle that
  includes an async module should all complete (their `EvaluationPromise`s
  settle) before modules that depend on the cycle observe the cycle's exports.
- A back-edge into a not-yet-finished async module must participate in the
  pending-dependency count so the dependent does not run its body until the
  cycle's async work settles — rather than observing partial bindings.
- Preserve existing behavior for: acyclic async graphs (already correct),
  fully-synchronous graphs (no Promise round-trip), and synchronous cycles
  (current partial-binding semantics are spec-correct for non-async cycles).
- Full Tarjan `[[DFSIndex]]` fidelity is not strictly required if a simpler
  pending-async-dependency counter over the cycle achieves the observable
  ordering; document whatever simplification you keep and why.

## Outputs
- Async-module cycles evaluate in spec-aligned order; dependents wait for the
  cycle's async completion.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/AsyncModuleCycleTests.cs` (MapHost/`report()`
  harness, deterministic — drive the microtask queue):
  - a 2-module cycle a↔b where one uses top-level await: both settle before an
    importer of either observes exports (assert via a side-effect log).
  - a cycle where the async member's TLA result is read by the other member
    after settlement (ordering correct, no partial-binding read of the awaited
    value).
  - a self-importing async module (trivial cycle) terminates and settles.
  - top-level await rejection inside a cycle propagates as an evaluation error
    to all dependents.
  - regression: acyclic async graphs, synchronous graphs, and synchronous
    cycles all behave exactly as before (existing `JsModuleTests` +
    `TopLevelAwaitTests` stay green).
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green
  (full suite, no regressions).

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- New tests in a NEW file; deterministic; no sleeps.
- This WP is confined to `ModuleLoader.cs` / `ModuleRecord.cs` — disjoint from
  the concurrent class/VM follow-ups, so it can land in parallel.

## Handoff log
- 2026-05-21T01:51:31Z — created + claimed for agent-claude-cody-modcycles (parallel with wp:M3-04c2)
