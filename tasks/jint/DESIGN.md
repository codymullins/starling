# Jint alt-engine — design & shared contract

This is the contract every Jint work package codes against. Read it before
touching code. Companion: [`TRACKER.md`](TRACKER.md).

## Principle

The engine-neutral shared asset is **`Starling.Dom`** (the real DOM). Both JS
engines wrap the *same* Dom nodes; only the marshalling differs. The abstraction
seam therefore lives at the **`Starling.Engine` ↔ JS** boundary, NOT at the
`JsValue`/`JsObject` level. We do **not** abstract the value model and we do
**not** touch the ~956 existing `Starling.Bindings` call sites.

Jint is pure-managed (no P/Invoke) → it satisfies the managed-first interop
policy like `BouncyCastle`. Do not add any native dependency.

## The seam (`Starling.Js.Hosting`, new project)

New project `src/Starling.Js.Hosting` — references **only** `Starling.Dom`,
`Starling.Net`, `Starling.Common`, `Starling.Url` (NOT `Starling.Js` or Jint).
It also owns the `ILayoutHost` layout-readback contract (+ `LayoutRect` /
`OffsetMetrics`) — moved here by J7 from `Starling.Bindings` so both backends can
reach it through the seam without referencing the other's bindings. The types
keep the `Starling.Bindings` namespace (no consumer churn) but ship in the
`Starling.Js.Hosting` assembly.

```csharp
public interface IScriptEngineFactory {
    string Name { get; }                                   // "starling" | "jint"
    IScriptSession CreateSession(ScriptSessionOptions opts);
}

public interface IScriptSession : IDisposable {
    Action<ConsoleLevel, string> ConsoleSink { get; set; }
    void RunClassicScript(string source, string label);    // parse+compile+run; normalize JS throws to ScriptThrow
    Task RunModuleScriptAsync(StarlingUrl url, string source, CancellationToken ct);
    void FireDomContentLoaded();
    void FireLoad();
    void DrainMicrotasks();
    bool PumpOnce();                                        // advance timers/rAF/promise jobs; false when fully idle
    void OnScriptElementConnected(Node scriptEl);          // runtime-injected <script> routing
    void MarkScriptStarted(Node scriptEl);                 // HTML §4.12.1 "already started" (see note below)
}

public sealed record ScriptSessionOptions(
    Document Document, StarlingUrl BaseUrl, ScriptFetcherDelegate Fetcher,
    StarlingHttpClient Http, ILayoutHost? LayoutHost, IDiagnostics Diag);  // J7: strongly typed; ILayoutHost moved into the seam

public delegate Task<string?> ScriptFetcherDelegate(StarlingUrl url, CancellationToken ct);

public sealed class ScriptThrow : Exception {   // (+ standard ctors) ; carries optional JsStack
    public ScriptThrow(string message, string? jsStack = null);
    public string? JsStack { get; }
}
```

### Wave-1 contract notes (deviations from the sketch above — implemented & frozen)

These were settled while building J1/J0/J2a; later agents must follow them:

- **`ConsoleLevel` is hosting-local.** The concrete `Starling.Js.Runtime.ConsoleLevel`
  cannot be referenced from the seam (Hosting must not reference `Starling.Js`).
  `Starling.Js.Hosting` therefore owns its own neutral `ConsoleLevel` enum (same
  members). Each backend maps its native level ↔ the hosting enum
  (`StarlingScriptSession.MapLevel`; the Jint console writes the hosting enum
  directly).
