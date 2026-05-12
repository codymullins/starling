using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tessera.Css.Cascade;
using Tessera.Dom;
using Tessera.Layout.Text;
using Tessera.Paint.Backend;
using Tessera.Paint.DisplayList;
using LayoutEngineImpl = Tessera.Layout.LayoutEngine;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint;

/// <summary>
/// Paint façade. Pre-M1 callers used the <see cref="RenderText"/> path; the
/// full pipeline (parse → style → layout → display list → raster) lives on
/// <see cref="RenderDocument"/>.
/// </summary>
public sealed class Painter
{
    private readonly FontResolver _fonts;

    public Painter(FontResolver? fonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
    }

    /// <summary>
    /// Run the full M1 pipeline: build a box tree, lay it out, build a paint
    /// display list, replay it onto an ImageSharp surface. The caller supplies
    /// a parsed <see cref="Document"/> and the viewport size in CSS px.
    /// </summary>
    public Image<Rgba32> RenderDocument(Document document, LayoutSize viewport)
    {
        ArgumentNullException.ThrowIfNull(document);

        var style = new StyleEngine();
        var layoutEngine = new LayoutEngineImpl(style, DefaultTextMeasurer.Instance);
        var root = layoutEngine.LayoutDocument(document, viewport);

        PaintList displayList = new DisplayListBuilder().Build(root);
        var backend = new ImageSharpBackend(_fonts);
        return backend.Render(displayList, viewport);
    }

    /// <summary>Legacy M0 path: draw a fixed string onto a viewport-sized canvas.</summary>
    public Image<Rgba32> RenderHelloWorld(string text, Size viewport)
        => RenderText(text, viewport, fontSize: 32f);

    /// <summary>
    /// Renders <paramref name="text"/> in a sans-serif font near the top-left of
    /// a viewport-sized white canvas. Splits on '\n'; no word-wrap. Kept for
    /// the M0 headless smoke path; new callers should prefer <see cref="RenderDocument"/>.
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
