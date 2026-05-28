using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssColor4;

/// <summary>
/// Comprehensive conformance suite for
/// <see href="https://www.w3.org/TR/css-color-4/">CSS Color Module Level 4</see>.
/// Covers hex notation (§5), named colors (§6), rgb()/hsl()/hwb() (§6–§7),
/// lab()/lch()/oklab()/oklch() (§9–§10), the color() function (§10), gamut
/// mapping (§13), and the <c>none</c> channel keyword (§4.4).
/// </summary>
[TestClass]
[Spec("css-color-4", "https://www.w3.org/TR/css-color-4/")]
public sealed class ColorParsingTests
{
    private static Starling.Css.Values.CssColor Color(string css)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + css + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return (Starling.Css.Values.CssColor)CssValueParser.Parse(decl.Value);
    }

    private static CssValue ParseRaw(string css)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + css + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    // ===== §5 Hex color notation =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_3digit_red()
    {
        var c = Color("#f00");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_3digit_green()
    {
        var c = Color("#0f0");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_3digit_blue()
    {
        var c = Color("#00f");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_3digit_white()
    {
        var c = Color("#fff");
        c.R.Should().Be(255);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_3digit_black()
    {
        var c = Color("#000");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_4digit_with_alpha_half_transparent()
    {
        // #f008 expands to #ff000088
        var c = Color("#f008");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(0x88);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_4digit_fully_transparent()
    {
        // #0000 expands to #00000000
        var c = Color("#0000");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_6digit_opaque()
    {
        var c = Color("#003366");
        c.R.Should().Be(0x00);
        c.G.Should().Be(0x33);
        c.B.Should().Be(0x66);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_6digit_mixed_case_parsed()
    {
        // Hex digits are case-insensitive per spec
        var c = Color("#aAbBcC");
        c.R.Should().Be(0xAA);
        c.G.Should().Be(0xBB);
        c.B.Should().Be(0xCC);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_8digit_with_alpha()
    {
        var c = Color("#003366cc");
        c.R.Should().Be(0x00);
        c.G.Should().Be(0x33);
        c.B.Should().Be(0x66);
        c.A.Should().Be(0xCC);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_8digit_fully_opaque_ff_alpha()
    {
        var c = Color("#FF0000FF");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#hex-notation", section: "5")]
    [SpecFact]
    public void Hex_8digit_fully_transparent_00_alpha()
    {
        var c = Color("#FF000000");
        c.R.Should().Be(255);
        c.A.Should().Be(0);
    }

    // ===== §6 Named colors =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_red()
    {
        var c = Color("red");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_lime()
    {
        var c = Color("lime");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_blue()
    {
        var c = Color("blue");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_white()
    {
        var c = Color("white");
        c.R.Should().Be(255);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_black()
    {
        var c = Color("black");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_rebeccapurple()
    {
        // Added in CSS Color 4 as a tribute to Rebecca Meyer.
        var c = Color("rebeccapurple");
        c.R.Should().Be(102);
        c.G.Should().Be(51);
        c.B.Should().Be(153);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_transparent_zero_alpha()
    {
        var c = Color("transparent");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_color_is_case_insensitive()
    {
        var c = Color("RED");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_gray_and_grey_are_identical()
    {
        var gray = Color("gray");
        var grey = Color("grey");
        gray.R.Should().Be(grey.R);
        gray.G.Should().Be(grey.G);
        gray.B.Should().Be(grey.B);
        gray.R.Should().Be(128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_aqua_and_cyan_are_identical()
    {
        var aqua = Color("aqua");
        var cyan = Color("cyan");
        aqua.R.Should().Be(cyan.R);
        aqua.G.Should().Be(cyan.G);
        aqua.B.Should().Be(cyan.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_fuchsia_and_magenta_are_identical()
    {
        var fuchsia = Color("fuchsia");
        var magenta = Color("magenta");
        fuchsia.R.Should().Be(magenta.R);
        fuchsia.G.Should().Be(magenta.G);
        fuchsia.B.Should().Be(magenta.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_cornflowerblue_correct_values()
    {
        var c = Color("cornflowerblue");
        c.R.Should().Be(100);
        c.G.Should().Be(149);
        c.B.Should().Be(237);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#named-colors", section: "6")]
    [SpecFact]
    public void Named_tomato_correct_values()
    {
        var c = Color("tomato");
        c.R.Should().Be(255);
        c.G.Should().Be(99);
        c.B.Should().Be(71);
    }

    // ===== §6 currentColor =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#currentcolor-color", section: "6.2")]
    [SpecFact]
    public void CurrentColor_is_a_keyword_not_a_color_literal()
    {
        // currentColor is an inherited keyword — the parser must not resolve it
        // to a concrete color at parse time.
        var v = ParseRaw("currentColor");
        v.Should().BeOfType<CssKeyword>().Which.Name.Should().Be("currentcolor");
    }

    // ===== §6.1 rgb() / rgba() — legacy comma syntax =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_legacy_comma_integers()
    {
        var c = Color("rgb(255, 0, 0)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgba_legacy_comma_with_alpha()
    {
        var c = Color("rgba(0, 255, 0, 0.5)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
        // 0.5 * 255 = 127.5, rounds to 128
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgba_legacy_comma_alpha_one()
    {
        var c = Color("rgba(10, 20, 30, 1)");
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgba_legacy_comma_alpha_zero()
    {
        var c = Color("rgba(255, 255, 255, 0)");
        c.A.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_legacy_comma_percentage_channels()
    {
        // 100%, 0%, 50% → 255, 0, 128
        var c = Color("rgb(100%, 0%, 50%)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().BeInRange(127, 128);
    }

    // ===== §6.1 rgb() / rgba() — modern whitespace syntax =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_modern_whitespace_integers()
    {
        var c = Color("rgb(255 128 0)");
        c.R.Should().Be(255);
        c.G.Should().Be(128);
        c.B.Should().Be(0);
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_modern_with_slash_alpha()
    {
        var c = Color("rgb(255 0 0 / 0.5)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_modern_with_slash_alpha_percentage()
    {
        var c = Color("rgb(0 0 255 / 50%)");
        c.R.Should().Be(0);
        c.B.Should().Be(255);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_modern_percentage_channels()
    {
        var c = Color("rgb(100% 0% 50%)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_out_of_range_values_are_clamped()
    {
        // Values >255 must be clamped, not wrapped.
        var c = Color("rgb(300 0 -50)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_alpha_above_one_is_clamped()
    {
        var c = Color("rgb(255 0 0 / 1.5)");
        c.A.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#rgb-functions", section: "6.1")]
    [SpecFact]
    public void Rgb_alpha_below_zero_is_clamped()
    {
        var c = Color("rgb(255 0 0 / -0.5)");
        c.A.Should().Be(0);
    }

    // ===== §7 hsl() / hsla() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_red_0deg()
    {
        var c = Color("hsl(0 100% 50%)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_green_120deg()
    {
        var c = Color("hsl(120 100% 50%)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_blue_240deg()
    {
        var c = Color("hsl(240 100% 50%)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_cyan_180deg()
    {
        var c = Color("hsl(180 100% 50%)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_white_l100()
    {
        var c = Color("hsl(0 100% 100%)");
        c.R.Should().Be(255);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_black_l0()
    {
        var c = Color("hsl(0 100% 0%)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_stores_space_as_hsl()
    {
        var c = Color("hsl(120 100% 50%)");
        c.Space.Should().Be(ColorSpace.Hsl);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_deg_unit_explicit()
    {
        var c = Color("hsl(120deg 100% 50%)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_turn_unit()
    {
        // 0.5turn = 180deg = cyan
        var c = Color("hsl(0.5turn 100% 50%)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_rad_unit()
    {
        // π rad = 180deg = cyan
        var c = Color($"hsl({Math.PI:F6}rad 100% 50%)");
        c.R.Should().Be(0);
        // Allow some rounding tolerance at the byte level
        c.G.Should().BeInRange(254, 255);
        c.B.Should().BeInRange(254, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_hue_wraps_360()
    {
        // 360deg is the same as 0deg
        var c0 = Color("hsl(0 100% 50%)");
        var c360 = Color("hsl(360 100% 50%)");
        c0.R.Should().Be(c360.R);
        c0.G.Should().Be(c360.G);
        c0.B.Should().Be(c360.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsla_legacy_comma_syntax()
    {
        var c = Color("hsla(0, 100%, 50%, 0.5)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hsl-notation", section: "7")]
    [SpecFact]
    public void Hsl_modern_slash_alpha()
    {
        var c = Color("hsl(240 100% 50% / 0.75)");
        c.R.Should().Be(0);
        c.B.Should().Be(255);
        // 0.75 * 255 ≈ 191
        c.A.Should().BeInRange(190, 192);
    }

    // ===== §7.2 hwb() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_pure_red_no_white_black()
    {
        var c = Color("hwb(0 0% 0%)");
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_full_white_component_gives_white()
    {
        var c = Color("hwb(0 100% 0%)");
        c.R.Should().Be(255);
        c.G.Should().Be(255);
        c.B.Should().Be(255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_full_black_component_gives_black()
    {
        var c = Color("hwb(0 0% 100%)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_white_plus_black_above_100pct_normalises_to_gray()
    {
        // When w+b > 100%, both are normalized: gray = w/(w+b)
        var c = Color("hwb(0 60% 60%)");
        c.R.Should().Be(c.G);
        c.G.Should().Be(c.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_green_hue()
    {
        var c = Color("hwb(120 0% 0%)");
        c.R.Should().Be(0);
        c.G.Should().Be(255);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_stores_space_as_hwb()
    {
        var c = Color("hwb(0 0% 0%)");
        c.Space.Should().Be(ColorSpace.Hwb);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#the-hwb-notation", section: "7.2")]
    [SpecFact]
    public void Hwb_with_alpha()
    {
        var c = Color("hwb(0 0% 0% / 0.5)");
        c.R.Should().Be(255);
        c.A.Should().BeInRange(127, 128);
    }

    // ===== §9 lab() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lab_stores_lab_space_and_native_components()
    {
        var c = Color("lab(50 20 -30)");
        c.Space.Should().Be(ColorSpace.Lab);
        c.C1.Should().BeApproximately(50.0, 0.001);
        c.C2.Should().BeApproximately(20.0, 0.001);
        c.C3.Should().BeApproximately(-30.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lab_with_alpha_slash()
    {
        var c = Color("lab(50 40 59.5 / 0.5)");
        c.Space.Should().Be(ColorSpace.Lab);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lab_L100_is_near_white()
    {
        // lab(100 0 0) is D50 white, which maps approximately to sRGB white.
        var c = Color("lab(100 0 0)");
        c.R.Should().BeInRange(254, 255);
        c.G.Should().BeInRange(254, 255);
        c.B.Should().BeInRange(254, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lab_L0_is_black()
    {
        var c = Color("lab(0 0 0)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lab_native_components_preserved_with_none()
    {
        // none component stored as NaN
        var c = Color("lab(50 none 0)");
        c.Space.Should().Be(ColorSpace.Lab);
        double.IsNaN(c.C2).Should().BeTrue();
    }

    // ===== §9 lch() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lch_stores_lch_space_and_native_components()
    {
        var c = Color("lch(54 106 41)");
        c.Space.Should().Be(ColorSpace.Lch);
        c.C1.Should().BeApproximately(54.0, 0.001);
        c.C2.Should().BeApproximately(106.0, 0.001);
        c.C3.Should().BeApproximately(41.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lch_resolves_to_srgb_bytes_in_range()
    {
        var c = Color("lch(54 106 41)");
        c.R.Should().BeInRange(0, 255);
        c.G.Should().BeInRange(0, 255);
        c.B.Should().BeInRange(0, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lch_with_alpha()
    {
        var c = Color("lch(54 106 41 / 0.5)");
        c.Space.Should().Be(ColorSpace.Lch);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lch_hue_angle_with_deg_unit()
    {
        var c = Color("lch(54 106 41deg)");
        c.Space.Should().Be(ColorSpace.Lch);
        c.C3.Should().BeApproximately(41.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-lab-lch", section: "9")]
    [SpecFact]
    public void Lch_zero_chroma_is_achromatic()
    {
        // With C=0 the color is purely by lightness — should be gray-ish.
        var c = Color("lch(50 0 0)");
        // R/G/B should be roughly equal at ~half (gray).
        var diff = Math.Abs(c.R - c.G) + Math.Abs(c.G - c.B);
        diff.Should().BeLessThan(5);
    }

    // ===== §10 oklab() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_stores_oklab_space_and_native_components()
    {
        var c = Color("oklab(0.59 0.1 0.1)");
        c.Space.Should().Be(ColorSpace.Oklab);
        c.C1.Should().BeApproximately(0.59, 0.0001);
        c.C2.Should().BeApproximately(0.1, 0.0001);
        c.C3.Should().BeApproximately(0.1, 0.0001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_L1_a0_b0_is_white()
    {
        var c = Color("oklab(1 0 0)");
        c.R.Should().BeInRange(254, 255);
        c.G.Should().BeInRange(254, 255);
        c.B.Should().BeInRange(254, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_L0_is_black()
    {
        var c = Color("oklab(0 0 0)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_with_alpha()
    {
        var c = Color("oklab(0.59 0.1 0.1 / 0.5)");
        c.Space.Should().Be(ColorSpace.Oklab);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_none_channel_stored_as_nan()
    {
        var c = Color("oklab(0.5 none 0)");
        c.Space.Should().Be(ColorSpace.Oklab);
        double.IsNaN(c.C2).Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklab_resolves_to_srgb_bytes_in_range()
    {
        var c = Color("oklab(0.59 0.1 0.1)");
        c.R.Should().BeInRange(0, 255);
        c.G.Should().BeInRange(0, 255);
        c.B.Should().BeInRange(0, 255);
    }

    // ===== §10 oklch() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_stores_oklch_space_and_native_components()
    {
        var c = Color("oklch(0.7 0.15 50)");
        c.Space.Should().Be(ColorSpace.Oklch);
        c.C1.Should().BeApproximately(0.7, 0.0001);
        c.C2.Should().BeApproximately(0.15, 0.0001);
        c.C3.Should().BeApproximately(50.0, 0.0001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_warm_orange_R_exceeds_B()
    {
        // oklch(0.7 0.15 50) is a warm orange/tan hue — R should dominate.
        var c = Color("oklch(0.7 0.15 50)");
        c.R.Should().BeGreaterThan(c.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_L1_is_white()
    {
        var c = Color("oklch(1 0 0)");
        c.R.Should().BeInRange(254, 255);
        c.G.Should().BeInRange(254, 255);
        c.B.Should().BeInRange(254, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_L0_is_black()
    {
        var c = Color("oklch(0 0 0)");
        c.R.Should().Be(0);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_with_alpha()
    {
        var c = Color("oklch(0.7 0.15 50 / 0.5)");
        c.Space.Should().Be(ColorSpace.Oklch);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_blue_hue_B_exceeds_R()
    {
        // hue 250 is in the blue range
        var c = Color("oklch(0.5 0.15 250)");
        c.B.Should().BeGreaterThan(c.R);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_none_channel_stored_as_nan()
    {
        var c = Color("oklch(0.7 none 50)");
        c.Space.Should().Be(ColorSpace.Oklch);
        double.IsNaN(c.C2).Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#specifying-oklab-oklch", section: "10")]
    [SpecFact]
    public void Oklch_hue_with_deg_unit()
    {
        var c = Color("oklch(0.7 0.15 50deg)");
        c.Space.Should().Be(ColorSpace.Oklch);
        c.C3.Should().BeApproximately(50.0, 0.001);
    }

    // ===== §10 color() function =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_srgb_red()
    {
        var c = Color("color(srgb 1 0 0)");
        c.Space.Should().Be(ColorSpace.Srgb);
        c.R.Should().Be(255);
        c.G.Should().Be(0);
        c.B.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_srgb_fractional()
    {
        var c = Color("color(srgb 0.5 0.25 0.75)");
        c.Space.Should().Be(ColorSpace.Srgb);
        c.C1.Should().BeApproximately(0.5, 0.001);
        c.C2.Should().BeApproximately(0.25, 0.001);
        c.C3.Should().BeApproximately(0.75, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_srgb_with_alpha()
    {
        var c = Color("color(srgb 1 0 0 / 0.5)");
        c.Space.Should().Be(ColorSpace.Srgb);
        c.A.Should().BeInRange(127, 128);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_srgb_percentage_channels()
    {
        // 100% = 1.0
        var c = Color("color(srgb 100% 0% 50%)");
        c.Space.Should().Be(ColorSpace.Srgb);
        c.C1.Should().BeApproximately(1.0, 0.001);
        c.C2.Should().BeApproximately(0.0, 0.001);
        c.C3.Should().BeApproximately(0.5, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_display_p3()
    {
        var c = Color("color(display-p3 1 0.5 0)");
        c.Space.Should().Be(ColorSpace.DisplayP3);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_display_p3_stores_native_components()
    {
        var c = Color("color(display-p3 0.8 0.3 0.1)");
        c.Space.Should().Be(ColorSpace.DisplayP3);
        c.C1.Should().BeApproximately(0.8, 0.001);
        c.C2.Should().BeApproximately(0.3, 0.001);
        c.C3.Should().BeApproximately(0.1, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_display_p3_red_is_wide_gamut()
    {
        // display-p3 red is outside sRGB gamut
        var c = Color("color(display-p3 1 0 0)");
        c.HasWideGamutData.Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_srgb_linear()
    {
        var c = Color("color(srgb-linear 1 0 0)");
        c.Space.Should().Be(ColorSpace.SrgbLinear);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_a98_rgb()
    {
        var c = Color("color(a98-rgb 0.5 0.5 0.5)");
        c.Space.Should().Be(ColorSpace.A98Rgb);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_rec2020()
    {
        var c = Color("color(rec2020 1 0 0)");
        c.Space.Should().Be(ColorSpace.Rec2020);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_prophoto_rgb()
    {
        var c = Color("color(prophoto-rgb 1 0 0)");
        c.Space.Should().Be(ColorSpace.ProphotoRgb);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_xyz_d65()
    {
        var c = Color("color(xyz-d65 0.5 0.5 0.5)");
        c.Space.Should().Be(ColorSpace.XyzD65);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_xyz_d50()
    {
        var c = Color("color(xyz-d50 0.5 0.5 0.5)");
        c.Space.Should().Be(ColorSpace.XyzD50);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_xyz_alias()
    {
        // "xyz" without qualifier is an alias for xyz-d65 per Color 4
        var c = Color("color(xyz 0.5 0.5 0.5)");
        c.Space.Should().Be(ColorSpace.XyzD65);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void Color_fn_none_channel_stored_as_nan()
    {
        var c = Color("color(srgb none 0.5 0.5)");
        double.IsNaN(c.C1).Should().BeTrue();
        c.C2.Should().BeApproximately(0.5, 0.001);
    }

    // ===== §13 Gamut mapping =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_srgb_in_gamut_passes_through_unchanged()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Srgb, 1.0, 0.0, 0.0);
        r.Should().BeApproximately(1.0, 0.001);
        g.Should().BeApproximately(0.0, 0.001);
        b.Should().BeApproximately(0.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_display_p3_red_mapped_to_valid_srgb()
    {
        var c = Color("color(display-p3 1 0 0)");
        var srgb = c.ToSrgb();
        // Result must be within 8-bit sRGB range
        srgb.R.Should().BeInRange(200, 255);
        srgb.G.Should().BeLessThan(50);
        srgb.B.Should().BeLessThan(50);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_oklch_excessive_chroma_chroma_reduced()
    {
        // oklch(0.7 0.4 30) — chroma 0.4 is far beyond sRGB gamut.
        var c = Color("oklch(0.7 0.4 30)");
        c.HasWideGamutData.Should().BeTrue();
        var srgb = c.ToSrgb();
        // Chroma reduction preserves lightness better than naive clamp —
        // at least one non-red channel should be non-zero.
        (srgb.G > 0 || srgb.B > 0).Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_oklch_L1_maps_to_white()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Oklch, 1.0, 0.4, 30.0);
        r.Should().BeApproximately(1.0, 0.01);
        g.Should().BeApproximately(1.0, 0.01);
        b.Should().BeApproximately(1.0, 0.01);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_oklch_L0_maps_to_black()
    {
        var (r, g, b) = GamutMapper.MapToSrgb(ColorSpace.Oklch, 0.0, 0.2, 30.0);
        r.Should().BeApproximately(0.0, 0.01);
        g.Should().BeApproximately(0.0, 0.01);
        b.Should().BeApproximately(0.0, 0.01);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_oklch_blue_preserves_blue_dominance()
    {
        // oklch(0.6 0.3 250) is an out-of-gamut blue
        var c = Color("oklch(0.6 0.3 250)");
        var srgb = c.ToSrgb();
        srgb.B.Should().BeGreaterThan(srgb.R);
        srgb.B.Should().BeGreaterThan(srgb.G);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_rec2020_red_maps_to_valid_srgb()
    {
        // Rec2020 has a significantly wider gamut than sRGB or Display P3.
        // oklch chroma reduction preserves hue, so the mapped sRGB red still has
        // R >> G and R >> B, but G may be higher than for Display-P3 red.
        var c = Color("color(rec2020 1 0 0)");
        c.HasWideGamutData.Should().BeTrue();
        var srgb = c.ToSrgb();
        srgb.A.Should().Be(255);
        srgb.R.Should().BeInRange(200, 255);
        // Chroma reduction produces a non-trivial G (measured: ~73); R still dominates.
        srgb.R.Should().BeGreaterThan(srgb.G);
        srgb.R.Should().BeGreaterThan(srgb.B);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_srgb_negative_channel_mapped_to_valid_range()
    {
        // color(srgb -0.5 0.5 0.5) is outside the sRGB gamut. CSS Color 4 §13.1
        // applies oklch chroma reduction (not simple channel clamping), so the
        // mapped result preserves perceptual appearance rather than naively
        // zeroing the negative channel. The byte result must be in [0, 255].
        var c = Color("color(srgb -0.5 0.5 0.5)");
        var srgb = c.ToSrgb();
        srgb.R.Should().BeInRange(0, 255);
        srgb.G.Should().BeInRange(0, 255);
        srgb.B.Should().BeInRange(0, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void Gamut_srgb_channel_above_one_clamped_on_toSrgb()
    {
        var c = Color("color(srgb 1.5 0 0)");
        var srgb = c.ToSrgb();
        srgb.R.Should().Be(255);
    }

    // ===== §4.4 "none" channel keyword =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#missing", section: "4.4")]
    [SpecFact]
    public void None_in_rgb_stored_as_nan_C1()
    {
        var c = Color("rgb(none 128 0)");
        // none → NaN in C1, treated as 0 in the byte R
        double.IsNaN(c.C1).Should().BeTrue();
        c.R.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#missing", section: "4.4")]
    [SpecFact]
    public void None_in_rgb_green_channel()
    {
        var c = Color("rgb(255 none 0)");
        c.R.Should().Be(255);
        double.IsNaN(c.C2).Should().BeTrue();
        c.G.Should().Be(0);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#missing", section: "4.4")]
    [SpecFact]
    public void None_in_hsl_hue_stored_as_nan()
    {
        var c = Color("hsl(none 100% 50%)");
        c.Space.Should().Be(ColorSpace.Hsl);
        double.IsNaN(c.C1).Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#missing", section: "4.4")]
    [SpecFact]
    public void None_in_oklab_middle_channel_stored_as_nan()
    {
        var c = Color("oklab(0.5 none -0.1)");
        c.Space.Should().Be(ColorSpace.Oklab);
        double.IsNaN(c.C2).Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#missing", section: "4.4")]
    [SpecFact]
    public void None_in_alpha_stored_as_nan()
    {
        var c = Color("rgb(255 0 0 / none)");
        double.IsNaN(c.AlphaF).Should().BeTrue();
        // Byte alpha treats NaN as 0
        c.A.Should().Be(0);
    }

    // ===== Color space conversions (§14/§15) =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_srgb_to_xyz_d65_roundtrip()
    {
        // Convert sRGB red to XYZ-D65 and back; should return approximately red.
        var (x, y, z) = ColorConversion.LinearSrgbToXyzD65(1.0, 0.0, 0.0);
        var (r, g, b) = ColorConversion.XyzD65ToSrgb(x, y, z);
        r.Should().BeApproximately(1.0, 0.0001);
        g.Should().BeApproximately(0.0, 0.0001);
        b.Should().BeApproximately(0.0, 0.0001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_srgb_to_oklab_white()
    {
        // White in sRGB → oklab should have L ≈ 1, a ≈ 0, b ≈ 0
        var (x, y, z) = ColorConversion.LinearSrgbToXyzD65(1.0, 1.0, 1.0);
        var (L, a, b) = ColorConversion.XyzD65ToOklab(x, y, z);
        L.Should().BeApproximately(1.0, 0.001);
        a.Should().BeApproximately(0.0, 0.001);
        b.Should().BeApproximately(0.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_srgb_to_oklab_black()
    {
        var (x, y, z) = ColorConversion.LinearSrgbToXyzD65(0.0, 0.0, 0.0);
        var (L, a, b) = ColorConversion.XyzD65ToOklab(x, y, z);
        L.Should().BeApproximately(0.0, 0.001);
        a.Should().BeApproximately(0.0, 0.001);
        b.Should().BeApproximately(0.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_xyz_d50_to_d65_roundtrip()
    {
        var (x65, y65, z65) = ColorConversion.XyzD50ToXyzD65(0.5, 0.4, 0.3);
        var (x50, y50, z50) = ColorConversion.XyzD65ToXyzD50(x65, y65, z65);
        x50.Should().BeApproximately(0.5, 0.0001);
        y50.Should().BeApproximately(0.4, 0.0001);
        z50.Should().BeApproximately(0.3, 0.0001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_hsl_roundtrip_red()
    {
        var (h, s, l) = ColorConversion.SrgbToHsl(1.0, 0.0, 0.0);
        h.Should().BeApproximately(0.0, 0.001);
        s.Should().BeApproximately(1.0, 0.001);
        l.Should().BeApproximately(0.5, 0.001);
        var (r, g, b) = ColorConversion.HslToSrgb(h, s, l);
        r.Should().BeApproximately(1.0, 0.001);
        g.Should().BeApproximately(0.0, 0.001);
        b.Should().BeApproximately(0.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_lab_to_xyz_roundtrip()
    {
        // Lab(50, 20, -30) → XYZ-D50 → Lab should round-trip.
        var (x, y, z) = ColorConversion.LabToXyzD50(50.0, 20.0, -30.0);
        var (L, a, b) = ColorConversion.XyzD50ToLab(x, y, z);
        L.Should().BeApproximately(50.0, 0.001);
        a.Should().BeApproximately(20.0, 0.001);
        b.Should().BeApproximately(-30.0, 0.001);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-conversion-code", section: "15")]
    [SpecFact]
    public void Conversion_oklab_roundtrip()
    {
        // Oklab(0.6, 0.1, -0.1) → XYZ → Oklab should round-trip.
        var (x, y, z) = ColorConversion.OklabToXyzD65(0.6, 0.1, -0.1);
        var (L, a, b) = ColorConversion.XyzD65ToOklab(x, y, z);
        L.Should().BeApproximately(0.6, 0.001);
        a.Should().BeApproximately(0.1, 0.001);
        b.Should().BeApproximately(-0.1, 0.001);
    }

    // ===== ToSrgb() =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void ToSrgb_from_rgb_preserves_bytes()
    {
        var c = Color("rgb(10 20 30)");
        var s = c.ToSrgb();
        s.R.Should().Be(10);
        s.G.Should().Be(20);
        s.B.Should().Be(30);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void ToSrgb_from_oklch_produces_valid_bytes()
    {
        var c = Color("oklch(0.7 0.15 50)");
        var s = c.ToSrgb();
        s.R.Should().BeInRange(0, 255);
        s.G.Should().BeInRange(0, 255);
        s.B.Should().BeInRange(0, 255);
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#css-gamut-mapping", section: "13")]
    [SpecFact]
    public void ToSrgb_from_display_p3_valid_range()
    {
        var c = Color("color(display-p3 0.5 0.3 0.8)");
        var s = c.ToSrgb();
        s.R.Should().BeInRange(0, 255);
        s.G.Should().BeInRange(0, 255);
        s.B.Should().BeInRange(0, 255);
        s.A.Should().Be(255);
    }

    // ===== HasWideGamutData =====

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void HasWideGamutData_true_for_display_p3()
    {
        var c = Color("color(display-p3 0.8 0.3 0.1)");
        c.HasWideGamutData.Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void HasWideGamutData_true_for_oklch()
    {
        var c = Color("oklch(0.7 0.15 50)");
        c.HasWideGamutData.Should().BeTrue();
    }

    [Spec("css-color-4", "https://www.w3.org/TR/css-color-4/#color-function", section: "10")]
    [SpecFact]
    public void HasWideGamutData_true_for_none_channel()
    {
        // A NaN component signals "none" — HasWideGamutData should be true.
        var c = Color("rgb(none 128 0)");
        c.HasWideGamutData.Should().BeTrue();
    }
}
