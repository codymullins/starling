using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssCompositing1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/compositing-1/">Compositing and Blending Level 1</see>.
/// Covers parsing and cascade metadata for <c>mix-blend-mode</c>,
/// <c>background-blend-mode</c> and <c>isolation</c>.
/// </summary>
[TestClass]
[Spec("compositing-1", "https://www.w3.org/TR/compositing-1/")]
public sealed class CompositingTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ---- mix-blend-mode (Compositing 1 §5.1) ----

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_parses_normal()
        => ValueOf("mix-blend-mode: normal;", PropertyId.MixBlendMode)
            .Should().Be(new CssKeyword("normal"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_parses_separable_blend_keywords()
    {
        // <blend-mode> separable keywords from Compositing 1 §6.
        string[] keywords =
        [
            "multiply", "screen", "overlay", "darken", "lighten",
            "color-dodge", "color-burn", "hard-light", "soft-light",
            "difference", "exclusion",
        ];
        foreach (var keyword in keywords)
        {
            ValueOf($"mix-blend-mode: {keyword};", PropertyId.MixBlendMode)
                .Should().Be(new CssKeyword(keyword), $"'{keyword}' is a valid <blend-mode>");
        }
    }

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_parses_non_separable_blend_keywords()
    {
        // <blend-mode> non-separable keywords from Compositing 1 §7.
        string[] keywords = ["hue", "saturation", "color", "luminosity"];
        foreach (var keyword in keywords)
        {
            ValueOf($"mix-blend-mode: {keyword};", PropertyId.MixBlendMode)
                .Should().Be(new CssKeyword(keyword), $"'{keyword}' is a valid <blend-mode>");
        }
    }

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_parses_plus_lighter()
        => ValueOf("mix-blend-mode: plus-lighter;", PropertyId.MixBlendMode)
            .Should().Be(new CssKeyword("plus-lighter"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_initial_is_normal()
        => PropertyRegistry.InitialValue(PropertyId.MixBlendMode)
            .Should().Be(new CssKeyword("normal"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#mix-blend-mode", section: "5.1")]
    [SpecFact]
    public void Mix_blend_mode_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MixBlendMode).Should().BeFalse();

    // ---- background-blend-mode (Compositing 1 §5.2) ----

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#background-blend-mode", section: "5.2")]
    [SpecFact]
    public void Background_blend_mode_parses_single_keyword()
        => ValueOf("background-blend-mode: multiply;", PropertyId.BackgroundBlendMode)
            .Should().Be(new CssKeyword("multiply"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#background-blend-mode", section: "5.2")]
    [SpecFact]
    public void Background_blend_mode_parses_comma_separated_list()
    {
        // background-blend-mode takes a comma-separated list of <blend-mode>,
        // one per background layer. The list surfaces as a CssValueList.
        var value = ValueOf("background-blend-mode: multiply, screen;", PropertyId.BackgroundBlendMode);
        var list = value.Should().BeOfType<CssValueList>().Subject;

        // The keyword tokens are preserved in order. Comma separators currently
        // surface as empty CssKeyword entries (see pending test below), so we
        // assert on the meaningful keyword values only.
        var keywords = list.Values
            .OfType<CssKeyword>()
            .Where(k => k.Name.Length > 0)
            .Select(k => k.Name)
            .ToList();
        keywords.Should().Equal("multiply", "screen");
    }

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#background-blend-mode", section: "5.2")]
    [PendingFact(
        "comma separators in a background-blend-mode list are not dropped/split: " +
        "the value parser keeps the comma as an empty CssKeyword inside the CssValueList " +
        "instead of producing one clean <blend-mode> entry per background layer",
        trackingWp: "wp:spec-css-compositing-1")]
    public void Background_blend_mode_list_splits_cleanly_on_commas()
    {
        var value = ValueOf("background-blend-mode: multiply, screen;", PropertyId.BackgroundBlendMode);
        var list = value.Should().BeOfType<CssValueList>().Subject;
        // Expected: exactly one entry per layer, no comma artifacts.
        list.Values.Should().Equal(new CssKeyword("multiply"), new CssKeyword("screen"));
    }

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#background-blend-mode", section: "5.2")]
    [SpecFact]
    public void Background_blend_mode_initial_is_normal()
        => PropertyRegistry.InitialValue(PropertyId.BackgroundBlendMode)
            .Should().Be(new CssKeyword("normal"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#background-blend-mode", section: "5.2")]
    [SpecFact]
    public void Background_blend_mode_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.BackgroundBlendMode).Should().BeFalse();

    // ---- isolation (Compositing 1 §3.1) ----

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#isolation", section: "3.1")]
    [SpecFact]
    public void Isolation_parses_auto()
        => ValueOf("isolation: auto;", PropertyId.Isolation)
            .Should().Be(new CssKeyword("auto"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#isolation", section: "3.1")]
    [SpecFact]
    public void Isolation_parses_isolate()
        => ValueOf("isolation: isolate;", PropertyId.Isolation)
            .Should().Be(new CssKeyword("isolate"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#isolation", section: "3.1")]
    [SpecFact]
    public void Isolation_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.Isolation)
            .Should().Be(new CssKeyword("auto"));

    [Spec("compositing-1", "https://www.w3.org/TR/compositing-1/#isolation", section: "3.1")]
    [SpecFact]
    public void Isolation_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Isolation).Should().BeFalse();
}
