using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Tessera.Gui.Mcp;

[McpServerToolType]
public sealed class BrowserTools
{
    private readonly BrowserControlBridge _browser;

    public BrowserTools(BrowserControlBridge browser)
    {
        _browser = browser;
    }

    [McpServerTool(Name = "browser_navigate", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window to a URL.")]
    public Task<BrowserControlResult> BrowserNavigate(
        [Description("The absolute URL to load, for example https://example.com.")]
        string url,
        CancellationToken cancellationToken)
        => _browser.NavigateAsync(url, cancellationToken);

    [McpServerTool(Name = "browser_back", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window back in history.")]
    public Task<BrowserControlResult> BrowserBack(CancellationToken cancellationToken)
        => _browser.BackAsync(cancellationToken);

    [McpServerTool(Name = "browser_forward", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window forward in history.")]
    public Task<BrowserControlResult> BrowserForward(CancellationToken cancellationToken)
        => _browser.ForwardAsync(cancellationToken);

    [McpServerTool(Name = "browser_refresh", UseStructuredContent = true),
     Description("Reload the current page in the visible Starling browser window.")]
    public Task<BrowserControlResult> BrowserRefresh(CancellationToken cancellationToken)
        => _browser.ReloadAsync(cancellationToken);
}
