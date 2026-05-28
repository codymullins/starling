using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssAnchorPosition1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-anchor-position-1/">CSS Anchor Positioning Level 1</see>:
/// anchor-name / position-anchor / position-area / anchor-scope.
/// Parse + cascade level — anchor-relative layout resolution is not implemented.
/// </summary>
[TestClass]
[Spec("css-anchor-position-1", "https://www.w3.org/TR/css-anchor-position-1/")]
public sealed class AnchorPropertiesTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-anchor-position-1", "https://www.w3.org/TR/css-anchor-position-1/#name", section: "2")]
    [SpecFact]
    public void Anchor_name_parses_none_and_dashed_ident()
    {
        ValueOf("anchor-name: none", PropertyId.AnchorName).Should().Be(new CssKeyword("none"));
        ValueOf("anchor-name: --hero", PropertyId.AnchorName).Should().Be(new CssKeyword("--hero"));
    }

    [Spec("css-anchor-position-1", "https://www.w3.org/TR/css-anchor-position-1/#position-anchor", section: "3")]
    [SpecFact]
    public void Position_anchor_parses_auto_and_dashed_ident()
    {
        ValueOf("position-anchor: auto", PropertyId.PositionAnchor).Should().Be(new CssKeyword("auto"));
        ValueOf("position-anchor: --hero", PropertyId.PositionAnchor).Should().Be(new CssKeyword("--hero"));
    }

    [Spec("css-anchor-position-1", "https://www.w3.org/TR/css-anchor-position-1/#position-area", section: "5")]
    [SpecFact]
    public void Position_area_parses_keyword_pair()
    {
        var value = ValueOf("position-area: top center", PropertyId.PositionArea);
        value.Should().BeOfType<CssValueList>().Which.Values.Should().HaveCount(2);
    }

    [Spec("css-anchor-position-1", "https://www.w3.org/TR/css-anchor-position-1/", section: "2")]
    [SpecFact]
    public void Anchor_properties_initial_values_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.AnchorName).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.PositionAnchor).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.Inherits(PropertyId.AnchorName).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.PositionAnchor).Should().BeFalse();
    }
}
