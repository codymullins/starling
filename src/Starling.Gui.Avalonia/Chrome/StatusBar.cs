using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Starling.Gui.Avalonia.Theme;

namespace Starling.Gui.Avalonia.Chrome;

/// <summary>
/// Bottom status bar — Avalonia port of Starling.Gui's Chrome/StatusBar.cs.
/// Hover hint / navigation feedback on the left. Right-side engine metrics
/// live in DevTools rather than the always-visible chrome.
/// </summary>
public sealed class StatusBar : Border
{
    private readonly ThemeManager _tm;
    private readonly TextBlock _left;

    public StatusBar(ThemeManager tm)
    {
        _tm = tm;
        var t = tm.Tokens;

        _left = ChromeKit.Mono(tm, string.Empty, tm.Metrics.FsXs, t.Text2);
        _left.TextTrimming = TextTrimming.CharacterEllipsis;
        _left.VerticalAlignment = VerticalAlignment.Center;

        Background = new SolidColorBrush(t.Panel);
        BorderThickness = new Thickness(0);
        Padding = new Thickness(12, 0);
        Height = 24;
        Child = _left;
    }

    /// <summary>Sets the left-side text — hover hint or navigation message.</summary>
    public void SetLeft(string text, bool isError = false)
    {
        _left.Text = text;
        _left.Foreground = new SolidColorBrush(isError ? _tm.Tokens.Err : _tm.Tokens.Text2);
    }
}
