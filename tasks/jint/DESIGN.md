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
    StarlingHttpClient Http, object? LayoutHost, IDiagnostics Diag);

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
- **`ScriptSessionOptions.LayoutHost` is `object?`, not `ILayoutHost?`.**
  `ILayoutHost` lives in `Starling.Bindings` (depends on `Starling.Dom`), which
  the seam cannot reference without inverting the dependency. Each backend casts:
  the Starling backend `as ILayoutHost`; the Jint backend stashes it on
  `JintBackendContext.LayoutHost` for J2d to cast.
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
  `WebEventLoop Loop`, `object? LayoutHost`,
  `Func<StarlingUrl,CancellationToken,Task<string?>> Fetch`, and the wrapper
  registry `JintDomWrapper Wrappers`.
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

## Conventions
- Central package versions (`Directory.Packages.props`); add `Jint` there.
- Tests: MSTest on Microsoft Testing Platform (see existing test csproj setup).
- Keep each backend's bindings self-contained so Jint is deletable in one step.
