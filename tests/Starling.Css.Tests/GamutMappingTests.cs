using FluentAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-color-4", "https://www.w3.org/TR/css-color-4/")]

[TestClass]
public class GamutMappingTests
{
    private static CssColor ParseColor(string text)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + text + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return (CssColor)CssValueParser.Parse(decl.Value);
    }

    [TestMethod]
    public void Srgb_in_gamut_passes_through_unchanged()
    {
        // pure sRGB red as oklch components → in-gamut after conversion
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Srgb, 1.0, 0.0, 0.0);
        r.Should().BeApproximately(1.0, 0.001);
        g.Should().BeApproximately(0.0, 0.001);
        b.Should().BeApproximately(0.0, 0.001);
    }

    [TestMethod]
    public void Srgb_grey_passes_through_unchanged()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Srgb, 0.5, 0.5, 0.5);
        r.Should().BeApproximately(0.5, 0.001);
        g.Should().BeApproximately(0.5, 0.001);
        b.Should().BeApproximately(0.5, 0.001);
    }

    [TestMethod]
    public void DisplayP3_red_is_out_of_sRGB_gamut_and_gets_mapped()
    {
        // color(display-p3 1 0 0) is brighter/more saturated than sRGB red.
        var c = ParseColor("color(display-p3 1 0 0)");
        c.HasWideGamutData.Should().BeTrue();
        var srgb = c.ToSrgb();
        // The result must be a valid sRGB color (each component in [0, 255]).
        srgb.R.Should().BeInRange(200, 255);
        srgb.G.Should().BeLessThan(50);
        srgb.B.Should().BeLessThan(50);
    }

    [TestMethod]
    public void Oklch_excessive_chroma_is_chroma_reduced()
    {
        // oklch(0.7 0.4 30) — chroma 0.4 is far beyond sRGB gamut for this hue.
        var c = ParseColor("oklch(0.7 0.4 30)");
        c.HasWideGamutData.Should().BeTrue();
        var srgb = c.ToSrgb();
        // Naive clamp would give (255, 0, 0). Chroma reduction should preserve
        // lightness better — green/blue components should be non-zero.
        (srgb.G > 0 || srgb.B > 0).Should().BeTrue();
    }

    [TestMethod]
    public void Oklch_max_lightness_returns_white()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Oklch, 1.0, 0.4, 30.0);
        // Spec: L ≥ 100% → white.
        r.Should().BeApproximately(1.0, 0.01);
        g.Should().BeApproximately(1.0, 0.01);
        b.Should().BeApproximately(1.0, 0.01);
    }

    [TestMethod]
    public void Oklch_zero_lightness_returns_black()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Oklch, 0.0, 0.2, 30.0);
        r.Should().BeApproximately(0.0, 0.01);
        g.Should().BeApproximately(0.0, 0.01);
        b.Should().BeApproximately(0.0, 0.01);
    }

    [TestMethod]
    public void Mapped_color_preserves_hue_better_than_naive_clamp()
    {
        // oklch(0.6 0.3 250) → out-of-gamut blue. Mapped to sRGB, hue should
        // still be in the blue region.
        var c = ParseColor("oklch(0.6 0.3 250)");
        var srgb = c.ToSrgb();
        // Blue should dominate.
        srgb.B.Should().BeGreaterThan(srgb.R);
        srgb.B.Should().BeGreaterThan(srgb.G);
    }

    [TestMethod]
    public void Rec2020_pure_red_is_mapped_to_valid_sRGB()
    {
        var c = ParseColor("color(rec2020 1 0 0)");
        c.HasWideGamutData.Should().BeTrue();
        var srgb = c.ToSrgb();
        // Must be a valid sRGB color — bytes in range, alpha intact.
        srgb.A.Should().Be(255);
        srgb.R.Should().BeInRange(200, 255);
    }
}
