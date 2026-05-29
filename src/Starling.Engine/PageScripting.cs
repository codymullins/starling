using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Hosting;
using Starling.Net;

namespace Starling.Engine;

/// <summary>
/// The live JavaScript context retained for an interactively-loaded page so the
/// shell can keep driving it <em>after</em> first paint. It lets the shell:
/// <list type="bullet">
/// <item>dispatch DOM events (input/change/focus/keydown/…) into page listeners
/// via <see cref="DispatchEvent"/>, so forms and search-as-you-type handlers
/// run; and</item>
/// <item>pump the page's event loop — microtasks, <c>setTimeout</c>/<c>setInterval</c>,
/// <c>requestAnimationFrame</c>, and <c>fetch</c>/XHR completions — against real
/// wall-clock time via <see cref="PumpFrame"/>.</item>
/// </list>
/// Owned by the <see cref="LaidOutPage"/> and disposed with it (tearing down the
/// JS HTTP client, the script fetcher, and the realm's DOM hooks). On a relayout
/// (resize / form edit) the owning page transfers this object to its successor,
/// so the same live realm survives reflows.
/// </summary>
/// <remarks>
/// Engine-neutral: the live realm is reached only through the
/// <see cref="IScriptSession"/> seam, so the same retained-context behaviour
/// works whichever JS backend (<c>STARLING_JS_ENGINE</c>) ran the page. The
/// backend owns the realm, the simulated loop, and the dynamic-script runner;
/// this host just routes the shell's post-load events and pump ticks into it.
/// </remarks>
/// <remarks>
/// Single-threaded: every method must run on the shell's UI thread. The realm is
/// not thread-safe; the load-time async phase has fully settled before the page
/// (and this host) is handed to the shell, after which only the UI thread touches
/// the realm. Off-thread <c>fetch</c>/XHR completions enqueue resolve jobs onto
/// the realm's (thread-safe) microtask queue, which <see cref="PumpFrame"/> drains.
/// </remarks>
public sealed class PageScripting : IDisposable
{
    private readonly IScriptSession _session;
    private readonly StarlingHttpClient _http;
    private readonly ScriptFetcher _fetcher;
    private readonly Document _document;
    private bool _disposed;

    internal PageScripting(
        IScriptSession session, StarlingHttpClient http, ScriptFetcher fetcher, Document document)
    {
        _session = session;
        _http = http;
        _fetcher = fetcher;
        _document = document;
    }

    /// <summary>The document whose realm this drives — equals the owning page's.</summary>
    public Document Document => _document;

    /// <summary>
    /// Dispatch <paramref name="evt"/> to <paramref name="target"/> with the
    /// realm's VM active so JS listeners run, then drain the microtasks they
    /// queue. Returns true when the dispatch mutated the DOM (the shell should
    /// re-render). Exceptions thrown by page listeners are swallowed by the
    /// engine's console pipe, never surfacing to the shell.
    /// </summary>
    public bool DispatchEvent(EventTarget target, Event evt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evt);
        if (_disposed) return false;
        return _session.DispatchEvent(target, evt);
    }

    /// <summary>
    /// Advance the page's event loop to <paramref name="elapsedMs"/> (real ms
    /// elapsed since the shell began driving this host), firing any due timers
    /// and one <c>requestAnimationFrame</c> frame, and draining microtasks plus
    /// any <c>fetch</c>/XHR completions that landed since the last pump. Returns
    /// true when the DOM changed and the shell should re-render.
    /// </summary>
    public bool PumpFrame(long elapsedMs)
    {
        if (_disposed) return false;
        return _session.PumpFrame(elapsedMs);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.FireBeforeUnload();
        _session.FireUnload();
        // Make the document inert again and release the JS-owned resources. The
        // session's own Dispose unregisters the realm's src/inject hooks.
        _document.NodeConnected = null;
        _session.Dispose();
        _http.Dispose();
        _fetcher.Dispose();
    }
}