- **`ScriptSessionOptions.LayoutHost` is `ILayoutHost?` (strongly typed).**
  *(Updated by J7.)* Wave 1 originally typed it `object?` because `ILayoutHost`
  lived in `Starling.Bindings`, which the seam can't reference. J7 moved
  `ILayoutHost` (+ `LayoutRect` / `OffsetMetrics`) physically into
  `Starling.Js.Hosting` (keeping the `Starling.Bindings` namespace so consumers
  don't churn) — it depends only on `Starling.Dom`, which the seam already
  references. The option is now `ILayoutHost?`, both backends use it directly
  (no `as` cast), and `JintBackendContext.LayoutHost` is `ILayoutHost?`. This
  drops `Starling.Bindings.Jint`'s `Starling.Bindings` project reference, so the
  Jint backend no longer pulls `Starling.Js` transitively — restoring the
  "delete Jint in one step, no `Starling.Js` dependency" property.
- **`Http` is `Starling.Net.StarlingHttpClient`** (not `System.Net.Http.HttpClient`) —
  that is the engine's real client type, owned/disposed by `Engine`, not the session.
- **`MarkScriptStarted(Node)` was added to `IScriptSession`.** The old engine
  called `DynamicScriptRunner.MarkStarted(element)` to flag parser-batch scripts
  "already started" so a later JS `src` rewrite never re-runs them. Since
  `RunClassicScript`/`RunModuleScriptAsync` are element-agnostic, the engine
  notifies the backend per element via this method after running each batch
  script (covered by `EngineJsExecutionTests.Already_run_external_script_*`).
- **`ScriptFetcherDelegate`** == the existing `ScriptFetcher.FetchSourceAsync`
  shape: `Task<string?> (StarlingUrl, CancellationToken)`. The engine passes a
  closure over its `ScriptFetcher`; the backend's dynamic-script runner + module
  loader fetch through it, sharing the engine's per-URL cache.

Selector mirrors `src/Starling.Paint/Backend/PaintBackendSelector.cs` exactly:
- env var `STARLING_JS_ENGINE`, lazy, default `"starling"`, **loud-fail on typo**.
- `JsEngineSelector.Factory` returns the chosen `IScriptEngineFactory`.

`Starling.Engine` keeps ALL orchestration (ordered→async→module ordering,
DOMContentLoaded/load timing, the async pump) and stops referencing
`Starling.Js` types directly — it talks only to `IScriptSession`. The current
`DynamicScriptRunner` + `ScriptSrcHook` coupling moves *inside* each backend.

## Backends

- **Starling.Js backend** (`StarlingScriptSession`): thin wrapper over today's
  exact path (`JsRuntime`, `WindowBinding.Install`, timers/rAF, the dynamic
  runner, `ScriptSrcHook`, `FireDomContentLoaded/Load`, microtask drain). Lives
  in `Starling.Bindings` (or a small `Starling.Js.Backend`). It is the default.
- **Jint backend** (`Starling.Bindings.Jint`, new project): references `Jint`,
  `Starling.Js.Hosting`, `Starling.Dom`, `Starling.Net`. Implements
  `JintScriptSession` + its own idiomatic bindings over Jint interop.

## Jint binding infrastructure (J2a — the frozen contract for Wave 2)

J2a creates these; Wave-2 agents call them and must not change their shape:

- `JintBackendContext` (public) — holds `Jint.Engine Engine`, `Document Document`,
  `StarlingUrl BaseUrl`, `StarlingHttpClient Http`, `IDiagnostics Diag`,
  `WebEventLoop Loop`, `ILayoutHost? LayoutHost` *(J7: was `object?`)*,
  `Func<StarlingUrl,CancellationToken,Task<string?>> Fetch`, and the wrapper
  registry `JintDomWrapper Wrappers`.
  - **`Action<Action> Post` (J3a additive contract).** A thread-safe "post to the
    JS thread" hook. Binding families that finish async work on a background
    thread (fetch/XHR HTTP completions, dynamic-script fetches) call
    `ctx.Post(action)` from any thread; the action is drained and invoked on the
    JS thread, and keeps the pump reporting "not idle" while anything is queued.
    `JintScriptSession` installs its own hook at construction (before
    `JintBindings.InstallAll`, so families can capture it) that feeds the session
    pump — `JintScriptSession.PumpOnce` drains the queue on the JS thread, runs
    each callback, then re-runs Jint promise jobs so reactions settle the same
    iteration. The **default** (a bare `JintBackendContext` with no session, e.g.
    a unit test) enqueues onto an internal thread-safe queue exposed via
    `bool DrainPosted()` / `bool HasPosted` — so completions still defer to a
    later turn (matching async semantics) rather than running inline on the
    background thread. This replaces the old fetch/XHR "0ms self-re-arming timer
    trampoline"; both families now route completions through `ctx.Post`.
  - **`Action<Element>? OnScriptSrcSet` (J6b additive contract).** A notification
    that a not-yet-started `<script>` element just had its `src` (re)assigned from
    JS — via `setAttribute('src', …)` or the `.src` IDL property. This is the Jint
    analogue of the Starling backend's realm-keyed `ScriptSrcHook`: the bindings
    observe the mutation (`NodeBindings.MaybeTriggerScriptSrc`, scoped strictly to
    a `<script>` element's non-empty `src`), and the session owns the fetch+run
    pipeline. `JintScriptSession` installs this at construction (after building the
    dynamic runner) to route into `JintDynamicScriptRunner.OnSrcSet`, which runs
    HTML §4.12.1 "prepare a script" — fetch via `ctx.Fetch` on a background task,
    run on the JS thread through `ctx.Post`/`PumpOnce`, then fire `load`/`error`.
    `OnSrcSet` honours the per-element "already started" flag (set by
    `MarkScriptStarted` for every parser-batch script), so re-assigning `src` on a
    script that already ran is a no-op — parser-batch scripts never double-run.
    Null until a session installs it (a bare context in a unit test): the mutation
    then just lands as a plain attribute write.
- `JintDomWrapper` (public) — per-engine identity map
  (`ConditionalWeakTable<object, ObjectInstance>` + a wrapper→backing side table);
  `JsValue Wrap(EventTarget?)`, `ObjectInstance GetOrCreate(EventTarget)`,
  `object? Unwrap(JsValue)`, `Node? UnwrapNode/UnwrapElement/UnwrapDocument`,
  plus settable prototype slots `EventTargetPrototype`, `NodePrototype`,
  `ElementPrototype`, `DocumentPrototype`, `WindowPrototype`, `EventPrototype`.
  Wave-2 sets its slot, then calls `Wrap`/`GetOrCreate`; `SelectPrototype` falls
  back up the chain so partial progress still yields usable wrappers.
- Helpers (static on `JintInterop`, public):
  `DefineMethod(Jint.Engine engine, ObjectInstance proto, string name, Func<JsValue,JsValue[],JsValue> body, int length)`,
  `DefineAccessor(Jint.Engine engine, ObjectInstance proto, string name, Func<...> getter, Func<...>? setter = null)`,
  `DefineDataProp(ObjectInstance target, string name, JsValue value, bool writable=true, bool enumerable=true, bool configurable=true)`.
  Built on Jint's **`ClrFunction`** (renamed from `ClrFunctionInstance` in Jint 4.x;
  namespace `Jint.Runtime.Interop`) + `PropertyDescriptor` (data) /
  `GetSetPropertyDescriptor` (accessor). Web-IDL flags baked in: operations are
  `{writable, !enumerable, configurable}`; attributes are `{enumerable, configurable}`.
  Value helpers `JintInterop.Str/Num/Bool`. **Note: the helpers take the `Engine`
  as the first argument** (Jint's `ClrFunction` ctor requires it) — that differs
  from the original sketch, which omitted it.
- Exception normalization: `JintInterop.Normalize(Jint.Runtime.JavaScriptException)
  → ScriptThrow` (preserves `JavaScriptStackTrace`); `JintInterop.DescribeError`
  pulls `name`/`message` off Error objects.
- `JintBindings.InstallAll(JintBackendContext ctx)` (public) calls each family's
  `internal static void Install(JintBackendContext ctx)`. **J2a created a stub
  file per Wave-2 family** with a no-op `Install` already wired into `InstallAll`
  in dependency order, so Wave-2 agents only edit their own file.
- `JintScriptSession` ships a working `console.*` (routes to the hosting
  `ConsoleSink`) so scripts run before J2d. `RunModuleScriptAsync` throws
  `ScriptThrow("…not yet implemented…")` until J4. `OnScriptElementConnected`
  runs inline injected scripts; external/async injected scripts are ignored until
  the J3a dynamic-script runner lands. `MarkScriptStarted` is a no-op until J3a.

### J4 — ES modules (implemented & frozen)

ES module support must be enabled at **engine construction** — Jint requires the
`IModuleLoader` and the `import.meta` host to be supplied to the `Engine`
constructor (`options.EnableModules(loader)` / `options.UseHostFactory(...)`), not
installed onto a live engine. So `JintScriptSession`'s constructor was restructured:
it builds `StarlingJintModuleLoader` first (from `options.BaseUrl` + the fetch
delegate), then constructs the engine with `EnableModules(_moduleLoader)` and
`UseHostFactory(_ => new StarlingJintModuleMetaHost())`. **`ModuleLoader.Install`
stays a no-op** so `JintBindings.InstallAll`'s frozen dispatcher order is untouched
(modules can't be wired onto an already-built realm).

- `StarlingJintModuleLoader : Jint.Runtime.Modules.IModuleLoader` resolves
  specifiers via `Starling.Url` against the importing module's URL (or the doc
  `BaseUrl` for entry/inline modules) and loads source through `ctx.Fetch`
  (synchronous-facing; HTTP blocks like the Starling backend's `StarlingModuleHost`).
  It primes the entry body (external → keyed by src URL; inline → synthetic
  `about:inline-N` with the doc base) so the loader never re-fetches the entry.
  **Contract detail:** the `ResolvedSpecifier.Key` is the full resolved URL and the
  `Uri` is left `null` — that makes Jint use the Key (not `Uri.LocalPath`) as
  `Module.Location`, which is what `import.meta.url` reads.
- `StarlingJintModuleMetaHost : Jint.Runtime.Host` overrides
  `GetImportMetaProperties` to set `import.meta.url` from `Module.Location`
  (Jint 4.9.2's default `import.meta` is empty).
- `RunModuleScriptAsync` registers the entry source, evaluates via
  `engine.Modules.Import(specifier)` (links + drives TLA to settlement), drains
  `ProcessTasks`, and normalizes `JavaScriptException` / `ModuleResolutionException`
  to `ScriptThrow`. Dynamic `import()` routes through the same loader automatically.

Stub files to create (one per Wave-2 package):
`NodeBindings.cs` (J2b), `EventTargetBinding.cs` (J2c), `WindowBinding.cs` +
`StorageBinding.cs`/`HistoryBinding.cs`/`PerformanceBinding.cs` (J2d),
`TimersBinding.cs`/`AnimationFrameBinding.cs` (J3a), `FetchBinding.cs` (J3b),
`XhrBinding.cs` (J3c), `Observers*`/`CryptoBinding.cs`/`CookieBinding.cs` (J3d),
`ModuleLoader.cs` (J4).

## Web-IDL fidelity rules (do NOT rely on Jint CLR auto-interop)

Define explicit prototypes/properties — auto-reflection over CLR objects gives
wrong names, enumerability, and identity. Mirror exact property names
(`textContent`, `nodeType`, …), accessor vs data semantics, and wrapper identity
from the corresponding `Starling.Bindings/*.cs` file. Reuse engine-neutral cores
where cheap (e.g. selector matching via `Starling.Css`, fetch/XHR HTTP logic via
`Starling.Net`) rather than re-porting algorithm logic.

## Event loop / async

Drive Jint's promise-job queue (`engine.Advanced.ProcessTasks()`) and timers/rAF
onto the existing `Starling.Loop.WebEventLoop`, exposed through
`IScriptSession.PumpOnce()` so `Engine.cs`'s existing pump loop is unchanged.

**J3a — `JintScriptSession.PumpOnce()` (complete).** One iteration: (1) run Jint
promise jobs; (2) drain the cross-thread `Post` queue on the JS thread, then
re-run promise jobs; (3) advance `ctx.Loop` by one simulated step so due timers +
one rAF frame fire. It returns **true** ("not idle") while ANY of these is
pending — `Loop.PendingTimerCount`, `Loop.PendingAnimationFrameCount`,
`Loop.PendingMicrotaskCount`, the post queue non-empty, or an in-flight
dynamic-script fetch — and **false** only when fully quiescent. Timers/rAF/
setImmediate ride `ctx.Loop`; async HTTP completions and dynamic `<script src>`
runs marshal back via `ctx.Post` (see the J3a `Post` hook above).

## Conventions
- Central package versions (`Directory.Packages.props`); add `Jint` there.
- Tests: MSTest on Microsoft Testing Platform (see existing test csproj setup).
- Keep each backend's bindings self-contained so Jint is deletable in one step.
