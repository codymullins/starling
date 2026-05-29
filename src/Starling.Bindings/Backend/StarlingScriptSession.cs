using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Js.Hosting;
using Starling.Loop;
using StarlingUrl = global::Starling.Url.Url;
using ConsoleLevel = global::Starling.Js.Hosting.ConsoleLevel;
using JsConsoleLevel = global::Starling.Js.Runtime.ConsoleLevel;

namespace Starling.Bindings.Backend;

/// <summary>
/// The default JS backend: a thin <see cref="IScriptSession"/> wrapper over the
/// from-scratch <c>Starling.Js</c> engine. It encapsulates exactly the wiring
/// that previously lived inline in <c>StarlingEngine.BeginScripts</c> /
/// <c>ExecuteScript</c> / <c>RunModuleScripts</c> / <c>Fire*</c> / the pump
/// methods: it owns the <see cref="JsRuntime"/> and the simulated
/// <see cref="WebEventLoop"/>, installs the Window/Timers/AnimationFrame
/// bindings, owns the dynamic <c>&lt;script src&gt;</c> runner + the
/// <see cref="ScriptSrcHook"/> registration + the runtime-injection routing,
/// runs classic and module scripts, fires DOMContentLoaded/load, drains
/// microtasks, and advances the loop one pump iteration at a time.
/// </summary>
/// <remarks>
/// All JS-engine knowledge stays inside this assembly. The engine talks only to
/// <see cref="IScriptSession"/>, so swapping in the Jint backend never touches
/// the engine's script orchestration. Removing this backend is a single-file
/// delete plus one selector arm.
/// </remarks>
internal sealed class StarlingScriptSession : IScriptSession
{
    private readonly JsRuntime _runtime;
    private readonly WebEventLoop _loop;
    private readonly Document _document;
    private readonly StarlingUrl _baseUrl;
    private readonly ScriptFetcherDelegate _fetcher;
    private readonly IDiagnostics _diag;
    private readonly StarlingDynamicScriptRunner _dynamicRunner;
    private bool _disposed;

    // Live-phase (post-load) wall-clock baseline: the simulated-clock value at
    // the first PumpFrame, so the shell's "ms since it began driving" maps onto
    // the loop's monotonic clock without rewinding the load-time advance.
    private long _liveBaselineMs;
    private bool _liveStarted;

    private Action<ConsoleLevel, string> _consoleSink = static (_, _) => { };

    public StarlingScriptSession(ScriptSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _document = options.Document;
        _baseUrl = options.BaseUrl;
        _fetcher = options.Fetcher;
        _diag = options.Diag;
        _loop = new WebEventLoop();

        _runtime = new JsRuntime();
        // wp:M3-68 — page JS reads many host globals this engine doesn't
        // implement yet (and pages routinely probe `someGlobal` directly). Keep
        // unresolved global reads lenient (yield undefined, the legacy behavior)
        // for the page realm so a missing host global degrades gracefully instead
        // of throwing a ReferenceError and breaking the page. (The strict,
        // spec-correct default stays on for Test262 / the default realm.)
        _runtime.Realm.ThrowOnUnresolvedGlobalRead = false;

        // Stop button / supersede signal. The VM's dispatch loop polls this
        // every ~1024 opcodes and throws OperationCanceledException, which the
        // engine's navigation catch unwinds without treating as a script error.
        _runtime.AbortToken = options.AbortToken;

        // Route the realm's console through the host sink. The realm sink uses
        // Starling.Js.ConsoleLevel; map to the hosting-neutral enum here so the
        // engine never sees an engine-specific type.
        _runtime.Realm.ConsoleSink = (level, message) => _consoleSink(MapLevel(level), message);

        WindowBinding.Install(_runtime, _document, new WindowInstallOptions(
            DocumentUrl: _baseUrl.ToString(),
            HttpClient: options.Http,
            LayoutHost: options.LayoutHost,
            AnimationHost: options.AnimationHost));

        // setTimeout / setInterval / rAF ride the same simulated WebEventLoop
        // clock; PumpOnce advances it.
        TimersBinding.Install(_runtime, _loop);
        AnimationFrameBinding.Install(_runtime, _loop);

        // Dynamic <script src=…> path (HTML §4.12.1 "prepare a script"). The
        // runner shares the session's fetch delegate; it is registered before
        // the NodeConnected hook so the hook can route script-inserted async /
        // external scripts to it.
        _dynamicRunner = new StarlingDynamicScriptRunner(
            _diag, _runtime, _baseUrl,
            (url, token) => _fetcher(url, token));
        ScriptSrcHook.Register(_runtime.Realm, _dynamicRunner.OnSrcSet);
    }

