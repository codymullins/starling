using System.Collections.Concurrent;
using SixLabors.Fonts;
using Starling.Layout.Text;

namespace Starling.Paint;

/// <summary>
/// <see cref="ITextMeasurer"/> backed by SixLabors.Fonts 3.x: real shaped glyph
/// advances and sized font metrics via <see cref="TextMeasurer"/>. Used by the
/// ImageSharp paint backend, which is now the only backend after the Skia shim
/// was removed.
/// <para>
/// Typefaces are loaded from the same embedded TTF/OTF resources
/// <see cref="FontResolver"/> uses (bundled OpenSans plus anything else
/// stamped into <c>Resources/Fonts/</c>). SixLabors.Fonts owns no native
/// resources, so disposal is bookkeeping only — but we still flag and guard
/// re-entry so misuse trips loudly instead of returning stale measurements.
/// </para>
/// </summary>
public sealed class ImageSharpTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly FontResolver _fonts;
    private readonly FontFaceRegistry? _webFonts;
    private readonly FontCollection _collection;
    private readonly ConcurrentDictionary<FontCacheKey, Font> _fontCache = new();
    private bool _disposed;

    public ImageSharpTextMeasurer(FontResolver? fonts = null, FontFaceRegistry? webFonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
        _webFonts = webFonts;
        _collection = ImageSharpFontLookup.LoadCollection(webFonts);
    }

    public double MeasureWidth(string text, double fontSize, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || fontSize <= 0) return 0d;
        var font = GetFont((float)fontSize, spec);
        var advance = TextMeasurer.MeasureAdvance(text, new TextOptions(font));
        return advance.Width;
    }

    /// <summary>
    /// Returns the run's advance with an empty <see cref="ShapedRun.Glyphs"/>
    /// array — the documented "let the paint backend re-shape" signal (see
    /// <see cref="ITextMeasurer.Shape"/>'s contract and
    /// <see cref="DefaultTextMeasurer.Shape"/>). SixLabors.Fonts 3 does not
    /// expose OpenType glyph indices on its public surface
    /// (<c>FontMetrics.TryGetGlyphId</c> is internal; <c>GlyphMetrics</c>
    /// carries only the codepoint), so we can't populate
    /// <see cref="ShapedGlyph.GlyphId"/> correctly. The ImageSharp paint
    /// backend re-renders by string anyway, so leaving the glyph array empty
    /// is the correct signal — no caller currently consumes it.
    /// </summary>
    public ShapedRun Shape(string text, double fontSize, FontSpec spec)
        => new(Array.Empty<ShapedGlyph>(), MeasureWidth(text, fontSize, spec));

    public double NormalLineHeight(double fontSize, FontSpec spec)
    {
        if (fontSize <= 0) return 0;
        var font = GetFont((float)fontSize, spec);
        var (ascent, descent, leading) = ScaledMetrics(font);
        return ascent + descent + leading;
    }

    public double Baseline(double fontSize, FontSpec spec)
    {
        if (fontSize <= 0) return 0;
        var font = GetFont((float)fontSize, spec);
        var (ascent, _, leading) = ScaledMetrics(font);
        return ascent + (leading / 2.0);
    }

    // SixLabors.Fonts exposes ascender/descender/line-gap in font design units
    // on HorizontalMetrics; scale by (fontSize / UnitsPerEm) to get CSS px.
    // Descender is signed (negative below baseline in OpenType) — abs it so
    // it composes additively with ascender.
    private static (double Ascent, double Descent, double Leading) ScaledMetrics(Font font)
    {
        var fm = font.FontMetrics;
        var h = fm.HorizontalMetrics;
        var scale = font.Size / fm.UnitsPerEm;
        var ascent = h.Ascender * scale;
        var descent = Math.Abs(h.Descender) * scale;
        var leading = h.LineGap * scale;
        return (ascent, descent, leading);
    }

    private Font GetFont(float size, FontSpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = new FontCacheKey(spec, size);
        return _fontCache.GetOrAdd(key, static (k, self) => self.CreateFont(k.Spec, k.Size), this);
    }

    private Font CreateFont(FontSpec spec, float size)
        => ImageSharpFontLookup.CreateFont(_collection, spec, size);

    private readonly record struct FontCacheKey(FontSpec Spec, float Size);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fontCache.Clear();
    }
}
