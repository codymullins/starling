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
    // Per-measurer shape cache, mirroring the cache the original
    // SkiaTextMeasurer carried (commit 499ce3d). SixLabors.Fonts shapes content-
    // only — no contextual kerning across run boundaries — so the same
    // (text, size, spec) deterministically produces the same glyph advances
    // relative to (0,0). ShapedRun is immutable, so the same instance can
    // safely back many TextFragments. Article-class pages repeat common tokens
    // ("the", " ", ",", digits) hundreds of times; without the cache, every
    // InlineLayout.LayoutInlineRun call re-shapes the run and then re-shapes
    // each word inside it.
    private readonly ConcurrentDictionary<ShapeCacheKey, ShapedRun> _shapeCache = new();
    private bool _disposed;

    private readonly record struct ShapeCacheKey(string Text, float Size, FontSpec Spec);

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
        return Shape(text, fontSize, spec).Advance;
    }

    /// <summary>
    /// Shape <paramref name="text"/> once and cache the result. Returns the
    /// per-glyph pen positions plus the total advance.
    /// <para>
    /// SixLabors.Fonts 3 does not expose OpenType glyph indices on its public
    /// <see cref="GlyphMetrics"/> surface (only the codepoint), so
    /// <see cref="ShapedGlyph.GlyphId"/> carries the codepoint as a stand-in.
    /// The ImageSharp paint backend re-renders by string and never reads the
    /// glyph id, so this substitution is safe; the positions are what matter,
    /// because <c>InlineLayout</c> uses them to slice a whole-run shape
    /// into per-word <c>TextFragment</c>s without re-shaping each word.
    /// </para>
    /// <para>
    /// When the metrics count matches <c>text.Length</c> (the ASCII / 1:1
    /// case) the slice optimisation in <c>InlineLayout</c> re-engages.
    /// For ligatures, surrogate pairs, or combining marks the counts differ
    /// and InlineLayout falls back to per-word shaping — still cached here
    /// by (word, size, spec), so the regression cost is bounded.
    /// </para>
    /// </summary>
    public ShapedRun Shape(string text, double fontSize, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || fontSize <= 0)
            return new ShapedRun(Array.Empty<ShapedGlyph>(), 0d);

        var size = (float)fontSize;
        var key = new ShapeCacheKey(text, size, spec);
        if (_shapeCache.TryGetValue(key, out var hit))
            return hit;

        var font = GetFont(size, spec);
        var options = new TextOptions(font);
        var advance = TextMeasurer.MeasureAdvance(text, options);
        var metrics = TextMeasurer.GetGlyphMetrics(text, options).Span;

        var glyphs = new ShapedGlyph[metrics.Length];
        var pen = 0f;
        for (var i = 0; i < metrics.Length; i++)
        {
            var gm = metrics[i];
            glyphs[i] = new ShapedGlyph((uint)gm.CodePoint.Value, pen, 0f);
            pen += gm.Advance.Width;
        }

        var shaped = new ShapedRun(glyphs, advance.Width);
        _shapeCache[key] = shaped;
        return shaped;
    }

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
        _shapeCache.Clear();
    }
}
