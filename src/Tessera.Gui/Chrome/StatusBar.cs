using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The bottom status bar — port of <c>StatusBar</c> in <c>design/chrome.jsx</c>
/// and HANDOFF §3.7. Left side is the hover hint / navigation feedback; right
/// side is at-a-glance live engine metrics, separated by middots.
/// </summary>
public sealed class StatusBar : Border
{
    private readonly ThemeManager _tm;
    private readonly Label _left;

    public StatusBar(ThemeManager tm, string dom = "—", string bytes = "—",
        string ttfb = "—", string heap = "—")
    {
        _tm = tm;
        var t = tm.Tokens;

        _left = ChromeKit.Mono(tm, string.Empty, tm.Metrics.FsXs, t.Text2);
        _left.LineBreakMode = LineBreakMode.TailTruncation;
        _left.VerticalOptions = LayoutOptions.Center;

        var metrics = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
        };
        AddMetric(metrics, dom, " DOM");
        AddDivider(metrics);
        AddMetric(metrics, bytes, string.Empty);
        AddDivider(metrics);
        AddMetric(metrics, ttfb, " TTFB");
        AddDivider(metrics);
        AddMetric(metrics, heap, " heap");

        var grid = new Grid
        {
            ColumnSpacing = 16,
            VerticalOptions = LayoutOptions.Fill,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(_left, 0, 0);
        grid.Add(metrics, 1, 0);

        BackgroundColor = t.Panel;
        Stroke = Colors.Transparent;
        StrokeThickness = 0;
        Padding = new Thickness(12, 0);
        HeightRequest = 24;
        Content = grid;

        // The hairline top border (StatusBar sits above nothing it can hairline
        // against itself) is drawn by the composition root.
    }

    /// <summary>Sets the left-side text — a hover hint or navigation message.</summary>
    public void SetLeft(string text, bool isError = false)
    {
        _left.Text = text;
        _left.TextColor = isError ? _tm.Tokens.Err : _tm.Tokens.Text2;
    }

    private void AddMetric(Microsoft.Maui.Controls.Layout into, string value, string suffix)
    {
        var t = _tm.Tokens;
        var label = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = value, TextColor = t.Text, FontFamily = _tm.MonoFont, FontSize = _tm.Metrics.FsXs },
                    new Span { Text = suffix, TextColor = t.Muted, FontFamily = _tm.MonoFont, FontSize = _tm.Metrics.FsXs },
                },
            },
        };
        into.Add(label);
    }

    private void AddDivider(Microsoft.Maui.Controls.Layout into)
        => into.Add(ChromeKit.Mono(_tm, "·", _tm.Metrics.FsXs, _tm.Tokens.Muted));
}