    public Action<ConsoleLevel, string> ConsoleSink
    {
        get => _consoleSink;
        set => _consoleSink = value ?? throw new ArgumentNullException(nameof(value));
    }

    private static int s_scriptDumpSeq;

    public void RunClassicScript(string source, string label)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Per-script span so `STARLING_DIAG_TRACE=1` reveals which inline tag
        // or external URL is responsible for the run_scripts wall time. The
        // label is the source url (or "<inline>") set by the engine.
        using var _ = _diag.Span("js", $"run {label} ({source.Length} chars)");
        // STARLING_DIAG_DUMP_SCRIPTS=<dir> writes every classic script to that
        // directory so the source of a slow inline can be inspected without
        // re-fetching the page. Numbered so order matches the trace stream.
        var dumpDir = Environment.GetEnvironmentVariable("STARLING_DIAG_DUMP_SCRIPTS");
        if (!string.IsNullOrEmpty(dumpDir))
        {
            var seq = System.Threading.Interlocked.Increment(ref s_scriptDumpSeq);
            try
            {
                System.IO.Directory.CreateDirectory(dumpDir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dumpDir, $"script-{seq:D3}.js"),
                    "// label: " + label + "\n" + source);
            }
            catch (IOException) { /* dumping is best-effort */ }
        }
        try
        {
            Starling.Js.Bytecode.Chunk chunk;
            using (_diag.Span("js", "parse+compile"))
            {
                var program = new JsParser(source).ParseProgram();
                chunk = JsCompiler.Compile(program);
            }
            var boxesBefore = _runtime.Realm.StringBoxCount;
            var boxCharsBefore = _runtime.Realm.StringBoxCharsTotal;
            var boxBucketsBefore = new long[Starling.Js.Runtime.JsRealm.BoxBucketCount];
            for (var i = 0; i < boxBucketsBefore.Length; i++)
                boxBucketsBefore[i] = _runtime.Realm.StringBoxSizeBuckets[i];
            var nativeStatsBefore = new System.Collections.Generic.Dictionary<string, (long Count, long Ticks)>();
            foreach (var kv in Starling.Js.Runtime.AbstractOperations.NativeCallStats)
                nativeStatsBefore[kv.Key] = kv.Value;
            var nativeSelfBefore = new System.Collections.Generic.Dictionary<string, long>();
            foreach (var kv in Starling.Js.Runtime.AbstractOperations.NativeCallSelfTicks)
                nativeSelfBefore[kv.Key] = kv.Value;
            var outliersBefore = Starling.Js.Runtime.AbstractOperations.NativeCallOutliers.Count;
            var vm = new JsVm(_runtime);
            try
            {
                using (_diag.Span("js", "execute"))
                {
                    vm.Run(chunk);
                }
            }
            finally
            {
                // Log per-script counters even if execution threw — partial
                // bytecode mix on a script that throws halfway is still useful.
                var boxes = _runtime.Realm.StringBoxCount - boxesBefore;
                var boxChars = _runtime.Realm.StringBoxCharsTotal - boxCharsBefore;
                if (boxes > 0)
                {
                    _diag.Log(DiagLevel.Trace, "js",
                        $"  boxed strings: {boxes} ({boxChars} chars total, avg {boxChars / boxes})");
                    // Histogram disambiguates the avg: with the lazy
                    // JsStringObject the box itself is O(1) regardless of
                    // length, but a hot loop hitting one massive source string
                    // with .charCodeAt/.match still re-boxes per call. The
                    // bucket spread tells us "one fat string, many touches"
                    // vs "many medium strings."
                    var buckets = new System.Text.StringBuilder();
                    var bucketTotal = 0L;
                    for (var i = 0; i < Starling.Js.Runtime.JsRealm.BoxBucketCount; i++)
                    {
                        var d = _runtime.Realm.StringBoxSizeBuckets[i] - boxBucketsBefore[i];
                        if (d == 0) continue;
                        if (buckets.Length > 0) buckets.Append(", ");
                        buckets.Append($"{Starling.Js.Runtime.JsRealm.BoxBucketLabels[i]}={d}");
                        bucketTotal += d;
                    }
                    if (bucketTotal > 0)
                        _diag.Log(DiagLevel.Trace, "js", $"    box sizes: {buckets}");
                }
                long totalOps = 0;
                for (var i = 0; i < vm.OpcodeCounts.Length; i++) totalOps += vm.OpcodeCounts[i];
                if (totalOps > 0)
                {
                    var top = new (string Name, long Count)[vm.OpcodeCounts.Length];
                    for (var i = 0; i < vm.OpcodeCounts.Length; i++)
                        top[i] = (((Starling.Js.Bytecode.Opcode)i).ToString(), vm.OpcodeCounts[i]);
                    Array.Sort(top, (a, b) => b.Count.CompareTo(a.Count));
                    var topStrs = new System.Text.StringBuilder();
                    for (var i = 0; i < 5 && top[i].Count > 0; i++)
                    {
                        if (i > 0) topStrs.Append(", ");
                        topStrs.Append($"{top[i].Name}={top[i].Count}");
                    }
                    _diag.Log(DiagLevel.Trace, "js", $"  bytecode: {totalOps} ops; top5: {topStrs}");
                }
                // Native-intrinsic time per script: subtract pre-snapshot from
                // current, report any function that took > 1 ms total during
                // this execute. Tells us which DOM/binding intrinsic a slow
                // script actually spends its wall time in. Self ticks =
                // inclusive minus time charged to nested CallNative frames,
                // so a high-inclusive / low-self dispatcher (call/apply/
                // forEach) is clearly attributable to its inner work, not
                // its own body.
                var deltas = new System.Collections.Generic.List<(string Name, long Count, long Incl, long Self)>();
                foreach (var kv in Starling.Js.Runtime.AbstractOperations.NativeCallStats)
                {
                    (long Count, long Ticks) prev = nativeStatsBefore.TryGetValue(kv.Key, out var p) ? p : (0L, 0L);
                    var dc = kv.Value.Count - prev.Count;
                    var dt = kv.Value.Ticks - prev.Ticks;
                    if (dc <= 0) continue;
                    var selfPrev = nativeSelfBefore.TryGetValue(kv.Key, out var sp) ? sp : 0L;
                    var selfNow = Starling.Js.Runtime.AbstractOperations.NativeCallSelfTicks
                        .TryGetValue(kv.Key, out var sn) ? sn : 0L;
                    deltas.Add((kv.Key, dc, dt, selfNow - selfPrev));
                }
                deltas.Sort((a, b) => b.Incl.CompareTo(a.Incl));
                var ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                var hot = new System.Text.StringBuilder();
                int shown = 0;
                foreach (var (n, c, incl, self) in deltas)
                {
                    var inclMs = incl / ticksPerMs;
                    if (inclMs < 1.0) break;
                    var selfMs = self / ticksPerMs;
                    if (shown++ > 0) hot.Append(", ");
                    // Format: name=incl/self ms/calls. When incl≈self the
                    // function's own body is the cost; when incl>>self it
                    // was a dispatcher.
                    hot.Append($"{n}={inclMs:F0}/{selfMs:F0}ms/{c}");
                    if (shown >= 8) break;
                }
                if (hot.Length > 0)
                    _diag.Log(DiagLevel.Trace, "js", $"  native: {hot} (incl/self ms/calls)");

                // Single-call outliers — any individual CallNative invocation
                // that crossed the threshold. Pulls every entry the script
                // added since the snapshot index; reports the top-N by ticks.
                var outliers = new System.Collections.Generic.List<(string Name, long Ticks)>();
                {
                    var snapshot = Starling.Js.Runtime.AbstractOperations.NativeCallOutliers.ToArray();
                    var addedSince = Math.Max(0, snapshot.Length - outliersBefore);
                    // The queue may have been trimmed FIFO since the snapshot —
                    // any tail entries newer than the index count.
                    for (var i = snapshot.Length - addedSince; i < snapshot.Length; i++)
                        outliers.Add(snapshot[i]);
                }
                if (outliers.Count > 0)
                {
                    outliers.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
                    var ob = new System.Text.StringBuilder();
                    var top = Math.Min(outliers.Count, 5);
                    for (var i = 0; i < top; i++)
                    {
                        if (i > 0) ob.Append(", ");
                        ob.Append($"{outliers[i].Name}={outliers[i].Ticks / ticksPerMs:F0}ms");
                    }
                    _diag.Log(DiagLevel.Trace, "js",
                        $"  native outliers (>5ms each, top {top} of {outliers.Count}): {ob}");
                }
            }
        }
        catch (JsThrow ex)
        {
            throw new ScriptThrow(DescribeThrow(ex.Value), jsStack: null, ex);
        }
    }

    public async Task RunModuleScriptAsync(StarlingUrl url, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();

        var host = new StarlingModuleHost(_baseUrl, (u, token) => _fetcher(u, token), ct);
        var loader = new ModuleLoader(_runtime, host);

        // Entry modules with a real URL load from that URL; an inline module
        // (url == baseUrl with source provided) is registered under a synthetic
        // key so its imports resolve against the document base.
        var isInline = string.Equals(url.ToString(), _baseUrl.ToString(), StringComparison.Ordinal);

        Exception? captured = null;
        _runtime.WithActiveVm(() =>
        {
            try
            {
                if (isInline)
                {
                    var key = host.RegisterInlineModule(source);
                    loader.LoadAndEvaluate(key);
                }
                else
                {
                    host.PrimeSource(url.ToString(), source);
                    loader.LoadAndEvaluate(url.ToString());
                }
            }
            catch (JsThrow ex)
            {
                captured = new ScriptThrow(DescribeThrow(ex.Value), jsStack: null, ex);
            }
        });

        if (captured is not null) throw captured;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void FireDomContentLoaded()
        => _runtime.WithActiveVm(() => WindowBinding.FireDomContentLoaded(_runtime));

    public void FireLoad()
        => _runtime.WithActiveVm(() => WindowBinding.FireLoad(_runtime));

    public void FireBeforeUnload()
        => _runtime.WithActiveVm(() => WindowBinding.FireBeforeUnload(_runtime));

    public void FireUnload()
        => _runtime.WithActiveVm(() => WindowBinding.FireUnload(_runtime));

    public void DrainMicrotasks() => _runtime.WithActiveVm(() => { });

    public bool PumpOnce()
    {
        // One pump iteration, factored out of the engine's old
        // PumpPendingAsync/PumpWithDynamicScriptsAsync inner bodies:
        //   1) drain pending microtasks/promise jobs;
        //   2) else advance the simulated clock so the next due timers + one rAF
        //      frame fire (their callbacks land back on the microtask queue);
        //   3) else run any due dynamic <script src> fetches.
        // Returns true while any front still has pending work, false when fully
        // idle. The engine's outer loop owns the wall-clock / idle budgets.
        const int SimulatedStepMs = 50;

        if (_runtime.Realm.Microtasks.PendingCount > 0)
        {
            _runtime.WithActiveVm(() => { });
            return true;
        }

        if (_loop.PendingTimerCount > 0 || _loop.PendingAnimationFrameCount > 0)
        {
            _loop.AdvanceBy(SimulatedStepMs);
            return true;
        }

        if (_dynamicRunner.HasPending)
        {
            // Synchronous drain of the queued dynamic scripts. Each fetch+run
            // can enqueue more work (chained loaders) or kick more microtasks,
            // observed on the next PumpOnce iteration.
            _dynamicRunner.DrainAsync(CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }

        return false;
    }

    public void OnScriptElementConnected(Node scriptEl)
    {
        ArgumentNullException.ThrowIfNull(scriptEl);
        if (scriptEl is not Element { LocalName: "script" } script) return;

        // Script-inserted external scripts (and any async-flagged script) are
        // async by default — defer them to the dynamic-script pump rather than
        // running them inline. EnqueueInjectedExternal is idempotent.
        var hasSrc = !string.IsNullOrWhiteSpace(script.GetAttribute("src"));
        if (hasSrc || script.HasAttribute("async"))
        {
            if (hasSrc) _dynamicRunner.EnqueueInjectedExternal(script);
            return;
        }

        // Inline non-async injected script: run synchronously on insertion so
        // its side effects are visible to the code that appended it.
        var inline = script.TextContent;
        if (string.IsNullOrWhiteSpace(inline)) return;
        var label = "<injected inline>";
        try
        {
            RunClassicScript(inline, label);
        }
        catch (ScriptThrow ex)
        {
            _diag.Counter("engine.script.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js", $"Injected script error ({label}): {ex.Message}");
        }
    }

    public void MarkScriptStarted(Node scriptEl)
    {
        ArgumentNullException.ThrowIfNull(scriptEl);
        if (scriptEl is Element script) _dynamicRunner.MarkStarted(script);
    }

    public bool DispatchEvent(EventTarget target, Event evt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evt);
        if (_disposed) return false;

        var before = _document.MutationVersion;
        _runtime.WithActiveVm(() => target.DispatchEvent(evt));
        return _document.MutationVersion != before;
    }

    public bool PumpFrame(long elapsedMs)
    {
        if (_disposed) return false;
        if (!_liveStarted)
        {
            _liveBaselineMs = _loop.NowMilliseconds;
            _liveStarted = true;
        }

        var before = _document.MutationVersion;
        var target = _liveBaselineMs + Math.Max(0, elapsedMs);
        _runtime.WithActiveVm(() =>
        {
            // RunFrame requires a non-decreasing clock; only advance when real
            // time has moved past the loop's current now. WithActiveVm drains the
            // realm microtask queue (promise + fetch resolve jobs) on exit
            // regardless, so a no-advance tick still services completions.
            if (target > _loop.NowMilliseconds)
                _loop.RunFrame(target);
        });

        // A script that set `src` on a <script> queues an off-thread fetch+run;
        // kick the drain (fire-and-forget — its completion re-enters via jobs the
        // next pump catches).
        if (_dynamicRunner.HasPending)
            _ = _dynamicRunner.DrainAsync(CancellationToken.None);

        return _document.MutationVersion != before;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ScriptSrcHook.Register(_runtime.Realm, null);
    }

    /// <summary>Render a thrown JS value for diagnostics, pulling
    /// <c>name</c>/<c>message</c> out of Error objects so they read legibly
    /// instead of stringifying to "[object Object]".</summary>
    internal static string DescribeThrow(JsValue v)
    {
        if (v.IsObject)
        {
            try
            {
                var o = v.AsObject;
                var name = o.Get("name");
                var message = o.Get("message");
                if (!name.IsUndefined || !message.IsUndefined)
                {
                    var n = name.IsUndefined ? "Error" : JsValue.ToStringValue(name);
                    var m = message.IsUndefined ? "" : JsValue.ToStringValue(message);
                    return string.IsNullOrEmpty(m) ? n : $"{n}: {m}";
                }
            }
            catch { /* fall through to generic stringification */ }
        }
        return JsValue.ToStringValue(v);
    }

    private static ConsoleLevel MapLevel(JsConsoleLevel level) => level switch
    {
        JsConsoleLevel.Log => ConsoleLevel.Log,
        JsConsoleLevel.Info => ConsoleLevel.Info,
        JsConsoleLevel.Warn => ConsoleLevel.Warn,
        JsConsoleLevel.Error => ConsoleLevel.Error,
        JsConsoleLevel.Debug => ConsoleLevel.Debug,
        JsConsoleLevel.Dir => ConsoleLevel.Dir,
        JsConsoleLevel.Table => ConsoleLevel.Table,
        JsConsoleLevel.Trace => ConsoleLevel.Trace,
        _ => ConsoleLevel.Log,
    };
}

/// <summary>
/// The Starling backend's <see cref="IScriptEngineFactory"/>. Named
/// <c>"starling"</c>; this is the default engine selected when
/// <c>STARLING_JS_ENGINE</c> is unset.
/// </summary>
public sealed class StarlingScriptEngineFactory : IScriptEngineFactory
{
    public string Name => "starling";

    public IScriptSession CreateSession(ScriptSessionOptions options)
        => new StarlingScriptSession(options);
}
