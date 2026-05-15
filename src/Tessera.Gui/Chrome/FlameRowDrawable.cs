using Microsoft.Maui.Graphics;
using Tessera.Gui.Theme;

namespace Tessera.Gui.Chrome;

/// <summary>
/// Draws one row of timing bars positioned proportionally across a total
/// duration — the C# port of <c>FlameRow</c> in <c>design/devtools.jsx</c>.
/// Shared by the DevTools Performance panel (tall, labelled bars) and the
/// URL-bar <see cref="MiniLoadChart"/> (short, unlabelled, with a live cursor),
/// configured via the public properties.
/// </summary>
public sealed class FlameRowDrawable : IDrawable
{
    public IReadOnlyList<TimingBar> Bars { get; set; } = Array.Empty<TimingBar>();
    public double Total { get; set; } = 1;
    public ThemeTokens Tokens { get; set; } = ThemeTokens.Dark;

    /// <summary>Draw each bar's label when it's wide enough (HANDOFF §5.2: &gt;4%).</summary>
    public bool ShowLabels { get; set; }

    /// <summary>Draw the 1px wall-clock cursor at <see cref="CursorFraction"/>.</summary>
    public bool ShowCursor { get; set; }
    public double CursorFraction { get; set; }

    public float CornerRadius { get; set; } = 2f;
    public float BarOpacity { get; set; } = 1f;
    public string? MonoFontFamily { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Total <= 0) return;
        float w = dirtyRect.Width, h = dirtyRect.Height;

        foreach (var bar in Bars)
        {
            var x = (float)(bar.T / Total) * w;
            var bw = (float)(bar.D / Total) * w;
            if (bw < 0.5f) continue;

            canvas.FillColor = Tokens[bar.Cat].WithAlpha(BarOpacity);
            canvas.FillRoundedRectangle(x, 0, bw, h, CornerRadius);

            // Inner hairline so adjacent same-hue bars still read as separate
            // (HANDOFF §5.2's inset shadow).
            canvas.StrokeColor = Colors.Black.WithAlpha(0.25f);
            canvas.StrokeSize = 1;
            canvas.DrawRoundedRectangle(x + 0.5f, 0.5f, bw - 1f, h - 1f, CornerRadius);

            if (ShowLabels && !string.IsNullOrEmpty(bar.Label) && bw > w * 0.04f)
            {
                canvas.FontColor = Tokens.BarInk;
                canvas.FontSize = 10;
                if (MonoFontFamily is not null)
                    canvas.Font = new Microsoft.Maui.Graphics.Font(MonoFontFamily);
                canvas.DrawString(
                    bar.Label, x + 4f, 0f, Math.Max(0f, bw - 8f), h,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        if (ShowCursor)
        {
            var cx = (float)Math.Clamp(CursorFraction, 0, 1) * w;
            canvas.StrokeColor = Tokens.Text.WithAlpha(0.7f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(cx, -2f, cx, h + 2f);
        }
    }
}
