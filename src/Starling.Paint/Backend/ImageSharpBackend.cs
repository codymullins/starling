#if TESSERA_IMAGESHARP_DRAWING
using System.Collections.Concurrent;
using System.Diagnostics;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Css.Values;
using Tessera.Layout.Text;
using LayoutRect = Tessera.Layout.Rect;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint.Backend;

/// <summary>
/// Cross-platform paint backend that replays a <see cref="DisplayList"/> through
/// ImageSharp.Drawing 3.0's canvas API. Compiled in only when the
/// <c>EnableImageSharpDrawing3</c> MSBuild flag is set (which also defines
/// <c>TESSERA_IMAGESHARP_DRAWING</c>); the default build path remains Skia.
/// </summary>
/// <remarks>
/// <para>
/// The starting canvas is filled opaque white to match
/// <see cref="SkiaGraphiteBackend"/>, so a per-pixel diff between the two
/// backends stays tight on background regions.
/// </para>
/// <para>
/// Text path: ImageSharp.Drawing 3.0 does not expose a public way to paint a
/// caller-supplied (pre-shaped) glyph run — all text APIs route through the
/// internal <see cref="TextRenderer"/>, which re-shapes via SixLabors.Fonts.
/// So the backend ignores <c>DrawText.Shaped</c> and re-shapes via
/// <see cref="RichTextOptions"/>. Goldens between Skia and ImageSharp will
/// diverge in glyph positioning at sub-pixel resolution; cross-backend tests
/// must compare via SSIM rather than pixel identity.
/// </para>
/// <para>
/// Fonts: <see cref="FontResolver"/> only exposes Skia handles, not raw bytes.
/// To avoid changing the resolver, the backend loads every <c>*.ttf</c>/<c>*.otf</c>
/// embedded resource on <c>Starling.Paint.dll</c> into a private
/// <see cref="FontCollection"/> once at construction. Family lookup walks the
/// <see cref="FontSpec.Families"/> list against that collection and falls back
/// to the first registered family (the bundled OpenSans) when nothing matches.
/// </para>
/// </remarks>
internal sealed class ImageSharpBackend : IPaintBackend
{
    private readonly FontResolver _fonts;
    private readonly FontFaceRegistry? _webFonts;
    private readonly IDiagnostics _diag;
    private readonly FontCollection _fontCollection;
    private readonly ConcurrentDictionary<FontCacheKey, Font> _fontCache = new();

