using Microsoft.Maui.ApplicationModel;

namespace Tessera.Gui.Mcp;

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

            return await MainThread.InvokeOnMainThreadAsync(() => action(controller));
        }
        finally
        {
            _gate.Release();
        }
    }
}
