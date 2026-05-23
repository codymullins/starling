using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Loop;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Bindings.Jint;

/// <summary>
/// Per-session state shared by every Jint binding family (J2b…J4). One instance
/// is created per page render and threaded through
/// <see cref="JintBindings.InstallAll(JintBackendContext)"/> into each family's
/// <c>Install(JintBackendContext)</c>.
/// </summary>
/// <remarks>
/// This is the FROZEN J2a contract Wave 2 depends on — do not change the shape
/// of the public surface. Wave-2 agents read <see cref="Engine"/>,
/// <see cref="Document"/>, <see cref="BaseUrl"/>, <see cref="Http"/>,
/// <see cref="Diag"/>, <see cref="Loop"/>, and use <see cref="Wrappers"/> +
/// <see cref="JintInterop"/> helpers; they never construct this directly.
/// </remarks>
public sealed class JintBackendContext
{
    /// <summary>The live Jint engine for this session (one realm per page).</summary>
    public global::Jint.Engine Engine { get; }

    /// <summary>The shared real DOM document both engines wrap.</summary>
    public Document Document { get; }

    /// <summary>Document base URL, for resolving relative resource URLs.</summary>
    public StarlingUrl BaseUrl { get; }

    /// <summary>JS-owned HTTP client for fetch / XHR / dynamic scripts.</summary>
    public Starling.Net.StarlingHttpClient Http { get; }

    /// <summary>Session diagnostics sink.</summary>
    public IDiagnostics Diag { get; }

    /// <summary>Per-engine Dom↔JS wrapper identity map + prototype slots.</summary>
    public JintDomWrapper Wrappers { get; }

    /// <summary>Simulated event loop driving timers / rAF / the pump
    /// (J3a installs onto it; the session advances it in PumpOnce).</summary>
    public WebEventLoop Loop { get; }

    /// <summary>Layout-readback host, when the engine supplied one (typed as
    /// <c>object</c> at the seam; J2d casts it to its concrete interface).</summary>
    public object? LayoutHost { get; }

    /// <summary>Fetch script/module source through the session's shared fetch
    /// path (file/data/http). Used by the dynamic-script runner (J3a) and the
    /// module loader (J4).</summary>
    public Func<StarlingUrl, CancellationToken, Task<string?>> Fetch { get; }

    public JintBackendContext(
        global::Jint.Engine engine,
        Document document,
        StarlingUrl baseUrl,
        Starling.Net.StarlingHttpClient http,
        IDiagnostics diag,
        WebEventLoop loop,
        object? layoutHost,
        Func<StarlingUrl, CancellationToken, Task<string?>> fetch)
    {
        Engine = engine;
        Document = document;
        BaseUrl = baseUrl;
        Http = http;
        Diag = diag;
        Loop = loop;
        LayoutHost = layoutHost;
        Fetch = fetch;
        Wrappers = new JintDomWrapper(this);
    }
}
