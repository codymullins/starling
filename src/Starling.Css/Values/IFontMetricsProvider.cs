namespace Tessera.Css.Values;

/// <summary>
/// Resolved per-font glyph metrics that <see cref="CssResolutionContext"/>
/// needs to compute <c>ex</c>, <c>cap</c>, <c>ch</c>, and <c>ic</c> units.
/// All values are in CSS pixels.
/// </summary>
public sealed record FontMetrics(
    double XHeightPx,
    double CapHeightPx,
    double ZeroAdvancePx,
    double IcAdvancePx);

/// <summary>
/// Provides glyph metrics for the cascaded font specification. The interface is
/// intentionally string-based so this module stays free of font/shape engine
/// dependencies (which live above Tessera.Css in the module graph).
/// </summary>
public interface IFontMetricsProvider
{
    FontMetrics Resolve(string fontFamily, double fontSizePx, string fontStyle, double fontWeight);
}

/// <summary>
/// Default <see cref="IFontMetricsProvider"/>. Derives metrics from the font
/// size using stable empirical ratios:
/// <list type="bullet">
/// <item>x-height ≈ 0.50 × font-size</item>
/// <item>cap-height ≈ 0.70 × font-size</item>
/// <item>'0' advance ≈ 0.50 × font-size (an English proportional approximation)</item>
/// <item>ideographic advance ≈ 1.00 × font-size</item>
/// </list>
/// These are close enough for layout to round-trip; a real measurer (Skia) can
/// replace the provider on the engine for higher fidelity.
/// </summary>
public sealed class HeuristicFontMetricsProvider : IFontMetricsProvider
{
    public FontMetrics Resolve(string fontFamily, double fontSizePx, string fontStyle, double fontWeight)
        => new(
            XHeightPx: fontSizePx * 0.5,
            CapHeightPx: fontSizePx * 0.7,
            ZeroAdvancePx: fontSizePx * 0.5,
            IcAdvancePx: fontSizePx);
}
