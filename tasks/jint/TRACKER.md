# Jint alternative JS engine — work tracker

Live tracker for adding **Jint** (pure-managed C# JS interpreter) as a
runtime-selectable alternative to the from-scratch `Starling.Js` engine.

**Decisions (2026-05-22):** Jint is a *temporary compatibility crutch* (run real
pages now while `Starling.Js` climbs toward 80% test262; keep cleanly
removable) · *full Web-API binding parity* · *narrow engine seam + parallel
bindings* (no rewrite of the ~956 existing `Starling.Bindings` call sites).

Design / shared contract: [`DESIGN.md`](DESIGN.md).

| Status legend |
|---|
| 🟢 `complete` · 🟡 `in_progress` · 🔵 `available` · ⚫ `blocked` |

## Waves & packages

Waves are dependency-ordered. Everything in a wave can run in parallel; a wave
starts only after the previous wave builds green.

### Wave 1 — foundation (single agent, establishes all shared contracts)

| ID | Status | Subsystem | Summary |
|---|---|---|---|
| J0 | 🟢 | build | Added `Jint` 4.9.2 (pure-managed); scaffolded `Starling.Js.Hosting`, `Starling.Bindings.Jint`; slnx wired; spike test proves Jint reads a real `Starling.Dom` node via `Wrap`+`DefineAccessor`. |
| J1 | 🟢 | Starling.Engine | `IScriptEngineFactory`/`IScriptSession` seam + `JsEngineSelector` (`STARLING_JS_ENGINE`, default `starling`, loud-fail); `StarlingScriptSession` over today's `JsRuntime` path; `Engine.cs` refactored to the seam; old `DynamicScriptRunner.cs`/`EngineModuleHost.cs` moved into the backend. Js/Engine/Bindings tests green with the var unset. |
| J2a | 🟢 | Starling.Bindings.Jint | `JintBackendContext`, `JintDomWrapper` (identity map + prototype slots), `JintInterop.DefineMethod/DefineAccessor/DefineDataProp` over Jint `ClrFunction`/`PropertyDescriptor`/`GetSetPropertyDescriptor`, `JavaScriptException`→`ScriptThrow`, `JintScriptSession` (+ working console), `JintBindings.InstallAll` with **stub installer files for every Wave-2 family**. |

### Wave 2 — Jint bindings (parallel; each agent owns ONE installer file)

| ID | Status | Subsystem | Summary | Mirrors | Edit only |
|---|---|---|---|---|---|
| J2b | 🟢 | Starling.Bindings.Jint | Node / Element / Document | `NodeBindings.cs`, `DomWrappers.cs`, `QuerySelectorEngine.cs` | `NodeBindings.cs` |
| J2c | 🟢 | Starling.Bindings.Jint | EventTarget / Event dispatch | `EventTargetBinding.cs` | `EventTargetBinding.cs` |
| J2d | 🟢 | Starling.Bindings.Jint | Window/global, Storage, History, Performance | `WindowBinding.cs`, `StorageBinding.cs`, `HistoryBinding.cs`, `PerformanceBinding.cs` | `WindowBinding.cs` + `StorageBinding.cs`/`HistoryBinding.cs`/`PerformanceBinding.cs` |
| J3a | 🟢 | Starling.Bindings.Jint | Timers, rAF, event-loop pump | `TimersBinding.cs`, `AnimationFrameBinding.cs` | `TimersBinding.cs`/`AnimationFrameBinding.cs` (+ a dynamic-script runner; see note) |
| J3b | 🟢 | Starling.Bindings.Jint | fetch | `FetchBinding.cs` | `FetchBinding.cs` |
| J3c | 🟢 | Starling.Bindings.Jint | XMLHttpRequest | `XhrBinding.cs` | `XhrBinding.cs` |
| J3d | 🟢 | Starling.Bindings.Jint | Observers, crypto, cookies | `Observers/`, `CryptoBinding.cs`, `CookieBinding.cs` | `ObserversBinding.cs`/`CryptoBinding.cs`/`CookieBinding.cs` |
| J4 | 🟢 | Starling.Bindings.Jint | ES modules (loader, TLA, dynamic `import()`) | module path in `Engine.cs` | `ModuleLoader.cs` (+ `JintScriptSession.RunModuleScriptAsync`) |

### Wave 3 — integration, CI, conformance, docs

| ID | Status | Subsystem | Summary |
|---|---|---|---|
| J6a | 🟢 | Starling.Bindings.Jint | Wire element geometry (`getBoundingClientRect`/`getClientRects`/`offset*`/`client*`/`scroll[WH]`) + `getComputedStyle` through `ctx.LayoutHost` (cast to `ILayoutHost`) — parity with the Starling backend; geometry reads now trigger the engine's lazy pre-script layout. |
| J6b | 🟢 | Starling.Bindings.Jint | Run `<script>` whose `src` is set from JS (dynamic/deferred loader). Added `ctx.OnScriptSrcSet` hook (Jint analogue of `ScriptSrcHook`); `NodeBindings` `setAttribute('src',…)` + new `.src`/`.async` IDL props notify it; session routes into `JintDynamicScriptRunner.OnSrcSet` (fetch + run + fire load/error, honouring the already-started flag). |
| J7 | 🟢 | Starling.Js.Hosting | Cleanup: moved `ILayoutHost`/`LayoutRect`/`OffsetMetrics` from `Starling.Bindings` into the engine-neutral `Starling.Js.Hosting` seam (kept `Starling.Bindings` namespace → no consumer churn); `ScriptSessionOptions.LayoutHost` + `JintBackendContext.LayoutHost` now strongly typed `ILayoutHost?` (casts gone). **Dropped the J6a `Starling.Bindings` project ref from `Starling.Bindings.Jint`**, so the Jint backend no longer pulls `Starling.Js` transitively — "deletable in one step, no `Starling.Js`" restored. |
| J5a | ⚫ | CI | E2E + Jint binding tests under `STARLING_JS_ENGINE=jint`; netclaw.dev golden render under Jint. |
| J5b | ⚫ | tests | Run test262 harness against Jint as a compat-delta baseline (informational). |
| J5c | ⚫ | docs | README env-var note + `browser-plan/09_JS_ENGINE.md` section + removal checklist. |

## Acceptance (overall)
- `STARLING_JS_ENGINE` unset → byte-for-byte today's behavior (Starling.Js).
- `STARLING_JS_ENGINE=jint` → netclaw.dev renders with zero JS-engine errors.
- Both engines green in CI. Removing Jint = delete `Starling.Bindings.Jint` +
  `Jint` package + one selector arm; `Starling.Js.Hosting` seam stays.

## Handoff log
- 2026-05-22 — tracker + DESIGN created (agent-claude-cody); baseline build green; Wave 1 launching.
- 2026-05-22 — **Wave 1 complete (J0+J1+J2a)** (agent-claude-cody). Full solution
  builds green (`dotnet build Starling.slnx -c Debug`); tests green:
  Starling.Js (1439 pass / 1 skip), Starling.Engine (151), Starling.Bindings
  (204), new Starling.Bindings.Jint (2 spike). With `STARLING_JS_ENGINE` unset,
  behavior is unchanged (default = Starling.Js backend).
- 2026-05-22 — **Wave 2A complete (J2b, J2c, J2d, J3b, J3c, J3d)** (agent-claude-cody,
  orchestrated). Worktree isolation misfired (agents branched from `main`, pre-Wave-1),
  so the fan-out was messy: J2b/J2c/J3c committed straight to `jint-js-engine`;
  J3b landed in a side worktree (cherry-picked as `78b7723`); J2d/J3d work was
  recovered from the working tree and committed as `559de55` (with two necessary
  infra additions: `JintDomWrapper.BindExisting` + window lifecycle-event routing
  in `JintScriptSession`). **All 6 families build + pass together: 56 Jint backend
  tests green; default Engine path unchanged (151 green).** Lesson: run remaining
  waves sequentially in the main tree, not via worktree isolation.
- Remaining: J3a (timers/rAF + a real pump hook — fetch/XHR flagged that
  `PumpOnce` ignores cross-thread completions + `Loop.PendingMicrotaskCount`),
  J4 (modules), then Wave 3 (CI/test262/docs + end-to-end render under Jint).
  - New projects: `src/Starling.Js.Hosting` (seam — refs Dom/Net/Common/Url only),
    `src/Starling.Bindings.Jint` (Jint backend), `tests/Starling.Bindings.Jint.Tests`.
  - Seam lives in `Starling.Js.Hosting`; selector `JsEngineSelector` lives in
    `Starling.Engine` (so Hosting never references a backend → no cycle).
  - **Jint is pure-managed**: 4.9.2 → only dep is Acornima (managed parser) →
    only deps are in-box `System.Memory`/`System.Runtime.CompilerServices.Unsafe`.
    No `runtimes/` native assets, ships `net10.0`. Managed-first interop policy holds
    like BouncyCastle. CI interop-seam grep stays satisfied.
  - **Contract deviations (all written into DESIGN.md):** hosting-local
    `ConsoleLevel`; `ScriptSessionOptions.LayoutHost` is `object?`; `Http` is
    `StarlingHttpClient`; added `IScriptSession.MarkScriptStarted(Node)`;
    `JintInterop` helpers are static and take `Jint.Engine` first;
    `ClrFunction` (not `ClrFunctionInstance`) in Jint 4.x.
  - **Wave-2 agents:** edit ONLY your one stub file (see "Edit only" column).
    Set your prototype slot on `ctx.Wrappers.*Prototype`, then `Wrap`/`GetOrCreate`.
    `JintBindings.InstallAll` order is frozen — don't touch it.
    - J3a note: the Jint backend has NO dynamic `<script src>` runner yet
      (`OnScriptElementConnected` runs inline injected scripts only;
      `MarkScriptStarted` is a no-op). J3a should add one (mirror
      `Starling.Bindings/Backend/StarlingDynamicScriptRunner.cs`) and wire
      `PumpOnce`/`OnScriptElementConnected`/`MarkScriptStarted` to it.
    - J4 note: `JintScriptSession.RunModuleScriptAsync` currently throws
      `ScriptThrow("…not yet implemented (J4)")`; replace it via `ModuleLoader.cs`
      using `ctx.Engine.Modules` + `ctx.Fetch`.
- 2026-05-22 — J2d + J3d picked up (agent-claude-coordinator) alongside in-flight J2b/J2c/J3b/J3c worktrees.
- 2026-05-22 — **J3a complete** (agent-claude-cody, main tree). Implemented:
  - `TimersBinding.cs` — `setTimeout`/`setInterval`/`clearTimeout`/`clearInterval`
    /`setImmediate`/`clearImmediate` over `ctx.Loop` (extra args forwarded,
    stable integer ids, interval reschedule-chain map, microtask drain after each
    callback, errors → `ctx.Diag`).
  - `AnimationFrameBinding.cs` — `requestAnimationFrame`/`cancelAnimationFrame`
    over `ctx.Loop`'s frame mechanism (callback gets a `DOMHighResTimeStamp`).
  - **Additive contract:** `JintBackendContext.Post` (thread-safe post-to-JS-thread
    hook) + default queue with `DrainPosted()`/`HasPosted` for bare contexts.
    Documented in DESIGN.md (J2a `JintBackendContext` bullet + Event-loop section).
  - `JintScriptSession.PumpOnce` rewritten: runs promise jobs, drains the
    cross-thread post queue on the JS thread, advances `ctx.Loop`, and reports
    not-idle while any of {timers, rAF, loop microtasks, post queue, in-flight
    dynamic-script fetch} is pending. `OnScriptElementConnected`/`MarkScriptStarted`
    wired to the new runner.
  - `JintDynamicScriptRunner.cs` (new) — dynamic `<script src>` runner mirroring
    `StarlingDynamicScriptRunner`: fetches via `ctx.Fetch` on a background task,
    runs on the JS thread via `ctx.Post` + the session's `RunClassicScript`, fires
    `load`/`error`, honours the "already started" flag.
  - **Refactored fetch + XHR** to use `ctx.Post` instead of their 0ms self-re-arming
    timer / poll trampolines (FetchBinding dropped `_completions`/`ArmPump`/`PumpTick`;
    XhrBinding dropped `XhrState.Pending`/`ArmPoll`). Cleaner and still green.
  - Tests: new `TimersAnimationPumpTests.cs` (11 tests). **All Jint backend tests
    green: 67 (56 prior + 11 new).** Default path unchanged: Starling.Engine 151
    green. Backend build clean (0 warnings, warnings-as-errors).
  - **Remaining gap for J4/Wave-3:** the Jint `NodeBindings.setAttribute` / `.src`
    IDL setter does NOT notify the dynamic runner when JS sets `src` on an existing
    not-yet-started `<script>` (the "loader copies data-src → src on
    DOMContentLoaded" pattern). The Starling backend handles this via
    `ScriptSrcHook` registered on the realm; the Jint `NodeBindings` has no
    equivalent hook and editing it was out of J3a's "edit only these files" scope.
    The runner is fully wired for the script-inserted/connected external path
    (`OnScriptElementConnected`) and the "already started" flag
    (`MarkScriptStarted`). A small follow-up should add a src-set notification in
    `NodeBindings` that calls into the session's runner.
- 2026-05-22 — **J4 complete** (agent-claude-cody, main tree). ES modules: loader,
  top-level await, dynamic `import()`. Implemented:
  - `StarlingJintModuleLoader : Jint.Runtime.Modules.IModuleLoader` (in
    `ModuleLoader.cs`) — `Resolve` resolves specifiers via `Starling.Url`
    (`UrlParser.Parse(specifier, base)`) against the importing module's URL (or the
    document `BaseUrl` for the entry / inline modules); `LoadModule` returns primed
    entry/inline bodies, else blocks on `ctx.Fetch` (synchronous IModuleLoader
    contract, mirrors the Starling backend's `StarlingModuleHost.FetchSource`).
    Handles `data:`/`http(s)`/relative specifiers; a fetch miss throws
    `ModuleResolutionException`. **The resolved `Key` is the module's full URL and
    the `ResolvedSpecifier.Uri` is left null** — that makes Jint report the Key
    (not `Uri.LocalPath`) as `Module.Location`, which drives `import.meta.url`.
  - `StarlingJintModuleMetaHost : Jint.Runtime.Host` — overrides
    `GetImportMetaProperties` to set `import.meta.url` from `Module.Location`
    (Jint 4.9.2's default `import.meta` is an empty object).
  - **Engine construction restructured:** module support must be enabled at
    construction. `JintScriptSession` now builds the loader first, then constructs
    the engine with `opts.EnableModules(loader)` + `opts.UseHostFactory(_ => meta)`.
    `ModuleLoader.Install` stays a no-op (the `JintBindings.InstallAll` order is
    untouched) since modules can't be wired onto a live engine.
  - `JintScriptSession.RunModuleScriptAsync` — registers the entry source
    (inline `<script type=module>` → synthetic `about:inline-N` whose imports
    resolve against the doc base; external → primed under its src URL), evaluates
    via `engine.Modules.Import(specifier)` (which links + drives TLA to
    settlement), then drains `ProcessTasks`. `JavaScriptException` /
    `ModuleResolutionException` → `ScriptThrow`.
  - **Exact Jint module API used:** `options.EnableModules(IModuleLoader)` +
    `options.UseHostFactory(Func<Engine,Host>)`; `IModuleLoader.{Resolve,LoadModule}`;
    `ResolvedSpecifier(ModuleRequest, Key, Uri, SpecifierType)`;
    `ModuleFactory.BuildSourceTextModule(engine, resolved, code, ModuleParsingOptions.Default)`;
    `engine.Modules.Add/Import`; `Module.Location`; `ModuleResolutionException`.
    Dynamic `import()` wires automatically through the same loader once
    `EnableModules` is set (verified).
  - Tests: new `ModuleBindingsTests.cs` (8 tests): named+default import from a
    fetched dependency, top-level await, `import.meta.url`, dynamic `import()`
    namespace, inline-module relative import vs doc base, missing-dep → ScriptThrow,
    throwing module → ScriptThrow, re-export chain. **All Jint backend tests green:
    75 (67 prior + 8 new).** Default path unchanged: Starling.Engine 151 green.
    Smoke-checked `EngineModuleScriptTests` (4) under `STARLING_JS_ENGINE=jint` —
    real engine module orchestration (external `<script type=module>` via the
    shared ScriptFetcher cache) passes end-to-end. Backend build clean
    (0 warnings, warnings-as-errors).
  - **Wave-3 gaps:** no import-map support (bare specifiers resolve as plain
    relative URLs against the base, not via a map); JSON/CSS module attributes
    (`import ... with { type: 'json' }`) are not specially handled — only source-text
    modules are built; the loader's HTTP fetch blocks synchronously (acceptable —
    matches the Starling backend, and parser-discovered modules hit the warm
    ScriptFetcher cache), but a deeply chained cold-fetch graph blocks the JS thread
    per dependency.
- 2026-05-22 — **J6a complete** (agent-claude-cody, main tree). Wired element
  geometry + `getComputedStyle` to the layout host (parity with the Starling
  backend); geometry reads now drive the engine's lazy pre-script layout.
  Implemented:
  - `NodeBindings.cs` — `getBoundingClientRect`/`getClientRects` route through
    `LayoutHost(ctx).TryGetBoundingClientRect`; `offsetWidth/Height`,
    `offsetTop/Left`, `clientWidth/Height`, `scrollWidth/Height` route through
    `ReadOffsetMetric` → `TryGetOffsetMetrics` (scrollW/H mirror border-box per
    the Starling backend). `getBoundingClientRect` now returns a real DOMRect-
    shaped object (`x/y/width/height/top/right/bottom/left` + `toJSON`).
    `scrollTop/Left` stay 0 no-ops (no scrolling wired through this surface yet).
    Host obtained via `ctx.LayoutHost as ILayoutHost`; **null host (bare
    unit-test contexts) keeps the zero/empty fallback** so the 75 binding unit
    tests still pass.
  - `WindowBinding.cs` — `getComputedStyle(el)` returns a CSSStyleDeclaration-
    shaped object whose `getPropertyValue(name)` + camel/kebab accessors
    (`CommonComputedStyleProps`, mirroring the Starling backend) resolve via
    `host.GetComputedProperty(el, name)`; no-op `setProperty`/`removeProperty`.
  - **csproj:** added `ProjectReference` to `Starling.Bindings` so the cast to
    `Starling.Bindings.ILayoutHost` compiles (the seam types `LayoutHost` as
    `object?`; the engine passes a `BoxLayoutHost : ILayoutHost`). No dependency
    cycle (nothing in Bindings/Js/Dom references this assembly).
  - **FLAG:** `Starling.Bindings` transitively references `Starling.Js`, so this
    pulls the Starling JS engine into the Jint assembly — slightly diluting the
    "deletable in one step, no Starling.Js" goal noted in the csproj. There is no
    standalone layout-host contract assembly to reference instead; if that goal
    must hold, extract `ILayoutHost`/`LayoutRect`/`OffsetMetrics` into a tiny
    contract project (Dom-only) and have both `Starling.Bindings` and this
    assembly reference it.
  - Tests: **10/11 listed geometry/layout target tests now pass under
    `STARLING_JS_ENGINE=jint`** (getBoundingClientRect ×2, getComputedStyle,
    offsetTop sibling, offsetWidth/Height, Prelayout_runs, Script_reads_geometry
    ×3, Reused_render_matches). The 11th —
    `Progressive_layout_first_paints_then_reflows_after_deferred_dom_change` — is
    gated on the **dynamic/injected async `<script src>` path (J6b's domain)**, not
    geometry: its deferred external script never fetches under Jint, so the DOM is
    never mutated and no successor page is produced. It fails alongside the other
    6 J6b dynamic-script tests (`Setting_src_*`, `Injected_async_external_script_*`,
    `Load_event_chains_*`, `Sequential_network_bundles_*`,
    `Error_event_fires_when_dynamic_script_fetch_fails`). Jint engine suite:
    **144/151 pass** (the 7 failures are exactly the J6b dynamic-script set).
    Default path unchanged: **Starling.Engine 151 green**. Jint backend unit
    tests: **75 green**. Backend build clean (0 warnings, warnings-as-errors).
  - **Remaining geometry gap:** `getClientRects` returns a single rect (block-box
    simplification, matches the Starling backend — multi-line-box inline flows
    need the box-tree walk); `scrollTop/Left` and `scroll[WH]` overflow are not
    yet wired (no scrolling surface); offset-parent is approximated as the
    document origin (same as `BoxLayoutHost`).
- 2026-05-22 — **J6b complete** (agent-claude-cody, main tree). Made the Jint
  backend run a `<script>` whose `src` is set from JS (dynamic/deferred loader),
  achieving parity with the Starling backend. **Root cause (flagged by J3a):** the
  J3a `JintDynamicScriptRunner` already had a working `OnSrcSet` entry, but the
  Jint `NodeBindings` never notified it when JS (re)assigned `src` on an existing
  not-yet-started `<script>`. Wired the missing notification path (additive only):
  - `JintBackendContext.cs` — added `Action<Element>? OnScriptSrcSet` (the Jint
    analogue of the Starling backend's realm-keyed `ScriptSrcHook`; bindings
    observe the mutation, session owns the fetch+run pipeline). Documented in
    `DESIGN.md` next to the J3a `Post` contract.
  - `JintScriptSession.cs` — installs `_ctx.OnScriptSrcSet = _dynamicRunner.OnSrcSet`
    at construction (right after building the dynamic runner, before
    `JintBindings.InstallAll`).
  - `NodeBindings.cs` — `setAttribute('src',…)` now calls a new
    `MaybeTriggerScriptSrc(ctx, e, name)` helper (scoped strictly to a `<script>`
    element's non-empty `src`); added the `.src` IDL property (get/set, setter
    mirrors `setAttribute('src',…)` + fires the hook) and the `.async` IDL
    property (reflects the `async` content attribute) — both were missing, and the
    injected-async (`el.async = true; el.src = …`) path needs them so
    `OnScriptElementConnected` classifies the appended script correctly.
  - **Semantics preserved:** the hook routes to `OnSrcSet`, which honours the
    per-element "already started" flag (`Already_run_external_script_does_not_rerun`
    stays green); load/error fire after the fetch+run settles; chained loaders
    (`Sequential_network_bundles_chain_to_quiescence`) reach quiescence because the
    runner increments `_inFlight` and the pump re-pumps after each completion.
  - Tests: **all 7 target tests now pass under `STARLING_JS_ENGINE=jint`**
    (`Setting_src_on_deferred_script_*`, `Setting_src_via_idl_property_*`,
    `Injected_async_external_script_*`, `Load_event_chains_*`,
    `Sequential_network_bundles_*`, `Error_event_fires_when_dynamic_script_fetch_fails`,
    `Progressive_layout_first_paints_then_reflows_*`). **Jint engine suite: 151/151.**
    Default path unchanged: **Starling.Engine 151 green**. Jint backend unit tests:
    **75 green**. Backend build clean (0 warnings, warnings-as-errors).
- 2026-05-22 — **J7 complete** (agent-claude-cody, main tree). Resolved the J6a
  FLAG: the Jint backend no longer depends on `Starling.Bindings` (hence no longer
  on `Starling.Js`). Implemented:
  - Moved `ILayoutHost` + `LayoutRect` + `OffsetMetrics` from
    `src/Starling.Bindings/LayoutHost.cs` (deleted) into a new
    `src/Starling.Js.Hosting/ILayoutHost.cs`. The seam already references
    `Starling.Dom` (the only dependency these types need) and is referenced by
    BOTH backends + the engine. **Kept the `Starling.Bindings` namespace** (types
    physically ship in `Starling.Js.Hosting.dll`) so no consumer using/qualifier
    churned: Engine `BoxLayoutHost` (`using Starling.Bindings`), the Starling
    backend `WindowBinding`/`NodeBindings` (same namespace), and the Jint backend
    (enclosing-namespace lookup from `Starling.Bindings.Jint`) all keep compiling
    unchanged.
  - `ScriptSessionOptions.LayoutHost`: `object?` → `ILayoutHost?` (added
    `using Starling.Bindings` to `IScriptSession.cs`). `JintBackendContext.LayoutHost`
    + ctor param: `object?` → `ILayoutHost?`. Removed the now-redundant `as`
    casts in the Starling backend (`StarlingScriptSession`) and both Jint
    `LayoutHost(ctx)` helpers (`NodeBindings`/`WindowBinding`).
  - **`Starling.Bindings.Jint.csproj`: removed the J6a `Starling.Bindings`
    ProjectReference.** Final Jint backend refs: `Jint` (pkg) + `Starling.Common`,
    `Starling.Css`, `Starling.Dom`, `Starling.Html`, `Starling.Js.Hosting`,
    `Starling.Net`, `Starling.Url` — NOT `Starling.Bindings`, NOT `Starling.Js`.
    Verified the build output dir contains `Starling.Js.Hosting.dll` but NO
    `Starling.Js.dll` and NO `Starling.Bindings.dll`.
  - DESIGN.md updated: seam section now notes it owns `ILayoutHost`; the Wave-1
    `LayoutHost is object?` deviation note rewritten as the J7 strongly-typed form;
    `JintBackendContext` bullet updated.
  - Tests all green: **`dotnet build Starling.slnx -c Debug` clean** (0 errors;
    warnings-as-errors holds). Jint backend unit **75**; Engine under
    `STARLING_JS_ENGINE=jint` **151**; Engine default **151**; Starling backend
    bindings **204**. Pure refactor → no new `DllImport`/`LibraryImport`, CI
    interop-seam policy still satisfied.
