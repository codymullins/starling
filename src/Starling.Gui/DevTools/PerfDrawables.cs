using Microsoft.Maui.Graphics;
using Tessera.Gui.Theme;

namespace Tessera.Gui.DevTools;

/// <summary>
/// The Performance frame strip — one block per frame, coloured by FPS health
/// (HANDOFF §5.2). Mint when ≥60fps, amber + ⚠ when below.
/// </summary>
public sealed class FramesStripDrawable : IDrawable
{
    public IReadOnlyList<PerfFrame> Frames { get; set; } = Array.Empty<PerfFrame>();
    public double Total { get; set; } = 1;
    public ThemeTokens Tokens { get; set; } = ThemeTokens.Dark;
    public string? MonoFontFamily { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Total <= 0) return;
        float w = dirtyRect.Width, h = dirtyRect.Height;

        foreach (var f in Frames)
        {
            var x = (float)(f.T / Total) * w;
            var fw = (float)(f.D / Total) * w;

            canvas.FillColor = f.Jank ? Tokens.Warn.WithAlpha(0.16f) : Tokens.Accent.WithAlpha(0.10f);
            canvas.FillRectangle(x, 4f, fw, h - 8f);

            canvas.StrokeColor = Tokens.Border;
            canvas.StrokeSize = 1;
            canvas.DrawLine(x, 4f, x, h - 4f);

            canvas.FontColor = f.Jank ? Tokens.Warn : Tokens.Muted;
            canvas.FontSize = 10;
            if (MonoFontFamily is not null) canvas.Font = new Microsoft.Maui.Graphics.Font(MonoFontFamily);
            var text = $"{f.Fps}fps · {f.D:0}ms{(f.Jank ? " ⚠" : string.Empty)}";
            canvas.DrawString(text, x, 0f, fw, h, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}

/// <summary>
/// The Performance ruler — 50ms ticks plus dashed accent verticals at the named
/// Web-Vitals markers (FB / FCP / LCP / TTI), per HANDOFF §5.2.
/// </summary>
public sealed class RulerDrawable : IDrawable
{
    public double Total { get; set; } = 1;
    public IReadOnlyList<PerfMarker> Markers { get; set; } = Array.Empty<PerfMarker>();
    public ThemeTokens Tokens { get; set; } = ThemeTokens.Dark;
    public string? MonoFontFamily { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Total <= 0) return;
        float w = dirtyRect.Width, h = dirtyRect.Height;
        if (MonoFontFamily is not null) canvas.Font = new Microsoft.Maui.Graphics.Font(MonoFontFamily);

        canvas.StrokeSize = 1;
        for (double t = 0; t <= Total; t += 50)
        {
            var x = (float)(t / Total) * w;
            canvas.StrokeColor = Tokens.Border;
            canvas.StrokeDashPattern = null;
            canvas.DrawLine(x, 0f, x, h);

            canvas.FontColor = Tokens.Faint;
            canvas.FontSize = 9;
            canvas.DrawString($"{t:0}ms", x + 3f, 0f, 44f, h, HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        foreach (var m in Markers)
        {
            var x = (float)(m.T / Total) * w;
            canvas.StrokeColor = Tokens.Accent;
            canvas.StrokeDashPattern = new float[] { 2f, 2f };
            canvas.DrawLine(x, 0f, x, h);

            canvas.FontColor = Tokens.Accent;
            canvas.FontSize = 9;
            canvas.DrawString(m.Label, x + 3f, 0f, 44f, h, HorizontalAlignment.Left, VerticalAlignment.Center);
        }
        canvas.StrokeDashPattern = null;
    }
}

/// <summary>
/// The GC card's bar chart — recent GC events oldest→newest, bar height
/// normalised to the window max, majors in <c>--cat-gc</c> with a <c>!</c>
/// marker, minors in <c>--cat-css</c> (HANDOFF §5.4.3).
/// </summary>
public sealed class GcBarsDrawable : IDrawable
{
    public IReadOnlyList<GcEvent> Events { get; set; } = Array.Empty<GcEvent>();
    public ThemeTokens Tokens { get; set; } = ThemeTokens.Dark;
    public string? MonoFontFamily { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Events.Count == 0) return;
        float w = dirtyRect.Width, h = dirtyRect.Height;
        var max = Events.Max(e => e.Kb);
        if (max <= 0) return;

        const float gap = 6f;
        var barW = (w - gap * (Events.Count - 1)) / Events.Count;
        if (MonoFontFamily is not null) canvas.Font = new Microsoft.Maui.Graphics.Font(MonoFontFamily);

        for (var i = 0; i < Events.Count; i++)
        {
            var e = Events[i];
            var barH = (float)(e.Kb / max) * (h - 12f);
            var x = i * (barW + gap);
            var y = h - barH;

            canvas.FillColor = e.Major ? Tokens.CatGc : Tokens.CatCss;
            canvas.FillRoundedRectangle(x, y, barW, barH, 2f);

            if (e.Major)
            {
                canvas.FontColor = Tokens.CatGc;
                canvas.FontSize = 8;
                canvas.DrawString("!", x, y - 12f, barW, 10f,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}

/// <summary>
/// One IPC channel's message-rate sparkline — 24 bars, coloured by channel role
/// (HANDOFF §5.4.4). The bar heights are a deterministic synthetic series, the
/// same shape <c>devtools.jsx</c> generates.
/// </summary>
public sealed class SparklineDrawable : IDrawable
{
    public int Seed { get; set; }
    public Color BarColor { get; set; } = Colors.Gray;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float w = dirtyRect.Width, h = dirtyRect.Height;
        const int n = 24;
        const float gap = 1f;
        var barW = (w - gap * (n - 1)) / n;

        canvas.FillColor = BarColor.WithAlpha(0.7f);
        for (var j = 0; j < n; j++)
        {
            var frac = 0.20 + Math.Abs(Math.Sin(Seed * 7 + j * 0.6)) * 0.80;
            var barH = (float)frac * h;
            var x = j * (barW + gap);
            canvas.FillRoundedRectangle(x, h - barH, barW, barH, 1f);
        }
    }
}
