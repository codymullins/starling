---
id: "wp:M3-07-dynamic-script-src"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T00:00:00Z"
branch: "worktree-agent-a06f2fcbc1bc6e5b5"
completed_at: "2026-05-20T00:00:00Z"
depends_on:
  - "wp:M3-04-js-vm"
blocks: []
subsystem: "Starling.Engine"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md"
spec_refs:
  - "https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element (4.12.1 prepare a script)"
---

# wp:M3-07 — Dynamically-triggered `<script>` loading via setting `src`

## Goal
Make setting `src` on a `<script>` element (via `setAttribute('src', …)` or the
`.src` IDL property) run HTML §4.12.1 "prepare a script": fetch the external
resource, execute it, and fire `load` / `error` on the element. This is the
mechanism real deferred-bundle loaders (e.g. mcmaster.com) use — a loader runs
on `DOMContentLoaded` and copies a custom `data-*` attribute onto `src` for each
`<script>`, chaining the next bundle from the previous bundle's `load` handler.

## The bug
Starling fired `DOMContentLoaded` and ran the loader, but setting `src` on an
existing parser-created `<script>` did nothing — `ScriptFetcher` only collects
scripts present at parse time, so deferred bundles never fetched or ran. A
second, blocking bug: `window.addEventListener('DOMContentLoaded', …)` never
fired because the Window is a separate host `EventTarget` not in the DOM node
tree, and DOMContentLoaded was only dispatched on `document` (which bubbles via
the DOM ancestor chain, never reaching the Window). Real loaders register on
`window`, so this had to be fixed too.

Seed repro (`/tmp/deferred-repro.html`): rendered "FALLBACK_NOT_LOADED" (text
length 19). Target: "DEFERRED_RAN" (text length 12). After the fix the headless
`starling render` reports `text length=12`.

## What shipped
1. **`src`-set triggers fetch+execute.** A new realm-keyed seam
   (`Starling.Bindings/ScriptSrcHook.cs`) lets the engine subscribe to script
   `src` mutations. `NodeBindings.setAttribute` and a new `.src` IDL accessor
   detect a script-element `src` write and notify the hook. The engine's
   `DynamicScriptRunner` (`Starling.Engine/DynamicScriptRunner.cs`) tracks the
   HTML "already started" flag per element, queues newly-eligible scripts, and
   fetches+executes them on the shared realm. Parser-created empty `<script>`s
   become eligible exactly once.
2. **`load` / `error` events.** After a src-triggered fetch+execute settles the
   runner dispatches `load` (fetch+compile+run succeeded) or `error` (resolve /
   fetch / compile failure) on the element via the shared VM, so chained loaders
   run. A script whose body throws at runtime still fired *loaded*, matching
   browsers; only fetch/parse failures fire `error`.
3. **Pump waits on in-flight dynamic scripts.** `RunScriptsAsync` now drives a
   `PumpWithDynamicScriptsAsync` loop: settle microtasks/timers/rAF/fetch, then
   drain queued dynamic scripts, re-pumping after each (a `load` handler can
   queue the next bundle), until quiescent on all three fronts or an 8 s
   wall-clock cap. The fast path for pages with no pending dynamic work is
   untouched (one idle-out of the inner pump, then return).

`data:` script URLs decode locally (no network) via the existing
`Starling.Url.DataUrl`, so the core repro is offline-testable.

## Files changed
- `src/Starling.Bindings/ScriptSrcHook.cs` (new) — realm-keyed bindings↔engine seam.
- `src/Starling.Bindings/NodeBindings.cs` — script-`src` detection in `setAttribute` + new `.src` IDL accessor (scoped to script `src`).
- `src/Starling.Bindings/WindowBinding.cs` — fire `DOMContentLoaded` on the Window host target too.
- `src/Starling.Engine/DynamicScriptRunner.cs` (new) — prepare-a-script queue/fetch/execute/load-error.
- `src/Starling.Engine/ScriptFetcher.cs` — `data:` scheme + `FetchSourceAsync` exposed for the dynamic path (shared cache).
- `src/Starling.Engine/Engine.cs` — `RunScriptsAsync` wires the runner + hook + new pump loop.
- `tests/Starling.Engine.Tests/EngineJsExecutionTests.cs` — 6 new `[SpecFact]` tests + `BundleServer` helper.

## Tests
All in `EngineJsExecutionTests`, tagged `[Spec("html", …, "4.12.1 prepare a script")] [SpecFact]`:
- `Setting_src_on_deferred_script_fetches_and_runs_it` — the seed repro → "DEFERRED_RAN".
- `Setting_src_via_idl_property_fetches_and_runs_it` — `.src` IDL setter path.
- `Load_event_chains_a_second_dynamic_script` — load-event chaining (data:).
- `Error_event_fires_when_dynamic_script_fetch_fails` — fetch failure → `error`.
- `Sequential_network_bundles_chain_to_quiescence` — 3 HTTP bundles chained off `load` → "L:123" (quiescence).
- `Already_run_external_script_does_not_rerun_when_src_reassigned` — "already started" flag (`runs=1`).

Engine.Tests 118/118, Bindings.Tests 124/124, Dom.Tests 29/29 green.

## Deferred / not handled
- `type="module"` dynamic scripts (still classic-only).
- `async` / `defer` ordering distinctions (all post-parse, document order).
- CSP / `nonce` enforcement.
- Re-fetching when `src` is set a *second* time on a not-yet-started script
  (covered: re-setting on an already-started script is correctly a no-op).
- Live-collection semantics unchanged (`getElementsByTagName` is a snapshot).

## Handoff log
- 2026-05-20 — agent-claude-cody. Implemented + tested as above; worktree branch
  `worktree-agent-a06f2fcbc1bc6e5b5`. Note the worktree branched from a stale
  base (`9d3ec43`); orchestrator should re-test on the merged tree.
