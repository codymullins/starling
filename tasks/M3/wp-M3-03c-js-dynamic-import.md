---
id: "wp:M3-03c-js-dynamic-import"
parent: "wp:M3-03-js-compiler"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-dynimport"
claimed_at: "2026-05-21T01:35:07Z"
completed_at: "2026-05-21T01:43:00Z"
branch: "main"
depends_on:
  - "wp:M3-03b-js-top-level-await"
blocks: []
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#modules"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03c — JS: dynamic `import()` + `import.meta`

## Goal
Support two ES module features:
1. **Dynamic import** — `import(specifier)` is a call-like expression that
   returns a Promise resolving to the imported module's namespace object. It
   works from modules AND classic scripts. A dynamically-imported module with
   top-level await resolves only after it settles.
2. **`import.meta`** — a meta-property available in module code; provide at least
   `import.meta.url` (the module's resolved URL).

## Inputs / current shape (TLA — wp:M3-03b — is landed)
- Reusable loader entry: **`internal JsPromise ModuleLoader.EvaluateToPromise(ModuleRecord)`**
  returns a Promise that settles when the module subtree finishes (already
  handles TLA). Build the dynamic-import resolution on top of it.
- `ModuleLoader` already has (private) `LoadGraph(specifier, referrer)` →
  `ModuleRecord`, `Link(record)`, and public `GetOrBuildNamespace(record)`. A
  dynamic import = LoadGraph + Link + EvaluateToPromise, then resolve the
  returned promise to `GetOrBuildNamespace(record)` (and reject on any throw,
  e.g. fetch/resolve failure or evaluation error).
- Static module parse/compile: `JsParser.Modules.cs`, `JsCompiler.Modules.cs`,
  AST in `Ast/Modules.cs`. Object/runtime: `JsVm.cs`, `JsRealm.cs`,
  `MicrotaskQueue`, `PromiseCtor`/`JsPromise`.
- `import` is currently a reserved word routed at statement level
  (`JsParser.Modules.cs`); you must add expression-context parsing for
  `import(` and `import.meta` WITHOUT breaking static `import …` declarations.

## Scope / approach
1. **Parser** (`JsParser.cs` / `JsParser.Modules.cs`): in primary-expression
   position, recognize:
   - `import` `(` AssignmentExpression [`,` … options ignored] `)` → dynamic
     import call. (A trailing options arg may be parsed and ignored.)
   - `import` `.` `meta` → meta-property.
   Keep `import`/`export` declaration parsing intact (only valid at module top
   level). `import(` and `import.meta` must parse as expressions anywhere an
   expression is allowed.
2. **AST** (`Ast/Modules.cs`): add `ImportCallExpression(Expression Specifier, …)`
   and `ImportMetaExpression(…)` (or equivalents).
3. **Compiler**: emit `import()` as a runtime call that hands the (string-coerced)
   specifier + the current module's referrer URL to the loader and pushes the
   resulting Promise. `import.meta` resolves to the current module's meta object.
   You'll likely need a new opcode (e.g. `DynamicImport`, `LoadImportMeta`) or a
   host-call shim — pick the approach that fits the VM; document it.
4. **Runtime/loader**:
   - Add an internal `JsPromise ImportDynamic(string specifier, string? referrer)`
     on `ModuleLoader` (LoadGraph+Link+EvaluateToPromise → `.then` namespace;
     catch resolve/fetch/eval errors into a rejected promise). The VM must be
     able to reach the active `ModuleLoader` (wire a reference via realm/VM if not
     already reachable).
   - `import.meta`: give `ModuleRecord` a lazily-built `Meta` `JsObject` with at
     least `url`. The module body needs access to its own meta — supply it the
     way module bindings are supplied (an upvalue/slot the loader fills at
     instantiation, or a record lookup keyed by the running module). Document the
     mechanism.
5. Specifier resolution uses the same host/resolver static imports use, relative
   to the importing module's URL (or the document base URL for classic scripts).

## Acceptance
- New tests in `tests/Starling.Js.Tests/DynamicImportTests.cs` (MapHost/`report()`
  harness), deterministic (drive the microtask queue):
  - `import('./m.js')` resolves to a namespace whose named + `default` exports
    read correctly.
  - dynamic import of a module with top-level await resolves only after it
    settles (assert ordering via a side-effect log).
  - `import.meta.url` returns the module's URL.
  - a failed specifier (missing module / resolve error) → rejected Promise
    (observable via `.catch`/`try`).
  - `import('x')` works from a classic (non-module) script too.
  - regression: static `import`/`export` declarations and existing module tests
    stay green.
- `dotnet build src/Starling.Js/Starling.Js.csproj -c Debug` green.
- `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj -c Debug` green
  (full suite, no regressions).

## Notes
- DO NOT touch any file under `tasks/` — the orchestrator owns task bookkeeping.
- New tests in a NEW test file; deterministic; no sleeps.
- Preserve all existing module + TLA + async behavior.

## Handoff log
- 2026-05-21T01:26:13Z — created as blocked (await spine, step 2 of 2)
- 2026-05-21T01:35:07Z — unblocked (wp:M3-03b complete) + claimed for agent-claude-cody-dynimport; reuse `ModuleLoader.EvaluateToPromise`
- 2026-05-21T01:43Z — COMPLETE (cherry-picked to main as `cfef6e4`). 2 new opcodes `DynamicImport`/`LoadImportMeta`; AST `ImportCallExpression`/`ImportMetaExpression`; parser disambiguates `import(`/`import.` (expression) from static `import …` (declaration) by peeking the next token. `ModuleLoader.ImportDynamic` = LoadGraph+Link+EvaluateToPromise → `.then` namespace (TLA-imported module resolves only after it settles); errors → rejected promise. `import.meta` resolved via `chunk.Name` (the resolved module URL) → `ModuleLoader.ResolveMetaForUrl` → lazily-built `ModuleRecord.Meta` (`url`); classic-script chunk has no registry entry → `import.meta` throws SyntaxError (spec-correct). VM reaches loader via new `JsRealm.ModuleLoader` (set in loader ctor). 14 tests in `DynamicImportTests.cs`; full JS suite 1172 green; downstream Bindings 136 + Engine 121 green.
  - **Note (not a bug):** `import('m')` synchronously runs the imported body up to its first top-level await, so the module's pre-await side effects log before the importer's next statement; the resolution `.then` still fires only after full settlement.
  - `import()` second arg (import attributes) parsed for forward-compat but discarded; `import.meta` exposes only `url` (per "at least url").
