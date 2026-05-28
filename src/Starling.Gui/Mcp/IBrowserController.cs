namespace Starling.Gui.Mcp;

public interface IBrowserController
{
    Task<BrowserControlResult> NavigateFromToolAsync(string url, CancellationToken ct);
    Task<BrowserControlResult> BackFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ScreenshotFromToolAsync(string path, CancellationToken ct);
    Task<BrowserControlResult> InspectFromToolAsync(bool includeHtml, string? logPath, CancellationToken ct);
    Task<BrowserControlResult> ClickFromToolAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> MoveMouseFromToolAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> TypeTextFromToolAsync(string text, bool submit, CancellationToken ct);
    Task<BrowserControlResult> ResizeFromToolAsync(double width, double height, CancellationToken ct);
}
