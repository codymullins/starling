using Starling.Bindings;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Net;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Js.Hosting;

/// <summary>
/// Severity of a <c>console.*</c> message, surfaced through
/// <see cref="IScriptSession.ConsoleSink"/>. This is a hosting-local mirror of
/// the per-engine console levels (e.g. <c>Starling.Js.ConsoleLevel</c>): the
/// seam project must not reference any concrete JS engine, so it owns its own
/// neutral enum. Each backend maps its native level to/from this set.
/// </summary>
public enum ConsoleLevel
{
    Log,
    Info,
    Warn,
    Error,
    Debug,
    Dir,
    Table,
    Trace,
}

/// <summary>Fetch the source text of a script/module URL (file:// + data: +
/// http(s)), or <c>null</c> on failure. Matches the engine's existing
/// <c>ScriptFetcher.FetchSourceAsync</c> shape so the same fetch+cache path is
/// reused by both backends and by dynamic <c>&lt;script src&gt;</c> + ES module
/// import resolution.</summary>
public delegate Task<string?> ScriptFetcherDelegate(StarlingUrl url, CancellationToken ct);

/// <summary>
/// Construction inputs for an <see cref="IScriptSession"/>. The
/// <paramref name="LayoutHost"/> is the strongly-typed <see cref="ILayoutHost"/>
/// the engine builds from its pre-script layout pass. Both <c>ILayoutHost</c>
/// and this seam live in the engine-neutral hosting project (depending only on
/// <c>Starling.Dom</c>), so neither JS backend has to reference the other's
/// bindings to consult it; passing <c>null</c> selects spec-permitted
/// "never laid out" readback.
/// </summary>
public sealed record ScriptSessionOptions(
    Document Document,
    StarlingUrl BaseUrl,
    ScriptFetcherDelegate Fetcher,
    StarlingHttpClient Http,
    ILayoutHost? LayoutHost,
    Starling.Common.Diagnostics.IDiagnostics Diag)
{
    /// <summary>Layout viewport size in CSS px. Backends expose this through
    /// <c>window.innerWidth</c>/<c>innerHeight</c> and <c>window.screen</c>.
    /// Defaults to 0 — a "no viewport hint" signal — when the host doesn't
    /// supply one (e.g. bare unit tests). Real pages branch on these (responsive
    /// grids, column-fit math), so the render path passes the real viewport.</summary>
    public int ViewportWidth { get; init; }

    /// <inheritdoc cref="ViewportWidth"/>
    public int ViewportHeight { get; init; }

    /// <summary>Cancellation observed by the backend's JS execution path so a
    /// user-visible Stop interrupts mid-script. Each backend wires this into
    /// its interpreter loop — Starling.Js checks it from the VM dispatch loop,
    /// Jint passes it via <c>Options.CancellationToken</c>. Defaults to
    /// <see cref="CancellationToken.None"/> for callers that do not need
    /// interruption (PNG/headless tests, unit tests).</summary>
    public CancellationToken AbortToken { get; init; }
}

/// <summary>
/// Engine-neutral handle to one page's live JS execution context. The engine
/// keeps ALL orchestration (ordered → async → module ordering,
/// DOMContentLoaded/load timing, the async pump loop) and delegates every
/// JS-touching operation to this interface, so the active engine is swappable
/// via <c>STARLING_JS_ENGINE</c> without changing the engine's control flow.
/// </summary>
public interface IScriptSession : IDisposable
{
    /// <summary>Sink for <c>console.*</c> output. Set by the engine before any
    /// script runs; the backend routes its native console through it.</summary>
    Action<ConsoleLevel, string> ConsoleSink { get; set; }

    /// <summary>Parse, compile, and run one classic script against the shared
    /// realm/global. JS throws are caught and normalized to
    /// <see cref="ScriptThrow"/> (then logged fail-soft by the caller); a bad
    /// script must not abort the render. <paramref name="label"/> is a
    /// diagnostic source label (URL or <c>"&lt;inline&gt;"</c>).</summary>
    void RunClassicScript(string source, string label);

    /// <summary>Evaluate <paramref name="source"/> as the entry of an ES module
    /// graph rooted at <paramref name="url"/>. Imported modules are fetched via
    /// the session's <see cref="ScriptFetcherDelegate"/>.</summary>
    Task RunModuleScriptAsync(StarlingUrl url, string source, CancellationToken ct);

