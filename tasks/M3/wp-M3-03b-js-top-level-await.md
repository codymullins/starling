---
id: "wp:M3-03b-js-top-level-await"
parent: "wp:M3-03-js-compiler"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-tla"
claimed_at: "2026-05-21T01:26:13Z"
completed_at: "2026-05-21T01:35:07Z"
branch: "main"
depends_on:
  - "wp:M3-02c-js-parser-classes-modules"
  - "wp:M3-03a-js-module-destructuring"
blocks:
  - "wp:M3-03c-js-dynamic-import"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#modules"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03b — JS: top-level await in ES modules

## Goal
Allow `await` at ES-module top level (outside any function). A module that uses
top-level await (or transitively depends on one) evaluates **asynchronously**:
its body suspends on `await` via the existing `Suspend(kind=1)` machinery, the
loader drives the microtask queue until the module's evaluation settles, and
importers observe the module's bindings only after it finishes. Today the
compiler does not lower top-level await and `ModuleLoader.Evaluate` is fully
synchronous (`vm.CallFunction(record.Instance!, …)`).

## Inputs / current shape
- Module compile: `src/Starling.Js/Bytecode/JsCompiler.Modules.cs`
  `CompileModule`/`EmitModule` (~44–86). The body becomes a `JsFunction`
  (`ModuleRecord.Instance`) the loader calls. The doc-comment there explicitly
  notes "Top-level await is not lowered here (the loader reports it)".
- Module eval: `src/Starling.Js/Modules/ModuleLoader.cs` `Evaluate` (~196–225) —
  depth-first, idempotent; calls `vm.CallFunction(record.Instance!, …)`.
  `LoadAndEvaluate` (~48–54) is the entry point.
- `src/Starling.Js/Modules/ModuleRecord.cs` — has `Status`, `Instance`,
  `EvaluationError`, `RequestedModules`. Add async-evaluation state here.
- Async-function machinery to REUSE (do not reinvent): `JsVm.StartAsyncBody`
  / `DriveAsync` / `ScheduleAwait` / `SettleAsync`, `JsFunctionKind.Async`,
  `MicrotaskQueue` (`DrainAll`), `PromiseCtor`/`JsPromise`. An async function
  body already suspends on `await` and returns a Promise — a TLA module body is
  essentially an async function body.

## Scope / approach (pragmatic, spec-aligned)
1. **Compiler — detect + mark async.** In `EmitModule`, scan the module body for
   a top-level `AwaitExpression` (NOT nested inside a non-async
   function/method/arrow; `for await` and `await` inside async functions are
   already fine and are NOT top-level). Thread a `bool HasTopLevelAwait` through
   `ModuleCompilation` → `ModuleRecord`. When set, the module body `JsFunction`
   must be created with `JsFunctionKind.Async` so the VM's `CallFunction`
   dispatches it through `StartAsyncBody` and it returns a Promise. Non-TLA
   modules keep `Normal` kind and the existing synchronous fast path —
   no behavior change for them.
2. **Loader — async-aware evaluation.** Make `Evaluate` propagate an
   awaitable completion. A module must finish evaluating its async dependencies
   **before** running its own body (spec: a module with async deps waits on
   them). Pragmatic shape: evaluate deps depth-first; if a dep evaluated to a
   pending Promise (async module), the dependent's evaluation must await it
   before/within running its body. Calling an `Async`-kind body returns a
   Promise; capture it as the module's evaluation promise. Rejections become
   `ModuleRecord.EvaluationError` and reject the chain.
3. **Driver.** At the `LoadAndEvaluate` boundary, after kicking off evaluation,
   drive `MicrotaskQueue.DrainAll()` (loop until quiescent) so the root
   module's evaluation promise actually settles synchronously from the host's
   perspective, then rethrow `EvaluationError` if set. Keep the existing
   synchronous contract for non-TLA graphs.
4. Cycles: preserve the existing `Evaluating` guard semantics. A cycle through
   an async module should not deadlock — match the spec's behavior as closely
   as is reasonable; document any simplification in the handoff log.

## Outputs
- `await` works at module top level; TLA modules and their importers evaluate
  in the correct order; a rejected top-level await surfaces as an evaluation
  error.

## Acceptance
- New tests in a dedicated file, e.g.
  `tests/Starling.Js.Tests/TopLevelAwaitTests.cs` (follow the `MapHost`/
  `report()` harness used by the existing `JsModuleTests`):
  - top-level `await Promise.resolve(v)` binds the resolved value; a later
    statement in the same module sees it.
  - an importer of a TLA module observes the module's exports only **after** the
    TLA settles (assert ordering via a shared side-effect log).
  - a module that `import`s an async (TLA) dependency waits for the dependency's
    top-level await to complete before running its own body.
  - top-level `await Promise.reject(e)` (or throw after await) surfaces as a
    module evaluation error to `LoadAndEvaluate`.
  - regression: a module with NO top-level await still evaluates synchronously
    (no Promise required) — existing `JsModuleTests` stay green.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green
  (full suite, no regressions).

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- Put new tests in a NEW test file.
- Tests must be DETERMINISTIC — drive the microtask queue the way existing
  Promise/async tests do; no wall-clock sleeps.
- This WP blocks `wp:M3-03c-js-dynamic-import`, which builds on the async-eval
  machinery you add to `ModuleLoader`/`ModuleRecord`. Leave the loader's
  async-evaluation entry points clean and reusable (a public/internal "evaluate
  to a Promise" path that `import()` can call) and note their names in the
  handoff log.

## Handoff log
- 2026-05-21T01:26:13Z — created + claimed for agent-claude-cody-tla (await spine, step 1 of 2)
- 2026-05-21T01:35Z — COMPLETE (cherry-picked to main as `11904a0`). AST-based top-level-await detection in `EmitModule` → `ModuleCompilation.HasTopLevelAwait` → `ModuleRecord.HasTopLevelAwait`; module body `JsFunction.Kind` switches to `Async` only when set (non-TLA modules keep the synchronous fast path — `Evaluate` returns null → plain `CallFunction`). Loader reworked: `internal JsPromise EvaluateToPromise(ModuleRecord)` (never null; sync graphs → already-settled promise), private `JsPromise? Evaluate(record)` (null = fully sync), async-dependency join, `ModuleRecord.EvaluationPromise`. 12 tests in `TopLevelAwaitTests.cs`; full JS suite 1158 green; Engine module integration 5 green.
  - **Reusable for wp:M3-03c:** `ModuleLoader.EvaluateToPromise(ModuleRecord)`.
  - **Documented simplification:** a cyclic back-edge into an async module does not await that module's completion (we only learn a module is async after evaluating its deps); it observes partially-published bindings, matching existing synchronous cycle semantics. Full `[[CycleRoot]]`/`[[AsyncEvaluation]]` ordering is not modeled; acyclic async graphs are correct.
