using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssColor5;

/// <summary>
/// <see href="https://www.w3.org/TR/css-color-5/#color-mix">CSS Color L5 §6</see>:
/// the <c>color-mix()</c> function.
/// </summary>
[TestClass]
[Spec("css-color-5", "https://www.w3.org/TR/css-color-5/", section: "6")]
public sealed class ColorMixTests
{
    private static Starling.Css.Values.CssColor Mix(string value)
    {
        var sheet = CssParser.ParseStyleSheet($"a{{color:{value}}}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value).Should().BeOfType<Starling.Css.Values.CssColor>().Subject;
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#color-mix", section: "6")]
    [SpecFact]
    public void Srgb_equal_mix_of_red_and_blue_is_half_each()
    {
        // 50/50 mix in sRGB: R = B = 0.5*255 ≈ 128, G = 0.
        var c = Mix("color-mix(in srgb, red, blue)");
        ((int)c.R).Should().BeInRange(126, 130);
        ((int)c.G).Should().Be(0);
        ((int)c.B).Should().BeInRange(126, 130);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#color-mix", section: "6")]
    [SpecFact]
    public void Srgb_mix_of_white_and_black_is_gray()
    {
        var c = Mix("color-mix(in srgb, white, black)");
        ((int)c.R).Should().BeInRange(126, 130);
        ((int)c.G).Should().BeInRange(126, 130);
        ((int)c.B).Should().BeInRange(126, 130);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#percentage-normalization", section: "6")]
    [SpecFact]
    public void Explicit_percentage_weights_the_first_color()
    {
        // 25% red, (implied 75%) blue → R ≈ 0.25*255 ≈ 64, B ≈ 0.75*255 ≈ 191.
        var c = Mix("color-mix(in srgb, red 25%, blue)");
        ((int)c.R).Should().BeInRange(61, 67);
        ((int)c.B).Should().BeInRange(188, 194);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#percentage-normalization", section: "6")]
    [SpecFact]
    public void Both_percentages_normalize()
    {
        // 30% / 70% → R ≈ 0.3*255 ≈ 77, B ≈ 0.7*255 ≈ 179.
        var c = Mix("color-mix(in srgb, red 30%, blue 70%)");
        ((int)c.R).Should().BeInRange(74, 80);
        ((int)c.B).Should().BeInRange(176, 182);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#color-mix", section: "6")]
    [SpecFact]
    public void Mix_in_polar_space_produces_in_gamut_color()
    {
        // oklch mixing routes through a polar space + gamut mapping; assert the
        // result is a valid sRGB color (channels in range).
        var c = Mix("color-mix(in oklch, red, blue)");
        ((int)c.R).Should().BeInRange(0, 255);
        ((int)c.G).Should().BeInRange(0, 255);
        ((int)c.B).Should().BeInRange(0, 255);
        ((int)c.A).Should().Be(255);
    }
}
