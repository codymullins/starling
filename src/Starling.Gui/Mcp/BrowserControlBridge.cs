using Avalonia.Threading;

namespace Starling.Gui.Mcp;

/// <summary>
/// Avalonia port of Starling.Gui's Mcp/BrowserControlBridge.cs. Marshals MCP
/// tool calls onto the UI thread via <see cref="Dispatcher.UIThread"/> instead
/// of MAUI's <c>MainThread.InvokeOnMainThreadAsync</c>. A semaphore serializes
/// concurrent tool calls so the browser state machine only sees one mutation
/// at a time.
/// </summary>
public sealed class BrowserControlBridge
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBrowserController? _controller;

    public void Attach(IBrowserController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        Volatile.Write(ref _controller, controller);
    }

    public void Detach(IBrowserController controller)
    {
        if (ReferenceEquals(Volatile.Read(ref _controller), controller))
            Volatile.Write(ref _controller, null);
    }

    public Task<BrowserControlResult> NavigateAsync(string url, CancellationToken ct)
        => InvokeAsync(controller => controller.NavigateFromToolAsync(url, ct), ct);

    public Task<BrowserControlResult> BackAsync(CancellationToken ct)
        => InvokeAsync(controller => controller.BackFromToolAsync(ct), ct);

    public Task<BrowserControlResult> ForwardAsync(CancellationToken ct)
        => InvokeAsync(controller => controller.ForwardFromToolAsync(ct), ct);

    public Task<BrowserControlResult> ReloadAsync(CancellationToken ct)
        => InvokeAsync(controller => controller.ReloadFromToolAsync(ct), ct);

    public Task<BrowserControlResult> ScreenshotAsync(string path, CancellationToken ct)
        => InvokeAsync(controller => controller.ScreenshotFromToolAsync(path, ct), ct);

    public Task<BrowserControlResult> InspectAsync(bool includeHtml, string? logPath, CancellationToken ct)
        => InvokeAsync(controller => controller.InspectFromToolAsync(includeHtml, logPath, ct), ct);

    public Task<BrowserControlResult> ClickAsync(double x, double y, CancellationToken ct)
        => InvokeAsync(controller => controller.ClickFromToolAsync(x, y, ct), ct);

    public Task<BrowserControlResult> MoveMouseAsync(double x, double y, CancellationToken ct)
        => InvokeAsync(controller => controller.MoveMouseFromToolAsync(x, y, ct), ct);

    public Task<BrowserControlResult> TypeTextAsync(string text, bool submit, CancellationToken ct)
        => InvokeAsync(controller => controller.TypeTextFromToolAsync(text, submit, ct), ct);

    public Task<BrowserControlResult> ResizeAsync(double width, double height, CancellationToken ct)
        => InvokeAsync(controller => controller.ResizeFromToolAsync(width, height, ct), ct);

    private async Task<BrowserControlResult> InvokeAsync(
        Func<IBrowserController, Task<BrowserControlResult>> action,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var controller = Volatile.Read(ref _controller);
            if (controller is null)
                return BrowserControlResult.Failure(
                    "No Starling browser window is available.",
                    url: null,
                    title: null,
                    canGoBack: false,
                    canGoForward: false,
                    isBusy: false);

            // Dispatcher.UIThread.InvokeAsync<T>(Func<Task<T>>) flattens the
            // returned task — same semantic as MAUI's MainThread.InvokeOnMainThreadAsync.
            return await Dispatcher.UIThread.InvokeAsync(() => action(controller));
        }
        finally
        {
            _gate.Release();
        }
    }
}
