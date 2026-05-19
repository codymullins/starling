namespace Starling.Gui.Mcp;

public interface IBrowserController
{
    Task<BrowserControlResult> NavigateFromToolAsync(string url, CancellationToken ct);
    Task<BrowserControlResult> BackFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct);
}
