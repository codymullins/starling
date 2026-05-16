using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The bottom status bar. Shows the hover hint / navigation feedback on the
/// left. The right-side engine metrics (DOM / bytes / TTFB / heap) live in
/// DevTools now rather than in the always-visible chrome.
/// </summary>
public sealed class StatusBar : Border
{
    private readonly ThemeManager _tm;
    private readonly Label _left;

    public StatusBar(ThemeManager tm)
    {
        _tm = tm;
        var t = tm.Tokens;

        _left = ChromeKit.Mono(tm, string.Empty, tm.Metrics.FsXs, t.Text2);
        _left.LineBreakMode = LineBreakMode.TailTruncation;
        _left.VerticalOptions = LayoutOptions.Center;

        BackgroundColor = t.Panel;
        Stroke = Colors.Transparent;
        StrokeThickness = 0;
        Padding = new Thickness(12, 0);
        HeightRequest = 24;
        Content = _left;
    }

    /// <summary>Sets the left-side text — a hover hint or navigation message.</summary>
    public void SetLeft(string text, bool isError = false)
    {
        _left.Text = text;
        _left.TextColor = isError ? _tm.Tokens.Err : _tm.Tokens.Text2;
    }
}
