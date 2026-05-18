#if TESSERA_IMAGESHARP_DRAWING
using System.Collections.Concurrent;
using SixLabors.Fonts;
using Tessera.Layout.Text;

namespace Tessera.Paint;

/// <summary>
/// <see cref="ITextMeasurer"/> backed by SixLabors.Fonts 3.x: real shaped glyph
/// advances and sized font metrics via <see cref="TextMeasurer"/>, parallel to
/// <see cref="SkiaTextMeasurer"/> but on the managed ImageSharp.Drawing path.
/// Selected at runtime when <c>TESSERA_PAINT_BACKEND=imagesharp</c>.
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
    private readonly FontCollection _collection = new();
    private readonly Dictionary<string, FontFamily> _bundledFamilies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FontCacheKey, Font> _fontCache = new();
    private FontFamily? _fallbackFamily;
    private bool _disposed;

    public ImageSharpTextMeasurer(FontResolver? fonts = null, FontFaceRegistry? webFonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
        _webFonts = webFonts;
        LoadBundledFonts();
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
    /// Shape <paramref name="text"/> via SixLabors.Fonts 3's single-pass
    /// <see cref="TextMeasurer.GetGlyphMetrics"/> — each entry's
    /// <c>Advance</c> rectangle carries the positioned pen coordinates in
    /// pixel units, so we read <c>Advance.X/Y</c> straight into
    /// <see cref="ShapedGlyph"/>. GlyphId is set to the codepoint: the 3.x API
    /// does not surface OpenType glyph indices, and the ImageSharp paint
    /// backend re-renders by string anyway, so the codepoint is the only
    /// identifier worth round-tripping.
    /// </summary>
    public ShapedRun Shape(string text, double fontSize, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || fontSize <= 0)
            return new ShapedRun(Array.Empty<ShapedGlyph>(), 0d);

        var font = GetFont((float)fontSize, spec);
        var options = new TextOptions(font);
        var metrics = TextMeasurer.GetGlyphMetrics(text, options).Span;
        if (metrics.Length == 0)
            return new ShapedRun(Array.Empty<ShapedGlyph>(), 0d);

        var glyphs = new ShapedGlyph[metrics.Length];
        for (var i = 0; i < metrics.Length; i++)
        {
            var m = metrics[i];
            glyphs[i] = new ShapedGlyph((uint)m.CodePoint.Value, m.Advance.X, m.Advance.Y);
        }

        var advance = TextMeasurer.MeasureAdvance(text, options).Width;
        return new ShapedRun(glyphs, advance);
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
    // it composes additively with ascender, matching Skia's m.Descent.
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
            if (_bundledFamilies.TryGetValue(family, out var fam))
                return fam.CreateFont(size, style);
        }

        var fallback = _fallbackFamily
            ?? throw new InvalidOperationException(
                "No SixLabors.Fonts family available. The bundled OpenSans-Regular.ttf " +
                "failed to load from Starling.Paint's embedded resources.");
        return fallback.CreateFont(size, style);
    }

    private void LoadBundledFonts()
    {
        var asm = typeof(ImageSharpTextMeasurer).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            try
            {
                var family = _collection.Add(stream);
                _bundledFamilies[family.Name] = family;
                _fallbackFamily ??= family;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Skip unreadable resources; the fallback chain still has a chance.
            }
        }
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fontCache.Clear();
        _bundledFamilies.Clear();
        _fallbackFamily = null;
    }
}
#endif