    public ImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
        _webFonts = webFonts;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _fontCollection = LoadEmbeddedFontCollection();
    }

    public string Name => "imagesharp";

    public RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0.0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        Activity.Current?.SetTag("raster.width", width);
        Activity.Current?.SetTag("raster.height", height);
        Activity.Current?.SetTag("raster.scale", scale);
        Activity.Current?.SetTag("raster.items", list.Items.Count);

        using (_diag.Span("paint", "raster.context_init"))
        {
            // ImageSharp has no persistent context; the span keeps the
            // trace shape identical to SkiaGraphiteBackend for diffability.
        }

        Image<Rgba32> image;
        using (_diag.Span("paint", "raster.surface_alloc"))
            image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));

        try
        {
            using (_diag.Span("paint", "raster.command_record"))
            {
                image.Mutate(x => x.Paint(canvas =>
                {
                    if (scale != 1.0f) canvas.Scale(scale);
                    foreach (var item in list.Items)
                        Apply(canvas, item, scale);
                }));
            }

            byte[] pixels;
            using (_diag.Span("paint", "raster.readback"))
            {
                pixels = new byte[checked(width * height * 4)];
                image.CopyPixelDataTo(pixels);
            }

            return new RenderedBitmap(width, height, pixels);
        }
        finally
        {
            image.Dispose();
        }
    }

    private void Apply(IDrawingCanvas canvas, DisplayList.DisplayItem item, float scale)
    {
        switch (item)
        {
            case DisplayList.FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rect", 1);
                {
                    var r = SnapRect(fill.Bounds, scale);
                    canvas.Fill(Brushes.Solid(ToColor(fill.Color)), new RectangleF(r.X, r.Y, r.Width, r.Height));
                }
                break;
            case DisplayList.StrokeRect stroke:
                _diag.Counter("paint.stroke_rect", 1);
                {
                    var r = SnapRect(stroke.Bounds, scale);
                    canvas.Draw(Pens.Solid(ToColor(stroke.Color), (float)stroke.Width),
                        new RectangleF(r.X, r.Y, r.Width, r.Height));
                }
                break;
            case DisplayList.DrawText text:
                _diag.Counter("paint.draw_text", 1);
                DrawText(canvas, text, scale);
                break;
            case DisplayList.DrawImage img:
                _diag.Counter("paint.draw_image", 1);
                DrawImage(canvas, img);
                break;
        }
    }

    private void DrawText(IDrawingCanvas canvas, DisplayList.DrawText text, float scale)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var spec = FontSpecFromDrawText(text);
        var size = (float)text.FontSize;
        var probe = FirstCodepoint(text.Text);
        var font = ResolveFont(spec, probe, size);
        var color = ToColor(text.Color);

        var baselineY = SnapToPixel((float)text.Y, scale);

        // No pre-shaped path: ImageSharp.Drawing 3.0 does not accept
        // caller-shaped glyph runs through its public canvas surface, so the
        // text.Shaped fast path is intentionally bypassed. See the class
        // doc-comment for why and what this means for cross-backend diffs.
        if (text.Shaped is { Glyphs.Length: > 0 })
            _diag.Counter("paint.draw_text.reshaped", 1);
        else
            _diag.Counter("paint.draw_text.unshaped", 1);

        var options = new RichTextOptions(font)
        {
            Origin = new PointF((float)text.X, baselineY),
            TextAlignment = TextAlignment.Start,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        canvas.DrawText(options, text.Text, Brushes.Solid(color), pen: null);
    }

    private static void DrawImage(IDrawingCanvas canvas, DisplayList.DrawImage item)
    {
        if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0) return;
        var decoded = item.Source;
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return;

        // ImageSharp's LoadPixelData<TPixel> wants a ReadOnlySpan<byte> of the
        // exact byte length; DecodedImage's buffer is straight-alpha RGBA8888
        // which matches Rgba32's layout precisely.
        using var src = Image.LoadPixelData<Rgba32>(decoded.Pixels.Span, decoded.Width, decoded.Height);
        var dest = new RectangleF(
            (float)item.Bounds.X,
            (float)item.Bounds.Y,
            (float)item.Bounds.Width,
            (float)item.Bounds.Height);
        canvas.DrawImage(src, dest);
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size, int Probe);

    private static FontSpec FontSpecFromDrawText(DisplayList.DrawText text)
        => new(text.FontFamilies, text.Bold, text.Italic);

    private static int FirstCodepoint(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]))
            return char.ConvertToUtf32(text[0], text[1]);
        return text[0];
    }

    private Font ResolveFont(FontSpec spec, int probeCodepoint, float size)
        => _fontCache.GetOrAdd(new FontCacheKey(spec, size, probeCodepoint), k => CreateFont(k.Spec, k.Size));

    private Font CreateFont(FontSpec spec, float size)
    {
        var style = (spec.Bold, spec.Italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular,
        };

        foreach (var family in spec.Families)
        {
            if (_fontCollection.TryGet(family, out var match))
                return match.CreateFont(size, style);
        }

        // Fallback: any family present in the embedded collection (bundled OpenSans).
        foreach (var family in _fontCollection.Families)
            return family.CreateFont(size, style);

        throw new InvalidOperationException(
            "ImageSharpBackend: no fonts available. Ensure Starling.Paint.dll bundles at least one TTF/OTF embedded resource.");
    }

    private static FontCollection LoadEmbeddedFontCollection()
    {
        var collection = new FontCollection();
        var asm = typeof(ImageSharpBackend).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            try
            {
                collection.Add(stream);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Skip unreadable font; FontCollection.Add throws on malformed data.
            }
        }
        return collection;
    }

    private static SixLabors.ImageSharp.RectangleF SnapRect(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (float)(Math.Round(r.X * s) / s);
        var y0 = (float)(Math.Round(r.Y * s) / s);
        var x1 = (float)(Math.Round((r.X + r.Width) * s) / s);
        var y1 = (float)(Math.Round((r.Y + r.Height) * s) / s);
        return new SixLabors.ImageSharp.RectangleF(x0, y0, x1 - x0, y1 - y0);
    }

    private static float SnapToPixel(float value, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        return MathF.Round(value * s) / s;
    }

    private static Color ToColor(CssColor c)
        => Color.FromRgba(c.R, c.G, c.B, c.A);

    public void Dispose()
    {
        // Font is a value-ish handle; FontCollection has no Dispose. Clear
        // the cache so a long-lived host releases the per-spec entries.
        _fontCache.Clear();
        _ = _fonts;
        _ = _webFonts;
    }
}
#endif
