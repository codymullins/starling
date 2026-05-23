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
| J3a | 🔵 | Starling.Bindings.Jint | Timers, rAF, event-loop pump | `TimersBinding.cs`, `AnimationFrameBinding.cs` | `TimersBinding.cs`/`AnimationFrameBinding.cs` (+ a dynamic-script runner; see note) |
| J3b | 🟢 | Starling.Bindings.Jint | fetch | `FetchBinding.cs` | `FetchBinding.cs` |
| J3c | 🟢 | Starling.Bindings.Jint | XMLHttpRequest | `XhrBinding.cs` | `XhrBinding.cs` |
| J3d | 🟢 | Starling.Bindings.Jint | Observers, crypto, cookies | `Observers/`, `CryptoBinding.cs`, `CookieBinding.cs` | `ObserversBinding.cs`/`CryptoBinding.cs`/`CookieBinding.cs` |
| J4 | 🔵 | Starling.Bindings.Jint | ES modules (loader, TLA, dynamic `import()`) | module path in `Engine.cs` | `ModuleLoader.cs` (+ `JintScriptSession.RunModuleScriptAsync`) |

### Wave 3 — integration, CI, conformance, docs

| ID | Status | Subsystem | Summary |
|---|---|---|---|
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
