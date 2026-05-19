using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-color-4", "https://www.w3.org/TR/css-color-4/")]

public sealed class ColorFunctionTests
{
    private static CssValue ParseSingle(string source)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + source + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    private static CssColor Color(string css) => (CssColor)ParseSingle(css);

    // ----- legacy + modern rgb/rgba -----

    [Fact]
    public void Rgb_legacy_comma_syntax()
    {
        var c = Color("rgb(255, 0, 0)");
        c.Should().BeEquivalentTo(new { R = (byte)255, G = (byte)0, B = (byte)0, A = (byte)255 });
    }

    [Fact]
    public void Rgb_modern_whitespace_syntax()
    {
        var c = Color("rgb(255 128 0)");
        c.R.Should().Be(255); c.G.Should().Be(128); c.B.Should().Be(0); c.A.Should().Be(255);
    }

    [Fact]
    public void Rgb_with_alpha_slash()
    {
        var c = Color("rgb(255 0 0 / 0.5)");
        c.A.Should().BeInRange(126, 129);
    }

    [Fact]
    public void Rgba_legacy_works()
    {
        var c = Color("rgba(0, 255, 0, 0.5)");
        c.G.Should().Be(255);
        c.A.Should().BeInRange(126, 129);
    }

    [Fact]
    public void Rgb_with_percentages()
    {
        var c = Color("rgb(100% 0% 50%)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(128);
    }

    // ----- hsl / hsla / hwb -----

    [Fact]
    public void Hsl_pure_red()
    {
        var c = Color("hsl(0 100% 50%)");
        c.R.Should().Be(255); c.G.Should().Be(0); c.B.Should().Be(0);
    }

    [Fact]
    public void Hsl_with_turn_angle()
    {
        var c = Color("hsl(0.5turn 100% 50%)");
        // 180deg = cyan
        c.R.Should().Be(0); c.G.Should().Be(255); c.B.Should().Be(255);
    }

    [Fact]
    public void Hsla_with_alpha()
    {
        var c = Color("hsla(0, 100%, 50%, 0.5)");
        c.R.Should().Be(255);
        c.A.Should().BeInRange(126, 129);
    }

    [Fact]
    public void Hwb_white_through_white_component()
    {
        var c = Color("hwb(0 100% 0%)");
        c.R.Should().Be(255); c.G.Should().Be(255); c.B.Should().Be(255);
    }

    // ----- lab / lch / oklab / oklch -----

    [Fact]
    public void Lab_parses_and_resolves_to_srgb()
    {
        var c = Color("lab(50% 40 59.5 / .5)");
        c.Space.Should().Be(ColorSpace.Lab);
        c.A.Should().BeInRange(126, 129);
    }

    [Fact]
    public void Lch_parses()
    {
        var c = Color("lch(54% 106 41)");
        c.Space.Should().Be(ColorSpace.Lch);
    }

    [Fact]
    public void Oklab_parses_and_preserves_native_components()
    {
        var c = Color("oklab(0.59 0.1 0.1)");
        c.Space.Should().Be(ColorSpace.Oklab);
        c.C1.Should().BeApproximately(0.59, 0.0001);
    }

    [Fact]
    public void Oklch_parses_and_resolves_to_srgb()
    {
        var c = Color("oklch(0.7 0.15 50)");
        c.Space.Should().Be(ColorSpace.Oklch);
        c.C3.Should().BeApproximately(50.0, 0.0001);
    }

    // ----- color() function -----

    [Fact]
    public void Color_function_srgb()
    {
        var c = Color("color(srgb 1 0 0)");
        c.Space.Should().Be(ColorSpace.Srgb);
        c.R.Should().Be(255);
    }

    [Fact]
    public void Color_function_display_p3()
    {
        var c = Color("color(display-p3 1 0.5 0)");
        c.Space.Should().Be(ColorSpace.DisplayP3);
    }

    [Fact]
    public void Color_function_with_alpha()
    {
        var c = Color("color(srgb 1 0 0 / 0.5)");
        c.A.Should().BeInRange(126, 129);
    }

    // ----- color-mix -----

    [Fact]
    public void Color_mix_red_blue_in_oklch_50_50()
    {
        var c = Color("color-mix(in oklch, red, blue)");
        c.Should().BeOfType<CssColor>();
        c.Space.Should().Be(ColorSpace.Oklch);
    }

    [Fact]
    public void Color_mix_with_percentage()
    {
        var c = Color("color-mix(in oklch, red 50%, blue)");
        c.Space.Should().Be(ColorSpace.Oklch);
    }

    [Fact]
    public void Color_mix_in_srgb_returns_blend()
    {
        var c = Color("color-mix(in srgb, white, black)");
        // 50/50 of 255 and 0 → ~128 in each channel.
        c.R.Should().BeInRange(127, 129);
        c.G.Should().BeInRange(127, 129);
        c.B.Should().BeInRange(127, 129);
    }

    // ----- named colors -----

    [Fact]
    public void Named_color_rebeccapurple()
    {
        var c = Color("rebeccapurple");
        c.R.Should().Be(102); c.G.Should().Be(51); c.B.Should().Be(153);
    }

    [Fact]
    public void Named_color_transparent_resolves_to_zero_alpha()
    {
        var c = Color("transparent");
        c.A.Should().Be(0);
    }

    [Fact]
    public void Current_color_remains_a_keyword()
    {
        // currentColor isn't a color literal — it's an inherited keyword.
        var v = ParseSingle("currentColor");
        v.Should().BeOfType<CssKeyword>().Which.Name.Should().Be("currentcolor");
    }

    // ----- hex -----

    [Fact]
    public void Hex_3_digit()
    {
        var c = Color("#f00");
        c.R.Should().Be(255); c.G.Should().Be(0); c.B.Should().Be(0); c.A.Should().Be(255);
    }

    [Fact]
    public void Hex_4_digit_alpha()
    {
        var c = Color("#f008");
        c.R.Should().Be(255); c.A.Should().Be(0x88);
    }

    [Fact]
    public void Hex_6_digit()
    {
        var c = Color("#003366");
        c.R.Should().Be(0); c.G.Should().Be(51); c.B.Should().Be(102);
    }

    [Fact]
    public void Hex_8_digit_alpha()
    {
        var c = Color("#003366cc");
        c.A.Should().Be(0xCC);
    }

    // ----- none keyword -----

    [Fact]
    public void None_keyword_preserved_as_nan_component()
    {
        var c = Color("rgb(none 128 0)");
        // Channel was "none" — stored as NaN in C1 but resolved to 0 in R.
        c.R.Should().Be(0);
        double.IsNaN(c.C1).Should().BeTrue();
    }

    // ----- ToSrgb -----

    [Fact]
    public void ToSrgb_returns_byte_equivalent()
    {
        var c = Color("rgb(10 20 30)");
        var s = c.ToSrgb();
        s.R.Should().Be(10); s.G.Should().Be(20); s.B.Should().Be(30);
    }

    [Fact]
    public void Oklch_resolves_to_reasonable_sRGB_for_paint()
    {
        // oklch(0.7 0.15 50) ≈ a warm orange/tan.
        var c = Color("oklch(0.7 0.15 50)");
        c.R.Should().BeGreaterThan(c.B);
    }
}
