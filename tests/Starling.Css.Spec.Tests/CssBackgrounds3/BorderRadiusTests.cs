using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssBackgrounds3;

/// <summary>
/// <see href="https://www.w3.org/TR/css-backgrounds-3/#border-radius">CSS Backgrounds 3 §5</see>:
/// <c>border-radius</c> and its longhands.
/// </summary>
[TestClass]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "5")]
public sealed class BorderRadiusTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Longhand(List<PropertyDeclaration> decls, PropertyId id)
        => decls.Single(d => d.Id == id).Value;

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#border-radius", section: "5")]
    [SpecFact]
    public void Single_value_applies_to_all_four_corners()
    {
        // border-radius: 8px;  → all 4 corners 8px.
        var decls = Expand("border-radius: 8px;");
        var px8 = new CssLength(8, CssLengthUnit.Px);
        Longhand(decls, PropertyId.BorderTopLeftRadius).Should().Be(px8);
        Longhand(decls, PropertyId.BorderTopRightRadius).Should().Be(px8);
        Longhand(decls, PropertyId.BorderBottomRightRadius).Should().Be(px8);
        Longhand(decls, PropertyId.BorderBottomLeftRadius).Should().Be(px8);
    }

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#border-radius", section: "5")]
    [SpecFact]
    public void Four_values_map_to_corners_clockwise_from_top_left()
    {
        // border-radius: 1px 2px 3px 4px → TL TR BR BL.
        var decls = Expand("border-radius: 1px 2px 3px 4px;");
        Longhand(decls, PropertyId.BorderTopLeftRadius).Should().Be(new CssLength(1, CssLengthUnit.Px));
        Longhand(decls, PropertyId.BorderTopRightRadius).Should().Be(new CssLength(2, CssLengthUnit.Px));
        Longhand(decls, PropertyId.BorderBottomRightRadius).Should().Be(new CssLength(3, CssLengthUnit.Px));
        Longhand(decls, PropertyId.BorderBottomLeftRadius).Should().Be(new CssLength(4, CssLengthUnit.Px));
    }
}
