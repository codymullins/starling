using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssBackgrounds4;

/// <summary>
/// Property conformance for
/// <see href="https://www.w3.org/TR/css-backgrounds-4/">CSS Backgrounds and Borders Module Level 4</see>:
/// the <c>background-position-x</c> / <c>background-position-y</c> longhands (§3).
/// </summary>
[TestClass]
[Spec("css-backgrounds-4", "https://www.w3.org/TR/css-backgrounds-4/")]
public sealed class BackgroundPositionLonghandTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-backgrounds-4", "https://www.w3.org/TR/css-backgrounds-4/#background-position-longhands", section: "3")]
    [SpecFact]
    public void Background_position_x_parses_keyword_and_length()
    {
        ValueOf("background-position-x: left", PropertyId.BackgroundPositionX).Should().Be(new CssKeyword("left"));
        ValueOf("background-position-x: 10px", PropertyId.BackgroundPositionX).Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Spec("css-backgrounds-4", "https://www.w3.org/TR/css-backgrounds-4/#background-position-longhands", section: "3")]
    [SpecFact]
    public void Background_position_y_parses_keyword_and_percentage()
    {
        ValueOf("background-position-y: bottom", PropertyId.BackgroundPositionY).Should().Be(new CssKeyword("bottom"));
        ValueOf("background-position-y: 25%", PropertyId.BackgroundPositionY).Should().Be(new CssPercentage(25));
    }

    [Spec("css-backgrounds-4", "https://www.w3.org/TR/css-backgrounds-4/#background-position-longhands", section: "3")]
    [SpecFact]
    public void Background_position_longhands_initial_is_zero_percent_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.BackgroundPositionX).Should().Be(new CssPercentage(0));
        PropertyRegistry.InitialValue(PropertyId.BackgroundPositionY).Should().Be(new CssPercentage(0));
        PropertyRegistry.Inherits(PropertyId.BackgroundPositionX).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.BackgroundPositionY).Should().BeFalse();
    }
}
