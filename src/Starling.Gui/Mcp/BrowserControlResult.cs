namespace Tessera.Gui.Mcp;

public sealed record BrowserControlResult(
    bool Ok,
    string? Url,
    string? Title,
    bool CanGoBack,
    bool CanGoForward,
    bool IsBusy,
    string? Error)
{
    public static BrowserControlResult Success(
        string? url,
        string? title,
        bool canGoBack,
        bool canGoForward,
        bool isBusy = false)
        => new(true, url, title, canGoBack, canGoForward, isBusy, Error: null);

    public static BrowserControlResult Failure(
        string error,
        string? url,
        string? title,
        bool canGoBack,
        bool canGoForward,
        bool isBusy)
        => new(false, url, title, canGoBack, canGoForward, isBusy, error);
}
