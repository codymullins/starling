using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Css.Values;
using Tessera.Layout.Text;
using Tessera.Skia.Handles;
using Tessera.Skia.Interop;
using LayoutRect = Tessera.Layout.Rect;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint.Backend;

/// <summary>
/// Replays a <see cref="DisplayList"/> onto a Skia Graphite (Dawn) offscreen
/// surface via the <c>Tessera.Skia</c> interop handles, then reads the pixels
/// back into a renderer-neutral <see cref="RenderedBitmap"/>. This is the
/// engine's sole rasterizer (see <see cref="Painter"/>) — there is no managed
/// fallback.
/// </summary>
/// <remarks>
/// The native shim (<c>libtessera_skia</c>) is a hard requirement and currently
/// ships osx-arm64 only — constructing this backend on a platform without the
/// shim throws a clear <see cref="DllNotFoundException"/> from the first native call.
/// <para>
/// The <see cref="SkContext"/> (Dawn instance/adapter/device + Graphite
/// context) is created once per backend instance and reused across every
/// <see cref="Render"/> call — context creation is the expensive step;
/// surfaces are cheap and per-render. The context and the font typeface are
/// disposed with the backend.
/// </para>
/// </remarks>
public sealed class SkiaGraphiteBackend : IDisposable
{
    // The DisplayList carries colors as straight sRGBA; the shim's TsColor is
    // the same 8-bit straight sRGBA shape, so the conversion is a field copy.

    private readonly Lazy<SkContext> _context;
    private readonly FontResolver _fonts;
    private readonly FontFaceRegistry? _webFonts;
    private readonly IDiagnostics _diag;
    private readonly ConcurrentDictionary<FontCacheKey, SkFont> _fontCache = new();

    public SkiaGraphiteBackend()
        : this(FontResolver.Default, webFonts: null)
    {
    }

    public SkiaGraphiteBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        _fonts = fonts;
        _webFonts = webFonts;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        // Defer the native context creation until the first render so merely
        // constructing the backend (e.g. for the flag selector on a platform
        // without the dylib) does not touch the shim.
        _context = new Lazy<SkContext>(() => SkContext.Create());
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

    private SkFont ResolveFont(FontSpec spec, int probeCodepoint, float size)
        => _fontCache.GetOrAdd(
            new FontCacheKey(spec, size, probeCodepoint),
            k => SkFont.Create(_fonts.GetTypeface(k.Spec, _webFonts, k.Probe == 0 ? null : k.Probe), k.Size, k.Spec.Bold, k.Spec.Italic));

    public RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0.0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        // The display list is in logical (CSS) pixels; the backing buffer is
        // sized in physical pixels (logical × density). A canvas-side scale
        // transform bridges the two so coordinates downstream stay unchanged.
        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        NativeCallTrace.Mark("render.begin", $"{width}x{height} scale={scale} items={list.Items.Count}");

        Activity.Current?.SetTag("raster.width", width);
        Activity.Current?.SetTag("raster.height", height);
        Activity.Current?.SetTag("raster.scale", scale);
        Activity.Current?.SetTag("raster.items", list.Items.Count);

        SkContext context;
        using (_diag.Span("paint", "raster.context_init"))
            context = _context.Value;

        SkSurface surface;
        using (_diag.Span("paint", "raster.surface_alloc"))
            surface = SkSurface.Create(context, width, height);

