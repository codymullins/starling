---
id: "wp:M3-63-js262-dynamic-import-referrer"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "in_progress"
phase: "2"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-63 — JS: dynamic import() referrer must be the script/module base, not the immediate function chunk

## Why (evidence)
**239** `dynamic-import` failures: `TypeError: Failed to fetch module: …/tests/Starling.Js.Test262.Tests/bin…` — the relative specifier (`import('./module-code_FIXTURE.js')`) resolves against the PROJECT/cwd dir instead of the test file. `Opcode.DynamicImport` (`src/Starling.Js/Runtime/JsVm.cs:1590`) passes `chunk.Name` as the referrer to `loader.ImportDynamic(spec, chunk.Name)`. The test harness sets the TOP-LEVEL script chunk's Name to the test's absolute path — but these tests `await import(...)` inside an `async function`, whose **nested function chunk** has a different/empty Name, so the referrer (and thus the resolution base) is lost.

## Scope
- Make dynamic `import()`'s referrer the **active script/module's source path**, consistent across nested functions (real engines key it off the active script/module record, not the current function). Cleanest options: carry a `SourcePath` on `Chunk` (inherited by nested function chunks from the enclosing top-level chunk at compile time) and use it in the `DynamicImport` opcode; or track the current script/module base on the VM execution context. `import.meta.url` (the `MetaProperty`/`ResolveMetaForUrl` path at JsVm.cs:1599) should use the same base.
- Verify nested arrow/async/generator functions all see the enclosing script/module path.

## Acceptance
- Maintainer-run: `STARLING_TEST262_FILTER=dynamic-import` subset failures drop sharply (was 239) — report before/after.
- Focused unit tests: `import()` from inside an async function / nested arrow resolves a relative specifier against the script path (use a temp fixture pair, mirroring existing DynamicImportTests).
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: your worktree base may predate merged Phase 0-2 work; keep changes isolated; integrator cherry-picks + reconciles.
