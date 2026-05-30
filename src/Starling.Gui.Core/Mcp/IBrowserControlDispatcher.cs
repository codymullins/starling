namespace Starling.Gui.Mcp;

public interface IBrowserControlDispatcher
{
    Task<BrowserControlResult> NavigateAsync(string url, CancellationToken ct);
    Task<BrowserControlResult> BackAsync(CancellationToken ct);
    Task<BrowserControlResult> ForwardAsync(CancellationToken ct);
    Task<BrowserControlResult> ReloadAsync(CancellationToken ct);
    Task<BrowserControlResult> ScreenshotAsync(string path, CancellationToken ct);
    Task<BrowserControlResult> InspectAsync(bool includeHtml, string? logPath, CancellationToken ct);
    Task<BrowserControlResult> ClickAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> MoveMouseAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> TypeTextAsync(string text, bool submit, CancellationToken ct);
    Task<BrowserControlResult> ResizeAsync(double width, double height, CancellationToken ct);
}

