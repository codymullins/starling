using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Js.Hosting;
using Starling.Loop;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Bindings.Jint;

/// <summary>
/// Jint-backed <see cref="IScriptSession"/>: a parallel implementation of the
/// same seam the Starling.Js backend satisfies, over the pure-managed Jint
/// interpreter. Owns the <see cref="global::Jint.Engine"/>, the
/// <see cref="JintBackendContext"/> the binding families share, and the
/// simulated <see cref="WebEventLoop"/>; installs all Web-API bindings via
/// <see cref="JintBindings.InstallAll(JintBackendContext)"/>; runs classic +
/// module scripts; fires DOMContentLoaded/load; and advances timers/rAF/promise
/// jobs through <see cref="PumpOnce"/>.
/// </summary>
/// <remarks>
/// CLR auto-interop is intentionally minimal — Web-IDL surfaces are defined
/// explicitly by the binding families (see DESIGN.md fidelity rules), not by
/// reflecting over Starling.Dom types. As Wave 2 fills in the binding stubs the
/// installed surface grows; J2a ships the seam + a working console + the wrapper
/// registry so scripts run and DOM wrappers can be proven end-to-end.
/// </remarks>
internal sealed class JintScriptSession : IScriptSession
{
    private readonly global::Jint.Engine _engine;
    private readonly JintBackendContext _ctx;
    private readonly WebEventLoop _loop;
    private readonly JintDynamicScriptRunner _dynamicRunner;
    private readonly StarlingJintModuleLoader _moduleLoader;
    private bool _disposed;

    // Thread-safe "post to the JS thread" queue. Background work (fetch/XHR HTTP
    // completions, dynamic-script fetches) enqueues a callback here via
    // ctx.Post(...); PumpOnce drains and runs them on the JS thread. A queued job
    // keeps PumpOnce reporting "not idle".
    private readonly ConcurrentQueue<Action> _postQueue = new();

    private Action<ConsoleLevel, string> _consoleSink = static (_, _) => { };

    public JintScriptSession(ScriptSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _loop = new WebEventLoop();

        // ES module support (J4) must be enabled at engine construction: Jint
        // requires the IModuleLoader + the import.meta host to be supplied via
        // options.EnableModules(...) / options.UseHostFactory(...) before the
        // realm is built. The loader resolves specifiers against the document
        // base / importing module URL and loads source through the session's
        // shared fetch path. The host populates import.meta.url from the module
        // location. Built first so it can be passed into the engine factory.
        _moduleLoader = new StarlingJintModuleLoader(
            options.BaseUrl, (url, token) => options.Fetcher(url, token));

        // Keep the engine lean and deterministic: bounded recursion, no ambient
        // CLR member access. Web surfaces come from the explicit bindings.
        _engine = new global::Jint.Engine(opts =>
        {
            opts.Strict = false;
            opts.AllowClr(); // no assemblies registered → no ambient CLR types
            opts.EnableModules(_moduleLoader);
            opts.UseHostFactory(_ => new StarlingJintModuleMetaHost());
        });

        _ctx = new JintBackendContext(
            engine: _engine,
            document: options.Document,
            baseUrl: options.BaseUrl,
            http: options.Http,
            diag: options.Diag,
            loop: _loop,
            layoutHost: options.LayoutHost,
            fetch: options.Fetcher.Invoke);

        // Install the cross-thread "post to JS thread" hook BEFORE the binding
        // families so fetch/XHR/dynamic-script work can capture it. Calls are
        // safe from any thread; PumpOnce drains the queue on the JS thread.
        _ctx.Post = Post;

        // Dynamic <script src=…> path (HTML §4.12.1 "prepare a script"). Fetches
        // through the session's fetch delegate and runs on the JS thread via the
        // post queue.
        _dynamicRunner = new JintDynamicScriptRunner(
            options.Diag, options.BaseUrl,
            (url, token) => options.Fetcher(url, token),
            RunClassicScript, Post);

        // Route the NodeBindings src-set notification (J6b) into the dynamic
        // runner so `script.setAttribute('src', …)` / `script.src = …` from JS
        // runs "prepare a script". Installed before the binding families so the
        // src setter can fire it. OnSrcSet honours the "already started" flag, so
        // re-assigning src on a parser-batch script that already ran is a no-op.
        _ctx.OnScriptSrcSet = _dynamicRunner.OnSrcSet;

        // Minimal console so console.* works before J2d's full Window surface.
        // J2d may redefine these against the same sink without changing behavior.
        InstallConsole();

        // Install every Web-API binding family (no-ops until each Wave-2 file is
        // implemented; the dispatcher order is the frozen J2a contract).
        JintBindings.InstallAll(_ctx);
    }

