using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "6")]
[TestClass]
public sealed class CssBoxShadowParserTests
{
    private static CssValue ParseValue(string source)
    {
        var sheet = new CssParser("a{box-shadow:" + source + "}").ParseStyleSheet();
        var decl = ((StyleRule)sheet.Rules.Single()).Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    private static CssBoxShadow Parse(string source) => CssBoxShadowParser.Parse(ParseValue(source));

    [TestMethod]
    public void None_keyword_yields_no_layers()
    {
        var s = Parse("none");
        s.IsNone.Should().BeTrue();
        s.Layers.Should().BeEmpty();
    }

    [TestMethod]
    public void Two_lengths_set_offsets_and_default_blur_spread_zero()
    {
        var s = Parse("4px 8px red");
        var layer = s.Layers.Should().ContainSingle().Which;
        layer.OffsetX.Should().Be(new CssLength(4, CssLengthUnit.Px));
        layer.OffsetY.Should().Be(new CssLength(8, CssLengthUnit.Px));
        layer.Blur.Should().Be(CssLength.Zero);
        layer.Spread.Should().Be(CssLength.Zero);
        layer.Inset.Should().BeFalse();
        layer.Color.Should().Be(new CssColor(255, 0, 0, 255));
    }

    [TestMethod]
    public void Four_lengths_set_blur_and_spread()
    {
        var layer = Parse("2px 3px 8px 4px rgba(0, 0, 0, 0.5)").Layers.Single();
        layer.OffsetX.Should().Be(new CssLength(2, CssLengthUnit.Px));
        layer.OffsetY.Should().Be(new CssLength(3, CssLengthUnit.Px));
        layer.Blur.Should().Be(new CssLength(8, CssLengthUnit.Px));
        layer.Spread.Should().Be(new CssLength(4, CssLengthUnit.Px));
        layer.Color!.A.Should().Be(128);
    }

    [TestMethod]
    public void Inset_keyword_recognized_in_any_position()
    {
        Parse("inset 1px 1px 2px blue").Layers.Single().Inset.Should().BeTrue();
        Parse("1px 1px 2px blue inset").Layers.Single().Inset.Should().BeTrue();
    }

    [TestMethod]
    public void Missing_color_defaults_to_currentColor_sentinel()
    {
        // No color => null color, signalling the painter to substitute the
        // element's `color`.
        Parse("3px 3px").Layers.Single().Color.Should().BeNull();
        Parse("3px 3px currentColor").Layers.Single().Color.Should().BeNull();
    }

    [TestMethod]
    public void Comma_separates_multiple_layers_in_source_order()
    {
        var s = Parse("1px 1px red, inset 0 0 2px blue, 5px 5px 5px 5px lime");
        s.Layers.Should().HaveCount(3);
        s.Layers[0].Color.Should().Be(new CssColor(255, 0, 0, 255));
        s.Layers[0].Inset.Should().BeFalse();
        s.Layers[1].Inset.Should().BeTrue();
        s.Layers[1].Color.Should().Be(new CssColor(0, 0, 255, 255));
        s.Layers[2].Spread.Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    [TestMethod]
    public void Unitless_zero_is_a_valid_length()
    {
        var layer = Parse("0 0 4px black").Layers.Single();
        layer.OffsetX.Should().Be(CssLength.Zero);
        layer.OffsetY.Should().Be(CssLength.Zero);
        layer.Blur.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    [TestMethod]
    public void Single_length_is_invalid_and_drops_to_none()
    {
        // box-shadow requires at least offset-x and offset-y.
        Parse("4px red").IsNone.Should().BeTrue();
    }

    [TestMethod]
    public void Negative_blur_is_invalid_and_drops_to_none()
    {
        Parse("4px 4px -2px red").IsNone.Should().BeTrue();
    }
}