    /// <summary>Fire <c>DOMContentLoaded</c> on the document/window.</summary>
    void FireDomContentLoaded();

    /// <summary>Fire <c>load</c> on the window.</summary>
    void FireLoad();

    /// <summary>Drain the engine's microtask/promise-job queue to quiescence
    /// against the active VM.</summary>
    void DrainMicrotasks();

    /// <summary>Advance the session one pump iteration: drive due timers, rAF
    /// callbacks, and promise jobs, and service any due runtime-injected /
    /// <c>src</c>-triggered dynamic scripts. Returns <c>false</c> when nothing
    /// is pending on any front (fully idle), so the engine's outer pump loop can
    /// terminate.</summary>
    bool PumpOnce();

    /// <summary>Route a runtime-injected <c>&lt;script&gt;</c> that was just
    /// connected to the document: inline non-async scripts run synchronously,
    /// external/async scripts are queued for the dynamic pump. Mirrors the
    /// "prepare a script" connection path (HTML §4.12.1).</summary>
    void OnScriptElementConnected(Node scriptEl);

    /// <summary>Mark a parser-batch <c>&lt;script&gt;</c> element as "already
    /// started" (HTML §4.12.1) so a later <c>src</c> write from JS never re-runs
    /// a script the engine already executed. The engine calls this for every
    /// element it ran via <see cref="RunClassicScript"/> /
    /// <see cref="RunModuleScriptAsync"/>, since those entry points are
    /// element-agnostic.</summary>
    void MarkScriptStarted(Node scriptEl);

    // ---- Live (post-load) interactive phase ----
    // After first paint the engine stops driving the session and hands it to the
    // page's PageScripting host, which keeps the realm alive and routes the
    // shell's events + pump ticks through the two methods below. They run on the
    // UI thread, with the backend's VM made active for the duration.

    /// <summary>Dispatch <paramref name="evt"/> to <paramref name="target"/> with
    /// the VM active so page listeners run, then drain the microtasks they queue.
    /// Returns <c>true</c> when the dispatch mutated the DOM (the shell should
    /// re-render). Listener throws are logged fail-soft, never propagated.</summary>
    bool DispatchEvent(EventTarget target, Event evt);

    /// <summary>Advance the simulated event loop to fire any timers + one rAF
    /// frame due within <paramref name="elapsedMs"/> real milliseconds of the
    /// moment the live phase began, draining microtasks and servicing any
    /// <c>fetch</c>/XHR completions and <c>src</c>-triggered dynamic scripts that
    /// landed since the last call. Returns <c>true</c> when the DOM changed and
    /// the shell should re-render.</summary>
    bool PumpFrame(long elapsedMs);
}

/// <summary>
/// A factory for one named JS engine backend. Selected by
/// <c>JsEngineSelector</c> from <c>STARLING_JS_ENGINE</c>.
/// </summary>
public interface IScriptEngineFactory
{
    /// <summary>Stable identifier matched against the env var, e.g.
    /// <c>"starling"</c> or <c>"jint"</c>.</summary>
    string Name { get; }

    /// <summary>Stand up a fresh session (realm + bindings + hooks) for one
    /// page render.</summary>
    IScriptSession CreateSession(ScriptSessionOptions options);
}

/// <summary>
/// Engine-neutral normalized form of an uncaught JavaScript exception. Each
/// backend converts its native throw (e.g. <c>Starling.Js.JsThrow</c> or
/// <c>Jint.Runtime.JavaScriptException</c>) into this so the engine's
/// fail-soft logging path is identical across engines. The original JS stack
/// (when the engine can produce one) rides in <see cref="JsStack"/>.
/// </summary>
public sealed class ScriptThrow : Exception
{
    public ScriptThrow() { }

    public ScriptThrow(string message) : base(message) { }

    public ScriptThrow(string message, Exception? inner) : base(message, inner) { }

    public ScriptThrow(string message, string? jsStack) : base(message)
    {
        JsStack = jsStack;
    }

    public ScriptThrow(string message, string? jsStack, Exception? inner) : base(message, inner)
    {
        JsStack = jsStack;
    }

    /// <summary>The JS-side stack trace, if the backend produced one.</summary>
    public string? JsStack { get; }
}