        try
        {
            var canvas = surface.GetCanvas();

            // ImageSharpBackend starts from an opaque white canvas; match that so
            // the two backends are diffable.
            canvas.Clear(new TsColor(255, 255, 255, 255));

            if (scale != 1.0f)
                canvas.Scale(scale, scale);

            using (_diag.Span("paint", "raster.command_record"))
            {
                foreach (var item in list.Items)
                    Apply(canvas, item, scale);
            }
            using (_diag.Span("paint", "raster.flush"))
                surface.Flush(context);

            byte[] pixels;
            using (_diag.Span("paint", "raster.readback"))
                pixels = surface.ReadPixels(context, width, height);

            NativeCallTrace.Mark("render.end");
            return new RenderedBitmap(width, height, pixels);
        }
        finally
        {
            surface.Dispose();
        }
    }

    private void Apply(SkCanvas canvas, DisplayList.DisplayItem item, float scale)
    {
        switch (item)
        {
            case DisplayList.FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                _diag.Counter("paint.fill_rect", 1);
                canvas.FillRect(SnapRect(fill.Bounds, scale), ToColor(fill.Color));
                break;
            case DisplayList.StrokeRect stroke:
                _diag.Counter("paint.stroke_rect", 1);
                canvas.StrokeRect(SnapRect(stroke.Bounds, scale), ToColor(stroke.Color), (float)stroke.Width);
                break;
            case DisplayList.DrawText text:
                _diag.Counter("paint.draw_text", 1);
                DrawText(canvas, text, scale);
                break;
            case DisplayList.DrawImage image:
                _diag.Counter("paint.draw_image", 1);
                DrawImage(canvas, image);
                break;
        }
    }

    private void DrawText(SkCanvas canvas, DisplayList.DrawText text, float scale)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        var spec = FontSpecFromDrawText(text);
        var color = ToColor(text.Color);
        var size = (float)text.FontSize;
        var pen = (float)text.X;
        // Snap the baseline to a device-pixel row so every glyph in the run
        // shares the same integer Y at the rasterizer — without this, fractional
        // baselines blur ascenders/descenders across two rows. Horizontal pen
        // stays subpixel; setSubpixel(true) on the font lets Skia hint glyphs at
        // fractional X for smooth advances.
        var baselineY = SnapToPixel((float)text.Y, scale);

        // Fast path: glyphs were already shaped at layout time and travel on
        // the DrawText item. Translate them by the fragment origin and hand
        // them straight to the canvas — no PartitionByFace, no ShapeText, no
        // MeasureRunEnd reshape. ShapedGlyph and TsGlyph share sequential
        // layout, so the cast to TsGlyph is zero-copy.
        if (text.Shaped is { Glyphs.Length: > 0 } shaped)
        {
            _diag.Counter("paint.draw_text.shaped", 1);
            var font = ResolveFont(spec, probeCodepoint: 0, size);
            var glyphs = new ShapedGlyph[shaped.Glyphs.Length];
            for (var i = 0; i < glyphs.Length; i++)
            {
                var g = shaped.Glyphs[i];
                glyphs[i] = new ShapedGlyph(g.GlyphId, g.X + pen, g.Y + baselineY);
            }
            canvas.DrawText(font, MemoryMarshal.Cast<ShapedGlyph, TsGlyph>(glyphs), color);
            return;
        }

        _diag.Counter("paint.draw_text.unshaped", 1);

        // Fallback (used only when layout supplied no shape — heuristic
        // measurer in tests, or future codepaths). To honour unicode-range
        // subset web fonts, partition the text into sub-runs by face affinity
        // and shape each on its own face. This breaks ligatures across face
        // boundaries (intentional — a Latin/Cyrillic switch has no shared
        // ligatures anyway).
        foreach (var (start, length, font) in PartitionByFace(text.Text, spec, size))
        {
            var sub = text.Text.Substring(start, length);
            var glyphs = font.ShapeText(sub);
            if (glyphs.Length == 0) continue;

            for (var i = 0; i < glyphs.Length; i++)
            {
                glyphs[i].X += pen;
                glyphs[i].Y += baselineY;
            }
            canvas.DrawText(font, glyphs, color);

            // Advance the pen by the sub-run width. ShapeText doesn't return
            // the trailing advance; the conventional trick is to append a
            // sentinel and read its pen position.
            pen = MeasureRunEnd(font, sub, pen);
        }
    }

    private static float MeasureRunEnd(SkFont font, string text, float startPen)
    {
        // Re-shape with a trailing ASCII sentinel. The sentinel's pen X is the
        // exact end of the input run — matches SkiaTextMeasurer's probe trick.
        var glyphs = font.ShapeText(text + "x");
        if (glyphs.Length < 1) return startPen;
        return startPen + glyphs[^1].X;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into maximal contiguous spans where
    /// every codepoint resolves to the same typeface. The first family in
    /// <paramref name="spec"/> whose face's <c>unicode-range</c> covers the
    /// codepoint wins (system fallback families have no range and therefore
    /// match anything).
    /// </summary>
    private IEnumerable<(int Start, int Length, SkFont Font)> PartitionByFace(string text, FontSpec spec, float size)
    {
        var i = 0;
        while (i < text.Length)
        {
            int cp;
            int width;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                width = 2;
            }
            else
            {
                cp = text[i];
                width = 1;
            }

            var font = ResolveFont(spec, cp, size);
            var start = i;
            i += width;

            // Extend the run while the next codepoint resolves to the same font.
            while (i < text.Length)
            {
                int nextCp;
                int nextWidth;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    nextCp = char.ConvertToUtf32(text[i], text[i + 1]);
                    nextWidth = 2;
                }
                else
                {
                    nextCp = text[i];
                    nextWidth = 1;
                }
                var nextFont = ResolveFont(spec, nextCp, size);
                if (!ReferenceEquals(nextFont, font)) break;
                i += nextWidth;
            }

            yield return (start, i - start, font);
        }
    }

    private static void DrawImage(SkCanvas canvas, DisplayList.DrawImage item)
    {
        if (item.Bounds.Width <= 0 || item.Bounds.Height <= 0) return;
        var decoded = item.Source;
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return;

        // ts_canvas_draw_image takes raw tightly-packed RGBA8888 pixels and
        // scales them into the destination rect — no intermediate handle, and
        // the DecodedImage buffer stays immutable and reusable across <img>s.
        canvas.DrawImage(decoded.Pixels.Span, decoded.Width, decoded.Height, ToRect(item.Bounds));
    }

    private static TsRect ToRect(LayoutRect r)
        => new((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height);

    /// <summary>
    /// Rounds a rect's edges to the device-pixel grid so abutting boxes share
    /// pixel boundaries (no half-pixel gaps, no anti-aliased blurry borders).
    /// Snapping is done in the post-scale space then divided back to the
    /// canvas's logical units so the canvas's scale transform can drop the
    /// coords directly onto integer device pixels.
    /// </summary>
    private static TsRect SnapRect(LayoutRect r, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        var x0 = (float)(Math.Round(r.X * s) / s);
        var y0 = (float)(Math.Round(r.Y * s) / s);
        var x1 = (float)(Math.Round((r.X + r.Width) * s) / s);
        var y1 = (float)(Math.Round((r.Y + r.Height) * s) / s);
        return new TsRect(x0, y0, x1 - x0, y1 - y0);
    }

    private static float SnapToPixel(float value, float scale)
    {
        var s = scale > 0 ? scale : 1f;
        return MathF.Round(value * s) / s;
    }

    private static TsColor ToColor(CssColor c)
        => new(c.R, c.G, c.B, c.A);

    public void Dispose()
    {
        foreach (var font in _fontCache.Values) font.Dispose();
        _fontCache.Clear();
        if (_context.IsValueCreated) _context.Value.Dispose();
    }
}
