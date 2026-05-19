namespace Starling.Css.Spec.Tests.CssColor5;

/// <summary>
/// <see href="https://www.w3.org/TR/css-color-5/#relative-colors">CSS Color L5 §4</see>:
/// Relative color syntax — <c>rgb(from &lt;color&gt; r g b)</c> etc.
/// </summary>
[Spec("css-color-5", "https://www.w3.org/TR/css-color-5/", section: "4")]
public sealed class RelativeColorSyntaxTests
{
    [PendingFact("relative color syntax parser not implemented",
                 trackingWp: "wp:spec-css-color-5-relative")]
    public void Rgb_from_resolves_channels_with_literal_replacements()
    {
        // rgb(from #336699 0 g b) → rgb(0, 102, 153)
        throw new NotImplementedException();
    }

    [PendingFact("relative color syntax with calc() not implemented",
                 trackingWp: "wp:spec-css-color-5-relative")]
    public void Rgb_from_allows_calc_on_extracted_channels()
    {
        // rgb(from red calc(r / 2) g b) → rgb(127.5, 0, 0)
        throw new NotImplementedException();
    }

    [PendingFact("relative color syntax in oklch space not implemented",
                 trackingWp: "wp:spec-css-color-5-relative")]
    public void Oklch_from_can_reuse_lightness_only()
    {
        // oklch(from var(--brand) l 0 0) → desaturated luminance-matched gray
        throw new NotImplementedException();
    }
}
