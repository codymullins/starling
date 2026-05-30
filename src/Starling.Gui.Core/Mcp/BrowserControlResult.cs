namespace Starling.Gui.Mcp;

public sealed record BrowserControlResult(
    bool Ok,
    string? Url,
    string? Title,
    bool CanGoBack,
    bool CanGoForward,
    bool IsBusy,
    string? Error,
    // Free-form payload for tools that return more than navigation state:
    // browser_screenshot puts the written PNG path here, browser_inspect its report.
    string? Detail = null)
{
    public static BrowserControlResult Success(
        string? url,
        string? title,
        bool canGoBack,
        bool canGoForward,
        bool isBusy = false,
        string? detail = null)
        => new(true, url, title, canGoBack, canGoForward, isBusy, Error: null, Detail: detail);

    public static BrowserControlResult Failure(
        string error,
        string? url,
        string? title,
        bool canGoBack,
        bool canGoForward,
        bool isBusy)
        => new(false, url, title, canGoBack, canGoForward, isBusy, error);
}
