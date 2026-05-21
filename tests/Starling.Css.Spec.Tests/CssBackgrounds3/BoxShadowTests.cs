using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssBackgrounds3;

/// <summary>
/// <see href="https://www.w3.org/TR/css-backgrounds-3/#box-shadow">CSS Backgrounds 3 §6</see>:
/// the <c>box-shadow</c> property.
/// </summary>
[TestClass]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "6")]
public sealed class BoxShadowTests
{
    private static CssBoxShadow Parse(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ box-shadow: {css}; }}");
        var rule = (StyleRule)sheet.Rules[0];
        var decl = rule.Declarations.SelectMany(PropertyRegistry.Parse)
            .Single(d => d.Id == PropertyId.BoxShadow);
        return CssBoxShadowParser.Parse(decl.Value);
    }

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    [SpecFact]
    public void Offset_blur_spread_color_parse_in_order()
    {
        // §6 grammar: <length>{2,4} && <color>? && inset?
        var shadow = Parse("4px 4px 8px 1px rgba(0, 0, 0, 0.5)");
        var layer = shadow.Layers.Should().ContainSingle().Which;
        layer.OffsetX.Should().Be(new CssLength(4, CssLengthUnit.Px));
        layer.OffsetY.Should().Be(new CssLength(4, CssLengthUnit.Px));
        layer.Blur.Should().Be(new CssLength(8, CssLengthUnit.Px));
        layer.Spread.Should().Be(new CssLength(1, CssLengthUnit.Px));
        layer.Color!.A.Should().Be(128);
        layer.Inset.Should().BeFalse();
    }

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#shadow-shape", section: "6")]
    [SpecFact]
    public void Inset_keyword_is_recognized()
    {
        Parse("inset 2px 2px 4px black").Layers.Single().Inset.Should().BeTrue();
    }

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#shadow-layers", section: "6")]
    [SpecFact]
    public void Comma_separated_layers_paint_first_listed_on_top()
    {
        var shadow = Parse("0 1px 2px red, 0 4px 8px blue");
        shadow.Layers.Should().HaveCount(2);
        shadow.Layers[0].Color.Should().Be(new Starling.Css.Values.CssColor(255, 0, 0, 255));
        shadow.Layers[1].Color.Should().Be(new Starling.Css.Values.CssColor(0, 0, 255, 255));
    }

    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    [SpecFact]
    public void None_yields_no_layers()
    {
        Parse("none").IsNone.Should().BeTrue();
    }
}
