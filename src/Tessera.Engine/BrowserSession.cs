using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Net;
using Tessera.Net.Http.Cookies;

namespace Tessera.Engine;

/// <summary>
/// Small stateful browsing session for headless and shell hosts. It keeps
/// navigation history and a shared cookie jar across page loads.
/// </summary>
public sealed class BrowserSession : IDisposable
{
    private readonly TesseraEngine _engine;

    public BrowserSession(IDiagnostics? diagnostics = null, CookieJar? cookieJar = null)
    {
        Cookies = cookieJar ?? new CookieJar();
        _engine = new TesseraEngine(
            diagnostics,
            httpFactory: () => new TesseraHttpClient(new TesseraHttpClientOptions
            {
                CookieJar = Cookies,
            }));
    }

    public NavigationHistory History { get; } = new();
    public CookieJar Cookies { get; }

    public async Task<Result<RenderOutcome, RenderError>> NavigateAsync(
        string url,
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
    {
        var result = await _engine.RenderAsync(url, options, outputPath, ct).ConfigureAwait(false);
        if (result.IsOk)
            History.Navigate(url);
        return result;
    }

    public Task<Result<RenderOutcome, RenderError>> BackAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
        => _engine.RenderAsync(History.Back(), options, outputPath, ct);

    public Task<Result<RenderOutcome, RenderError>> ForwardAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
        => _engine.RenderAsync(History.Forward(), options, outputPath, ct);

    public Task<Result<RenderOutcome, RenderError>> ReloadAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
        => _engine.RenderAsync(History.Reload(), options, outputPath, ct);

    public void Dispose()
    {
        // The underlying engine creates per-request clients; no persistent
        // sockets need disposal yet.
    }
}
