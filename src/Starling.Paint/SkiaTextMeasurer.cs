using System.Collections.Concurrent;
using Tessera.Layout.Text;
using Tessera.Skia.Handles;

namespace Tessera.Paint;

/// <summary>
/// <see cref="ITextMeasurer"/> backed by Skia: real shaped glyph advances from
/// <c>ts_shape_text</c> and real sized-font metrics from <c>ts_font_metrics</c>,
/// replacing <see cref="DefaultTextMeasurer"/>'s ~0.5em heuristic.
/// <para>
/// The typeface comes from <see cref="FontResolver"/> (bundled
/// <c>OpenSans-Regular.ttf</c> → system sans-serif). Sized <see cref="SkFont"/>
/// handles are cached per font size — font creation is the per-size cost;
/// shaping is per-call. The cache (and the resolver's typeface) are owned for
/// the process lifetime, mirroring how a browser keeps its font objects warm.
/// </para>
/// <para>
/// macOS/arm64-only for now: the native shim (<c>libtessera_skia</c>) ships
/// osx-arm64 only, so the first measurement call throws on other platforms.
/// Paint-free layout unit tests stay on <see cref="DefaultTextMeasurer"/>.
/// </para>
/// </summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    /// <summary>
    /// A single-character pen-position probe. <c>ts_shape_text</c> exposes glyph
    /// pen positions but not per-glyph advances, and the shim positions glyphs
    /// by accumulated advance with no contextual kerning (<c>SkFont::textToGlyphs</c>
    /// + <c>getWidths</c>). So the pen X of a sentinel appended after the run is
    /// exactly the run's total advance. 'x' is a plain Latin letter present in
    /// every reasonable sans-serif face.
    /// </summary>
    private const char AdvanceProbe = 'x';

    private readonly FontResolver _fonts;
    private readonly FontFaceRegistry? _webFonts;
    private readonly ConcurrentDictionary<FontCacheKey, SkFont> _fontCache = new();
    private bool _disposed;

    public SkiaTextMeasurer(FontResolver? fonts = null, FontFaceRegistry? webFonts = null)
    {
        _fonts = fonts ?? FontResolver.Default;
        _webFonts = webFonts;
    }

    public double MeasureWidth(string text, double fontSize, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || fontSize <= 0)
            return 0;

        var font = GetFont((float)fontSize, spec);

        // Shape `text + probe`: the probe glyph's pen X is the advance the run
        // before it consumed — i.e. the exact width of `text`.
        var glyphs = font.ShapeText(text + AdvanceProbe);
        if (glyphs.Length == 0)
            return 0;

        // The probe contributes its own trailing glyph; its X marks the end of
        // `text`. If the shaper collapsed something (it should not, for a plain
        // probe char) fall back to the last real glyph's X.
        var probeGlyph = glyphs[^1];
        return probeGlyph.X;
    }

    public double NormalLineHeight(double fontSize, FontSpec spec)
    {
        if (fontSize <= 0) return 0;
        var m = GetFont((float)fontSize, spec).Metrics();
        // CSS `line-height: normal` ≈ the font's natural line spacing:
        // ascent + descent + recommended leading.
        return m.Ascent + m.Descent + m.Leading;
    }

    public double Baseline(double fontSize, FontSpec spec)
    {
        if (fontSize <= 0) return 0;
        var m = GetFont((float)fontSize, spec).Metrics();
        // Distance from the top of the line box to the alphabetic baseline.
        // The line box adds half the leading above the ascent.
        return m.Ascent + (m.Leading / 2.0);
    }

    private SkFont GetFont(float size, FontSpec spec)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = new FontCacheKey(spec, size);
        return _fontCache.GetOrAdd(
            key,
            k => SkFont.Create(_fonts.GetTypeface(k.Spec, _webFonts), k.Size, k.Spec.Bold, k.Spec.Italic));
    }

    private readonly record struct FontCacheKey(FontSpec Spec, float Size);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var font in _fontCache.Values)
            font.Dispose();
        _fontCache.Clear();

        // Typefaces are owned by the FontResolver (it caches and reuses them),
        // so the measurer does not dispose them here.
    }
}
