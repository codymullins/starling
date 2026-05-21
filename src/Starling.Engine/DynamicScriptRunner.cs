using System.Runtime.CompilerServices;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Url;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Engine;

/// <summary>
/// Implements the dynamic half of HTML §4.12.1 "prepare a script": when JS sets
/// <c>src</c> on a <c>&lt;script&gt;</c> element that has not yet started, fetch
/// the external resource, execute it on the shared realm, then fire
/// <c>load</c> (or <c>error</c>) on the element. Deferred-bundle loaders run on
/// <c>DOMContentLoaded</c> and copy a custom <c>data-*</c> attribute onto
/// <c>src</c> to start the real download; sequential loaders chain by setting
/// <c>src</c> on the next script only from the previous script's <c>load</c>
/// handler, so firing the event is load-bearing, not cosmetic.
/// </summary>
/// <remarks>
/// Scope: classic external scripts. <c>type="module"</c>, async/defer ordering
/// nuance, CSP/nonce enforcement, and re-fetching after a second <c>src</c>
/// write are out of scope (see the WP report). The "already started" flag (per
/// HTML §4.12.1) is tracked per element so the parser-run batch and any script
/// we run here are never run twice, while an empty parser-inserted script that
/// is given a <c>src</c> for the first time becomes eligible exactly once.
/// </remarks>
internal sealed class DynamicScriptRunner
{
    private readonly IDiagnostics _diag;
    private readonly JsRuntime _runtime;
    private readonly StarlingUrl _baseUrl;
    private readonly Func<StarlingUrl, CancellationToken, Task<string?>> _fetch;

    // HTML §4.12.1 "already started" flag, per element. A script in this set
    // has run (or been claimed for running) and must never run again.
    private readonly ConditionalWeakTable<Element, object> _started = new();
    // Elements whose src was set from JS and are awaiting fetch+execute. Ordered
    // so sequential loaders settle in src-set order.
    private readonly Queue<Element> _pending = new();
    private static readonly object Marker = new();

    public DynamicScriptRunner(
        IDiagnostics diag, JsRuntime runtime, StarlingUrl baseUrl,
        Func<StarlingUrl, CancellationToken, Task<string?>> fetch)
    {
        _diag = diag;
        _runtime = runtime;
        _baseUrl = baseUrl;
        _fetch = fetch;
    }

    /// <summary>Mark a script element as "already started" so a later
    /// <c>src</c> write does not re-run it. Called for every script the parser
    /// batch executed.</summary>
    public void MarkStarted(Element script) => _started.AddOrUpdate(script, Marker);

    /// <summary>True when at least one src-triggered script is waiting to be
    /// fetched+executed. The pump loop checks this for quiescence.</summary>
    public bool HasPending => _pending.Count > 0;

    /// <summary>Hook target wired into the binding layer: queue a script whose
    /// <c>src</c> was just set, unless it already started. Runs synchronously on
    /// the JS thread (inside the setAttribute call), so it must not block —
    /// it only enqueues; the async fetch happens later on the pump.</summary>
    public void OnSrcSet(Element script)
    {
        if (_started.TryGetValue(script, out _)) return;
        // Claim it now (set "already started") so a re-entrant or duplicate
        // src write during the same drain doesn't double-queue it.
        _started.AddOrUpdate(script, Marker);
        _pending.Enqueue(script);
    }

    /// <summary>Drain every queued script: fetch, execute on the realm, then
    /// fire load/error. Each execution can itself enqueue the next script (a
    /// chained loader sets src #N+1 from #N's load handler), so we loop until
    /// the queue empties. Returns the number of scripts processed this call.</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var processed = 0;
        while (_pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var script = _pending.Dequeue();
            await RunOneAsync(script, ct).ConfigureAwait(false);
            processed++;
        }
        return processed;
    }

    private async Task RunOneAsync(Element script, CancellationToken ct)
    {
        var src = script.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            FireEvent(script, "error");
            return;
        }

        var absolute = ResolveAbsolute(src, _baseUrl);
        if (absolute is null)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve dynamic <script src='{src}'>");
            FireEvent(script, "error");
            return;
        }

        string? source;
        try
        {
            source = await _fetch(absolute, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Dynamic script fetch failed {absolute}: {ex.Message}");
            source = null;
        }

        if (source is null)
        {
            // §"if the fetch fails": fire `error`, then continue. Sequential
            // loaders that only chain off `load` will stop here, matching a
            // real browser where the next bundle never starts.
            FireEvent(script, "error");
            return;
        }

        var label = absolute.ToString();
        var ranOk = false;
        _runtime.WithActiveVm(() =>
        {
            try
            {
                var program = new JsParser(source).ParseProgram();
                var chunk = JsCompiler.Compile(program);
                new JsVm(_runtime).Run(chunk);
                _diag.Counter("engine.script.dynamic.ok", 1);
                ranOk = true;
            }
            catch (JsThrow ex)
            {
                _diag.Counter("engine.script.dynamic.failed", 1);
                _diag.Log(DiagLevel.Warn, "engine.js",
                    $"Uncaught dynamic script error ({label}): {JsValue.ToStringValue(ex.Value)}");
            }
            catch (Exception ex)
            {
                _diag.Counter("engine.script.dynamic.failed", 1);
                _diag.Log(DiagLevel.Warn, "engine.js",
                    $"Dynamic script compile/run failure ({label}): {ex.Message}");
            }
        });

        // §"executing the script block": even a script whose body threw at
        // runtime still successfully *loaded*, so it fires `load` (real
        // browsers fire `error` only for fetch/parse failures, not for
        // exceptions thrown by an otherwise-loaded script). A compile failure
        // is treated as a load failure → `error`.
        FireEvent(script, ranOk ? "load" : "error");
    }

    /// <summary>Dispatch a simple, non-bubbling event on the element through the
    /// shared VM so JS listeners (e.g. a chained loader's <c>load</c> handler)
    /// run and any src they set lands back on <see cref="_pending"/>.</summary>
    private void FireEvent(Element script, string type)
    {
        _runtime.WithActiveVm(() =>
        {
            try
            {
                script.DispatchEvent(new Event(type));
            }
            catch (Exception ex)
            {
                _diag.Log(DiagLevel.Warn, "engine.js",
                    $"Dynamic script '{type}' handler threw: {ex.Message}");
            }
        });
    }

    private static StarlingUrl? ResolveAbsolute(string href, StarlingUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? UrlParser.Parse(href)
            : UrlParser.Parse(href, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }
}