    /// <summary>Thread-safe enqueue of a JS-thread callback. Drained by
    /// <see cref="PumpOnce"/>. See <see cref="JintBackendContext.Post"/>.</summary>
    private void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _postQueue.Enqueue(action);
    }

    public Action<ConsoleLevel, string> ConsoleSink
    {
        get => _consoleSink;
        set => _consoleSink = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void RunClassicScript(string source, string label)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            _engine.Execute(source, label ?? "<script>");
            _engine.Advanced.ProcessTasks();
        }
        catch (JavaScriptException ex)
        {
            throw JintInterop.Normalize(ex);
        }
    }

    public Task RunModuleScriptAsync(StarlingUrl url, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();

        // Inline <script type="module"> bodies have no URL of their own (Engine.cs
        // passes the document base for them); register them under a synthetic
        // about:inline-N specifier whose imports resolve against the document
        // base. External modules carry their own src URL; prime the entry body so
        // the loader does not re-fetch it, then import by that URL.
        var isInline = string.Equals(url.ToString(), _ctx.BaseUrl.ToString(), StringComparison.Ordinal);
        var specifier = isInline
            ? _moduleLoader.RegisterInline(source)
            : _moduleLoader.RegisterEntry(url, source);

        try
        {
            // Import links + evaluates the module graph and drives top-level await
            // to settlement (Jint runs the module's promise jobs as part of the
            // import). Drain any remaining microtasks so reactions queued by TLA /
            // dynamic import() settle before we return.
            _engine.Modules.Import(specifier);
            _engine.Advanced.ProcessTasks();
        }
        catch (JavaScriptException ex)
        {
            throw JintInterop.Normalize(ex);
        }
        catch (global::Jint.Runtime.Modules.ModuleResolutionException ex)
        {
            throw new ScriptThrow(ex.Message, jsStack: null, ex);
        }

        return Task.CompletedTask;
    }

    public void FireDomContentLoaded() => DispatchDocumentEvent("DOMContentLoaded", onWindow: false);

    public void FireLoad() => DispatchDocumentEvent("load", onWindow: true);

    public void DrainMicrotasks()
    {
        try { _engine.Advanced.ProcessTasks(); }
        catch (JavaScriptException ex) { ReportUncaught(ex); }
    }

    public bool PumpOnce()
    {
        // One pump iteration:
        //   1) run Jint promise jobs (engine.Advanced.ProcessTasks);
        //   2) drain the cross-thread post queue ON THE JS THREAD (fetch/XHR HTTP
        //      completions, dynamic-script runs) — then re-run promise jobs so any
        //      reactions they queued settle this iteration;
        //   3) advance the simulated clock so due timers + one rAF frame fire.
        // Reports "not idle" (true) while ANY of these still has pending work:
        // loop timers, rAF callbacks, loop microtasks, the post queue, or an
        // in-flight dynamic-script fetch. Returns false only when fully quiescent.
        const int SimulatedStepMs = 50;

        DrainMicrotasks();

        if (DrainPostQueue())
            DrainMicrotasks();

        if (_loop.PendingTimerCount > 0 || _loop.PendingAnimationFrameCount > 0)
            _loop.AdvanceBy(SimulatedStepMs);

        return _loop.PendingTimerCount > 0
            || _loop.PendingAnimationFrameCount > 0
            || _loop.PendingMicrotaskCount > 0
            || !_postQueue.IsEmpty
            || _dynamicRunner.HasPending;
    }

    /// <summary>Drain every callback the post queue holds <i>now</i>, on the JS
    /// thread. A drained callback may enqueue further work; that lands on a later
    /// PumpOnce. Returns true if any callback ran.</summary>
    private bool DrainPostQueue()
    {
        var ran = false;
        while (_postQueue.TryDequeue(out var action))
        {
            ran = true;
            try
            {
                action();
            }
            catch (JavaScriptException ex)
            {
                ReportUncaught(ex);
            }
            catch (Exception ex)
            {
                _ctx.Diag.Log(DiagLevel.Warn, "engine.js", $"Posted callback threw: {ex.Message}");
            }
        }
        return ran;
    }

    public void OnScriptElementConnected(Node scriptEl)
    {
        ArgumentNullException.ThrowIfNull(scriptEl);
        if (scriptEl is not Element { LocalName: "script" } script) return;

        // Script-inserted external scripts (and any async-flagged script) are
        // async by default — route them to the dynamic-script runner
        // (HTML §4.12.1) rather than running them inline. EnqueueInjectedExternal
        // is idempotent and honours the "already started" flag.
        var hasSrc = !string.IsNullOrWhiteSpace(script.GetAttribute("src"));
        if (hasSrc || script.HasAttribute("async"))
        {
            if (hasSrc) _dynamicRunner.EnqueueInjectedExternal(script);
            return;
        }

        // Inline non-async injected script: run synchronously on insertion so its
        // side effects are visible to the code that appended it.
        var inline = script.TextContent;
        if (string.IsNullOrWhiteSpace(inline)) return;
        try
        {
            RunClassicScript(inline, "<injected inline>");
        }
        catch (ScriptThrow ex)
        {
            _ctx.Diag.Log(DiagLevel.Warn, "engine.js", $"Injected script error: {ex.Message}");
        }
    }

    public void MarkScriptStarted(Node scriptEl)
    {
        ArgumentNullException.ThrowIfNull(scriptEl);
        // Flag a parser-batch script "already started" so a later JS `src` write
        // never re-runs it (HTML §4.12.1).
        if (scriptEl is Element script) _dynamicRunner.MarkStarted(script);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }

    // ---- internals ----

    private void DispatchDocumentEvent(string type, bool onWindow)
    {
        try
        {
            // DOMContentLoaded targets the document and bubbles through the DOM
            // tree; load targets the window only. Both also dispatch on the
            // separate window host EventTarget (bound by J2d's WindowBinding via
            // JintDomWrapper.BindExisting on engine.Global) because the window
            // is not in the DOM parent chain that EventDispatcher walks.
            if (!onWindow)
            {
                _ctx.Document.DispatchEvent(new Starling.Dom.Events.Event(type,
                    new Starling.Dom.Events.EventInit(Bubbles: true, Cancelable: false)));
            }
            if (_ctx.Wrappers.Unwrap(_engine.Global) is Starling.Dom.Events.EventTarget windowHost)
            {
                windowHost.DispatchEvent(new Starling.Dom.Events.Event(type));
            }
            _engine.Advanced.ProcessTasks();
        }
        catch (JavaScriptException ex)
        {
            ReportUncaught(ex);
        }
        catch (Exception ex)
        {
            _ctx.Diag.Log(DiagLevel.Warn, "engine.js", $"'{type}' handler threw: {ex.Message}");
        }
    }

    private void ReportUncaught(JavaScriptException ex)
        => _consoleSink(ConsoleLevel.Error,
            $"Uncaught {JintInterop.DescribeError(ex.Error, ex.Message)}");

    private void InstallConsole()
    {
        var console = new global::Jint.Native.JsObject(_engine);
        void Method(string name, ConsoleLevel level)
            => JintInterop.DefineMethod(_engine, console, name, (_, args) =>
            {
                _consoleSink(level, FormatConsoleArgs(args));
                return JsValue.Undefined;
            }, 0);

        Method("log", ConsoleLevel.Log);
        Method("info", ConsoleLevel.Info);
        Method("warn", ConsoleLevel.Warn);
        Method("error", ConsoleLevel.Error);
        Method("debug", ConsoleLevel.Debug);
        Method("trace", ConsoleLevel.Trace);
        Method("dir", ConsoleLevel.Dir);
        Method("table", ConsoleLevel.Table);

        JintInterop.DefineDataProp(_engine.Global, "console", console,
            writable: true, enumerable: false, configurable: true);
    }

    private static string FormatConsoleArgs(JsValue[] args)
    {
        if (args.Length == 0) return string.Empty;
        return string.Join(" ", args.Select(a => a.IsNull() ? "null" : a.ToString()));
    }
}

/// <summary>
/// The Jint backend's <see cref="IScriptEngineFactory"/>. Named <c>"jint"</c>;
/// selected when <c>STARLING_JS_ENGINE=jint</c>.
/// </summary>
public sealed class JintScriptEngineFactory : IScriptEngineFactory
{
    public string Name => "jint";

    public IScriptSession CreateSession(ScriptSessionOptions options)
        => new JintScriptSession(options);
}
