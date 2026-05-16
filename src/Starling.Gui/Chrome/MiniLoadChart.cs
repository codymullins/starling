using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// The mini load chart that lives inside the URL bar during a page load — port
/// of <c>MiniLoadChart</c> in <c>design/chrome.jsx</c> and HANDOFF §3.4. A
/// compact stacked waterfall of the request lifecycle with a live wall-clock
/// cursor and a total-ms readout. Clicking it is the bridge into DevTools →
/// Performance (wired by the host).
/// </summary>
public static class MiniLoadChart
{
    private const double TrackWidth = 140;

    public static Border Make(
        ThemeManager tm, IReadOnlyList<TimingBar> phases, double totalMs,
        double cursorFraction = 0.78, EventHandler? onClick = null)
    {
        var t = tm.Tokens;

        var drawable = new FlameRowDrawable
        {
            Bars = phases,
            Total = totalMs <= 0 ? 1 : totalMs,
            Tokens = t,
            CornerRadius = 2f,
            BarOpacity = 0.92f,
            ShowCursor = true,
            CursorFraction = cursorFraction,
        };

        var track = new GraphicsView
        {
            Drawable = drawable,
            WidthRequest = TrackWidth,
            HeightRequest = 8,
            VerticalOptions = LayoutOptions.Center,
        };

        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                ChromeKit.Mono(tm, "●", 10, t.Accent),
                track,
                new Label
                {
                    Text = $"{totalMs:0}ms",
                    FontFamily = tm.MonoFont,
                    FontSize = 10,
                    TextColor = t.Muted,
                    WidthRequest = 38,
                    HorizontalTextAlignment = TextAlignment.End,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };

        var border = new Border
        {
            BackgroundColor = t.Surface,
            Stroke = t.Border,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RSm },
            Padding = new Thickness(8, 0),
            HeightRequest = 22,
            Content = row,
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(border, $"Page load · {totalMs:0}ms · open Performance");

        if (onClick is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => onClick(s, e);
            border.GestureRecognizers.Add(tap);
        }
        return border;
    }
}
