using System.Diagnostics;
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
    private readonly IDiagnostics _diag;

    public BrowserSession(IDiagnostics? diagnostics = null, CookieJar? cookieJar = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        Cookies = cookieJar ?? new CookieJar();
        _engine = new TesseraEngine(
            diagnostics,
            httpFactory: () => new TesseraHttpClient(new TesseraHttpClientOptions
            {
                CookieJar = Cookies,
                Diagnostics = diagnostics,
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
        return await TrackAsync("navigate", url, async () =>
        {
            var result = await _engine.RenderAsync(url, options, outputPath, ct).ConfigureAwait(false);
            if (result.IsOk)
                History.Navigate(url);
            return result;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Same flow as <see cref="NavigateAsync"/> but returns a laid-out page
    /// the caller can walk for interactive rendering, instead of saving a PNG.
    /// </summary>
    public async Task<Result<LaidOutPage, RenderError>> NavigateInteractiveAsync(
        string url,
        RenderOptions options,
        CancellationToken ct = default)
    {
        return await TrackAsync("navigate", url, async () =>
        {
            var result = await _engine.LayoutPageAsync(url, options, ct).ConfigureAwait(false);
            if (result.IsOk)
                History.Navigate(url);
            return result;
        }).ConfigureAwait(false);
    }

    public Task<Result<LaidOutPage, RenderError>> BackInteractiveAsync(
        RenderOptions options,
        CancellationToken ct = default)
    {
        var url = History.Back();
        return TrackAsync("back", url, () => _engine.LayoutPageAsync(url, options, ct));
    }

    public Task<Result<LaidOutPage, RenderError>> ForwardInteractiveAsync(
        RenderOptions options,
        CancellationToken ct = default)
    {
        var url = History.Forward();
        return TrackAsync("forward", url, () => _engine.LayoutPageAsync(url, options, ct));
    }

    public Task<Result<LaidOutPage, RenderError>> ReloadInteractiveAsync(
        RenderOptions options,
        CancellationToken ct = default)
    {
        var url = History.Reload();
        return TrackAsync("reload", url, () => _engine.LayoutPageAsync(url, options, ct));
    }

    public Task<Result<RenderOutcome, RenderError>> BackAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
    {
        var url = History.Back();
        return TrackAsync("back", url, () => _engine.RenderAsync(url, options, outputPath, ct));
    }

    public Task<Result<RenderOutcome, RenderError>> ForwardAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
    {
        var url = History.Forward();
        return TrackAsync("forward", url, () => _engine.RenderAsync(url, options, outputPath, ct));
    }

    public Task<Result<RenderOutcome, RenderError>> ReloadAsync(
        RenderOptions options,
        string outputPath,
        CancellationToken ct = default)
    {
        var url = History.Reload();
        return TrackAsync("reload", url, () => _engine.RenderAsync(url, options, outputPath, ct));
    }

    public void Dispose()
    {
        // The underlying engine creates per-request clients; no persistent
        // sockets need disposal yet.
    }

    /// <summary>
    /// Wraps a navigation operation in a "session" span, tags the URL,
    /// increments per-op counters, and surfaces error status on the active
    /// activity. Returns the underlying result unchanged.
    /// </summary>
    private async Task<Result<T, RenderError>> TrackAsync<T>(
        string op,
        string? url,
        Func<Task<Result<T, RenderError>>> work)
    {
        using var span = _diag.Span("session", op);
        if (!string.IsNullOrEmpty(url))
            Activity.Current?.SetTag("http.url", url);
        _diag.Counter("session.navigations", 1);

        var result = await work().ConfigureAwait(false);
        if (result.IsErr)
        {
            _diag.Counter("session.navigation_failures", 1);
            _diag.Log(DiagLevel.Error, "session", $"{op} failed: {result.Error.Message}");
            Activity.Current?.SetStatus(ActivityStatusCode.Error, result.Error.Message);
        }
        return result;
    }
}
