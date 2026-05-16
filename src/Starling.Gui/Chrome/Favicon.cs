using Microsoft.Maui.Controls.Shapes;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// Synthetic favicon — the M3 placeholder from <c>design/chrome.jsx</c> and
/// HANDOFF §3.6: a small rounded square tinted by a deterministic hash of the
/// host, with the host's first letter in white. Stable across reloads, so
/// there's no "loading favicon" flicker.
/// </summary>
public static class Favicon
{
    public static Border Make(ThemeManager tm, string host, double size = 12)
    {
        var hue = HostHue(host);
        var bg = OklchToColor(0.65, 0.13, hue);

        var letter = new Label
        {
            Text = InitialOf(host),
            FontFamily = tm.MonoFont,
            FontSize = size * 0.62,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };

        return new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            BackgroundColor = bg,
            Stroke = Colors.Transparent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Content = letter,
            VerticalOptions = LayoutOptions.Center,
        };
    }

    private static string InitialOf(string? host)
    {
        if (string.IsNullOrEmpty(host)) return "?";
        var h = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return h.Length == 0 ? "?" : char.ToUpperInvariant(h[0]).ToString();
    }

    /// <summary>Deterministic hue (0–360) — the exact hash from <c>chrome.jsx</c>.</summary>
    private static int HostHue(string? host)
    {
        var h = 0;
        if (host is null) return 0;
        foreach (var ch in host) h = (h * 31 + ch) % 360;
        return h;
    }

    /// <summary>
    /// OKLCH → sRGB. <paramref name="hueDegrees"/> in degrees; uses Björn
    /// Ottosson's OKLab matrices. MAUI's <see cref="Color"/> has no OKLCH
    /// constructor, so the conversion is done here.
    /// </summary>
    private static Color OklchToColor(double l, double c, double hueDegrees)
    {
        var hr = hueDegrees * Math.PI / 180.0;
        var a = c * Math.Cos(hr);
        var b = c * Math.Sin(hr);

        var lp = l + 0.3963377774 * a + 0.2158037573 * b;
        var mp = l - 0.1055613458 * a - 0.0638541728 * b;
        var sp = l - 0.0894841775 * a - 1.2914855480 * b;

        var lc = lp * lp * lp;
        var mc = mp * mp * mp;
        var sc = sp * sp * sp;

        var r = 4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc;
        var g = -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc;
        var bl = -0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc;

        return new Color(
            (float)GammaEncode(r), (float)GammaEncode(g), (float)GammaEncode(bl));
    }

    private static double GammaEncode(double linear)
    {
        var v = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        return Math.Clamp(v, 0.0, 1.0);
    }
}
