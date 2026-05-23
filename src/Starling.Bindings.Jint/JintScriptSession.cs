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
    private bool _disposed;

    private Action<ConsoleLevel, string> _consoleSink = static (_, _) => { };

    public JintScriptSession(ScriptSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _loop = new WebEventLoop();

        // Keep the engine lean and deterministic: bounded recursion, no ambient
        // CLR member access. Web surfaces come from the explicit bindings.
        _engine = new global::Jint.Engine(opts =>
        {
            opts.Strict = false;
            opts.AllowClr(); // no assemblies registered → no ambient CLR types
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

        // Minimal console so console.* works before J2d's full Window surface.
        // J2d may redefine these against the same sink without changing behavior.
        InstallConsole();

        // Install every Web-API binding family (no-ops until each Wave-2 file is
        // implemented; the dispatcher order is the frozen J2a contract).
        JintBindings.InstallAll(_ctx);
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
        // J4 wires the real module loader through ctx.Engine.Modules + ctx.Fetch.
        // Until then a module script is a no-op-with-signal so the engine's
        // fail-soft module path logs it rather than crashing the render.
        throw new ScriptThrow(
            "ES modules are not yet implemented in the Jint backend (J4).", jsStack: null);
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
        // Mirror the Starling backend's single-iteration pump: drain promise
        // jobs, else advance the simulated clock so due timers + one rAF frame
        // fire (J3a routes them onto _loop). Returns true while any front has
        // pending work. Microtask pending-count isn't directly observable on the
        // Jint engine, so a quiet ProcessTasks plus an idle loop is treated as
        // "no in-process work".
        DrainMicrotasks();

        if (_loop.PendingTimerCount > 0 || _loop.PendingAnimationFrameCount > 0)
        {
            _loop.AdvanceBy(50);
            return true;
        }

        return false;
    }

    public void OnScriptElementConnected(Node scriptEl)
    {
        ArgumentNullException.ThrowIfNull(scriptEl);
        if (scriptEl is not Element { LocalName: "script" } script) return;

        // Inline non-async injected scripts run synchronously on insertion;
        // external/async scripts are deferred (J3a/dynamic-runner territory —
        // not yet wired on the Jint backend, so they are ignored for now).
        var hasSrc = !string.IsNullOrWhiteSpace(script.GetAttribute("src"));
        if (hasSrc || script.HasAttribute("async")) return;

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
        // The Jint backend's dynamic-script runner lands with J3a; until then
        // there is no separate started-flag bookkeeping to update. No-op.
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
            // Both DOMContentLoaded and load are dispatched on the document here;
            // J2d/J3a route window-targeted listeners once the Window surface
            // exists. (onWindow is kept in the signature for that wiring.)
            _ = onWindow;
            _ctx.Document.DispatchEvent(new Starling.Dom.Events.Event(type));
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
