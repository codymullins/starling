using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Css.Values;
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
    private const string DefaultSansSerifFamily = "sans-serif";

    private readonly Lazy<SkContext> _context;
    private readonly Lazy<SkTypeface> _typeface;

    public SkiaGraphiteBackend()
    {
        // Defer the native context + typeface creation until the first render
        // so merely constructing the backend (e.g. for the flag selector on a
        // platform without the dylib) does not touch the shim.
        _context = new Lazy<SkContext>(() => SkContext.Create());
        _typeface = new Lazy<SkTypeface>(() => SkTypeface.FromName(DefaultSansSerifFamily));
    }

    public RenderedBitmap Render(PaintList list, LayoutSize viewport)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));

        var width = (int)Math.Ceiling(viewport.Width);
        var height = (int)Math.Ceiling(viewport.Height);

        NativeCallTrace.Mark("render.begin", $"{width}x{height} items={list.Items.Count}");

        var context = _context.Value;
        using var surface = SkSurface.Create(context, width, height);
        var canvas = surface.GetCanvas();

        // ImageSharpBackend starts from an opaque white canvas; match that so
        // the two backends are diffable.
        canvas.Clear(new TsColor(255, 255, 255, 255));

        foreach (var item in list.Items)
            Apply(canvas, item);

        surface.Flush(context);
        var pixels = surface.ReadPixels(context, width, height);
        NativeCallTrace.Mark("render.end");
        return new RenderedBitmap(width, height, pixels);
    }

    private void Apply(SkCanvas canvas, DisplayList.DisplayItem item)
    {
        switch (item)
        {
            case DisplayList.FillRect fill:
                if (fill.Bounds.Width <= 0 || fill.Bounds.Height <= 0) return;
                canvas.FillRect(ToRect(fill.Bounds), ToColor(fill.Color));
                break;
            case DisplayList.StrokeRect stroke:
                canvas.StrokeRect(ToRect(stroke.Bounds), ToColor(stroke.Color), (float)stroke.Width);
                break;
            case DisplayList.DrawText text:
                DrawText(canvas, text);
                break;
            case DisplayList.DrawImage image:
                DrawImage(canvas, image);
                break;
        }
    }

    private void DrawText(SkCanvas canvas, DisplayList.DrawText text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;

        // The shim does SkFont::textToGlyphs shaping (LTR). ts_shape_text
        // returns glyph pen positions relative to the run origin (0,0); the
        // display-list X/Y is the baseline origin, so translate every glyph by
        // it before handing the run to ts_canvas_draw_text.
        using var font = SkFont.Create(_typeface.Value, (float)text.FontSize);
        var glyphs = font.ShapeText(text.Text);
        if (glyphs.Length == 0) return;

        var originX = (float)text.X;
        var originY = (float)text.Y;
        for (var i = 0; i < glyphs.Length; i++)
        {
            glyphs[i].X += originX;
            glyphs[i].Y += originY;
        }

        canvas.DrawText(font, glyphs, ToColor(text.Color));
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

    private static TsColor ToColor(CssColor c)
        => new(c.R, c.G, c.B, c.A);

    public void Dispose()
    {
        if (_typeface.IsValueCreated) _typeface.Value.Dispose();
        if (_context.IsValueCreated) _context.Value.Dispose();
    }
}
