using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssSizing4;

/// <summary>
/// Property conformance for the <c>contain-intrinsic-size</c> family of
/// <see href="https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override">CSS Box Sizing 4 §4</see>.
/// Parse + cascade level — the size-containment placeholder behavior is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/")]
public sealed class ContainIntrinsicSizeTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#propdef-contain-intrinsic-width", section: "4")]
    [SpecFact]
    public void Contain_intrinsic_width_parses_none_and_length()
    {
        ValueOf("contain-intrinsic-width: none", PropertyId.ContainIntrinsicWidth).Should().Be(new CssKeyword("none"));
        ValueOf("contain-intrinsic-width: 300px", PropertyId.ContainIntrinsicWidth).Should().Be(new CssLength(300, CssLengthUnit.Px));
    }

    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#propdef-contain-intrinsic-height", section: "4")]
    [SpecFact]
    public void Contain_intrinsic_height_parses_length()
        => ValueOf("contain-intrinsic-height: 150px", PropertyId.ContainIntrinsicHeight).Should().Be(new CssLength(150, CssLengthUnit.Px));

    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#propdef-contain-intrinsic-size", section: "4")]
    [SpecFact]
    public void Contain_intrinsic_size_shorthand_one_value_fills_both()
    {
        var decls = Expand("contain-intrinsic-size: 200px");
        decls.Single(d => d.Id == PropertyId.ContainIntrinsicWidth).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ContainIntrinsicHeight).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
    }

    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#propdef-contain-intrinsic-size", section: "4")]
    [SpecFact]
    public void Contain_intrinsic_size_shorthand_two_values_map_width_then_height()
    {
        var decls = Expand("contain-intrinsic-size: 200px 100px");
        decls.Single(d => d.Id == PropertyId.ContainIntrinsicWidth).Value.Should().Be(new CssLength(200, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ContainIntrinsicHeight).Value.Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    [Spec("css-sizing-4", "https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override", section: "4")]
    [SpecFact]
    public void Contain_intrinsic_longhands_initial_is_none_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.ContainIntrinsicWidth).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.ContainIntrinsicHeight).Should().Be(new CssKeyword("none"));
        PropertyRegistry.Inherits(PropertyId.ContainIntrinsicWidth).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ContainIntrinsicHeight).Should().BeFalse();
    }
}
