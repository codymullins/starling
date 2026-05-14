using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tessera.Css.Values;
using Tessera.Paint.DisplayList;
using LayoutRect = Tessera.Layout.Rect;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint.Backend;

/// <summary>
/// Replays a <see cref="DisplayList"/> onto an ImageSharp surface. v1 supports
/// solid-fill rects and rasterized text via <see cref="FontResolver"/>.
/// </summary>
public sealed class ImageSharpBackend
{
    private readonly FontResolver _fonts;

    public ImageSharpBackend(FontResolver? fonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
    }

    public Image<Rgba32> Render(PaintList list, LayoutSize viewport)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));

        var width = (int)Math.Ceiling(viewport.Width);
        var height = (int)Math.Ceiling(viewport.Height);
        var image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));

        image.Mutate(ctx =>
        {
            foreach (var item in list.Items)
                Apply(ctx, item);
        });

        return image;
    }

    private void Apply(IImageProcessingContext ctx, DisplayItem item)
    {
        switch (item)
        {
            case FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                ctx.Fill(ToColor(fill.Color), ToPath(fill.Bounds));
                break;
            case StrokeRect stroke:
                ctx.Draw(ToColor(stroke.Color), (float)stroke.Width, ToPath(stroke.Bounds));
                break;
            case DrawText text:
                DrawText(ctx, text);
                break;
            case DisplayList.DrawImage image:
                DrawImage(ctx, image);
                break;
        }
    }

    private static void DrawImage(IImageProcessingContext ctx, DisplayList.DrawImage item)
    {
        if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0) return;
        var decoded = item.Source;
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return;

        var targetW = (int)Math.Round(item.Bounds.Width);
        var targetH = (int)Math.Round(item.Bounds.Height);
        if (targetW <= 0 || targetH <= 0) return;

        // ImageSharp stays the rasterizer: wrap the backend-neutral straight
        // RGBA8888 pixels back into an Image<Rgba32> for the blit. LoadPixelData
        // copies, so the DecodedImage's buffer stays immutable and reusable
        // across multiple <img> elements.
        using var source = Image.LoadPixelData<Rgba32>(
            decoded.Pixels.Span, decoded.Width, decoded.Height);

        // Resample only when the destination differs from the source's native
        // size.
        Image<Rgba32>? resampled = null;
        try
        {
            Image<Rgba32> drawable = source;
            if (source.Width != targetW || source.Height != targetH)
            {
                resampled = source.Clone(c => c.Resize(targetW, targetH));
                drawable = resampled;
            }

            ctx.DrawImage(
                drawable,
                new Point((int)Math.Round(item.Bounds.X), (int)Math.Round(item.Bounds.Y)),
                opacity: 1f);
        }
        finally
        {
            resampled?.Dispose();
        }
    }

    private static RectangularPolygon ToPath(LayoutRect r)
        => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

    private void DrawText(IImageProcessingContext ctx, DrawText text)
    {
        // v1 keeps the font face simple — we always draw with the resolver's
        // sans-serif face; bold/italic variant selection lands when we wire
        // FontFamily resolution into the resolver (M5+).
        var font = _fonts.GetSansSerifFont((float)text.FontSize);
        var color = ToColor(text.Color);
        // ImageSharp draws text from the top-left of the typographic bounding
        // box; back off by the baseline-to-top distance so display-list Y
        // (which is the baseline) lines up.
        var point = new PointF((float)text.X, (float)(text.Y - font.Size * 0.8f));
        ctx.DrawText(text.Text, font, color, point);
    }

    private static Color ToColor(CssColor c)
        => Color.FromRgba(c.R, c.G, c.B, c.A);
}
