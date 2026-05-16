using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// Shared chrome atoms — the C# port of the reusable pieces in
/// <c>design/chrome.jsx</c> (<c>IconBtn</c>) and the <c>.pill</c> / <c>.hr</c> /
/// <c>.vr</c> utilities in <c>design/theme.css</c>. Every helper reads its
/// colours and metrics from the supplied <see cref="ThemeManager"/> at build
/// time, so a theme flip plus a tree rebuild keeps everything in sync.
/// </summary>
public static class ChromeKit
{
    /// <summary>The <c>.pill</c> utility — a rounded accent-tinted capsule.</summary>
    public static Border Pill(ThemeManager tm, params IView[] children)
    {
        var t = tm.Tokens;
        var stack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        foreach (var c in children) stack.Add((View)c);

        return new Border
        {
            BackgroundColor = t.AccentBg,
            Stroke = t.AccentLine,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = tm.Metrics.RPill },
            Padding = new Thickness(10, 4),
            Content = stack,
            HorizontalOptions = LayoutOptions.Start,
        };
    }

    /// <summary>A 6px accent dot — the <c>.pill .dot</c> / audio-tab indicator.</summary>
    public static Border Dot(Color color, double size = 6) => new()
    {
        WidthRequest = size,
        HeightRequest = size,
        BackgroundColor = color,
        Stroke = Colors.Transparent,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
        VerticalOptions = LayoutOptions.Center,
    };

    /// <summary>A 1px hairline divider — <c>.hr</c> / <c>.vr</c>.</summary>
    public static BoxView Hairline(ThemeManager tm, bool vertical = false) => new()
    {
        Color = tm.Tokens.Border,
        WidthRequest = vertical ? 1 : -1,
        HeightRequest = vertical ? -1 : 1,
        HorizontalOptions = vertical ? LayoutOptions.Center : LayoutOptions.Fill,
        VerticalOptions = vertical ? LayoutOptions.Fill : LayoutOptions.Center,
    };

    /// <summary>A chrome (sans) label bound to the active type mode.</summary>
    public static Label Sans(ThemeManager tm, string text, double fontSize, Color color,
        FontAttributes attrs = FontAttributes.None) => new()
    {
        Text = text,
        FontFamily = tm.ChromeFont,
        FontSize = fontSize,
        FontAttributes = attrs,
        TextColor = color,
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    /// <summary>A monospace label — timestamps, paths, sizes, code.</summary>
    public static Label Mono(ThemeManager tm, string text, double fontSize, Color color,
        FontAttributes attrs = FontAttributes.None) => new()
    {
        Text = text,
        FontFamily = tm.MonoFont,
        FontSize = fontSize,
        FontAttributes = attrs,
        TextColor = color,
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    /// <summary>
    /// Wires pointer enter/exit on a view so hover styling can be applied.
    /// No-ops gracefully on platforms without pointer support.
    /// </summary>
    public static void AttachHover(View view, Action onEnter, Action onExit)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => onEnter();
        pointer.PointerExited += (_, _) => onExit();
        view.GestureRecognizers.Add(pointer);
    }
}
