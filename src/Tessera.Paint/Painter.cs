using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Tessera.Paint;

/// <summary>
/// M0 paint backend. One responsibility: take a string and a viewport size,
/// produce an <see cref="Image{TPixel}"/> with the text drawn near the top-left.
///
/// The full display-list pipeline per 08_FONTS_PAINT.md (DisplayList builder,
/// stacking-context order, brushes, clips, transforms) is M1+ work. This class
/// is the M0 entry point and disappears once Painter consumes a LayoutTree.
/// </summary>
public sealed class Painter
{
    private readonly FontResolver _fonts;

    public Painter(FontResolver? fonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
    }

    public Image<Rgba32> RenderHelloWorld(string text, Size viewport)
        => RenderText(text, viewport, fontSize: 32f);

    /// <summary>
    /// Renders <paramref name="text"/> in a sans-serif font near the top-left of
    /// a viewport-sized white canvas. Splits on '\n'; no word-wrap (that's M1).
    /// </summary>
    public Image<Rgba32> RenderText(string text, Size viewport, float fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));

        var font = _fonts.GetSansSerifFont(fontSize);

        var image = new Image<Rgba32>(viewport.Width, viewport.Height, new Rgba32(255, 255, 255, 255));
        const float Margin = 16f;
        var lineHeight = font.Size * 1.4f;

        image.Mutate(ctx =>
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n')
                            .Split('\n', StringSplitOptions.None);
            var y = Margin;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                    ctx.DrawText(line, font, Color.Black, new PointF(Margin, y));
                y += lineHeight;
            }
        });

        return image;
    }
}
