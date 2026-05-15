using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Values;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class LengthUnitTests
{
    private static CssValue ParseSingle(string source)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + source + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    [Theory]
    [InlineData("10px", CssLengthUnit.Px)]
    [InlineData("10em", CssLengthUnit.Em)]
    [InlineData("10rem", CssLengthUnit.Rem)]
    [InlineData("10pt", CssLengthUnit.Pt)]
    [InlineData("10pc", CssLengthUnit.Pc)]
    [InlineData("10in", CssLengthUnit.In)]
    [InlineData("10cm", CssLengthUnit.Cm)]
    [InlineData("10mm", CssLengthUnit.Mm)]
    [InlineData("10q", CssLengthUnit.Q)]
    [InlineData("10ch", CssLengthUnit.Ch)]
    [InlineData("10ex", CssLengthUnit.Ex)]
    public void Existing_units_continue_to_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10lh", CssLengthUnit.Lh)]
    [InlineData("10rlh", CssLengthUnit.Rlh)]
    [InlineData("10cap", CssLengthUnit.Cap)]
    [InlineData("10ic", CssLengthUnit.Ic)]
    public void Font_relative_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10vh", CssLengthUnit.Vh)]
    [InlineData("10vw", CssLengthUnit.Vw)]
    [InlineData("10vmin", CssLengthUnit.Vmin)]
    [InlineData("10vmax", CssLengthUnit.Vmax)]
    [InlineData("10vi", CssLengthUnit.Vi)]
    [InlineData("10vb", CssLengthUnit.Vb)]
    public void Viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10svh", CssLengthUnit.Svh)]
    [InlineData("10svw", CssLengthUnit.Svw)]
    [InlineData("10svmin", CssLengthUnit.Svmin)]
    [InlineData("10svmax", CssLengthUnit.Svmax)]
    public void Small_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10lvh", CssLengthUnit.Lvh)]
    [InlineData("10lvw", CssLengthUnit.Lvw)]
    [InlineData("10lvmin", CssLengthUnit.Lvmin)]
    [InlineData("10lvmax", CssLengthUnit.Lvmax)]
    public void Large_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10dvh", CssLengthUnit.Dvh)]
    [InlineData("10dvw", CssLengthUnit.Dvw)]
    [InlineData("10dvmin", CssLengthUnit.Dvmin)]
    [InlineData("10dvmax", CssLengthUnit.Dvmax)]
    public void Dynamic_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Theory]
    [InlineData("10cqw", CssLengthUnit.Cqw)]
    [InlineData("10cqh", CssLengthUnit.Cqh)]
    [InlineData("10cqi", CssLengthUnit.Cqi)]
    [InlineData("10cqb", CssLengthUnit.Cqb)]
    [InlineData("10cqmin", CssLengthUnit.Cqmin)]
    [InlineData("10cqmax", CssLengthUnit.Cqmax)]
    public void Container_query_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [Fact]
    public void To_css_text_round_trips_unit_name()
    {
        // Sample a few to verify ToCssText covers the new units.
        CssLengthUnit.Lh.ToCssText().Should().Be("lh");
        CssLengthUnit.Svh.ToCssText().Should().Be("svh");
        CssLengthUnit.Dvw.ToCssText().Should().Be("dvw");
        CssLengthUnit.Cqmin.ToCssText().Should().Be("cqmin");
    }

    [Fact]
    public void Angle_dimensions_parse()
    {
        ParseSingle("45deg").Should().BeOfType<CssAngle>().Which.Unit.Should().Be(CssAngleUnit.Degrees);
        ParseSingle("0.25turn").Should().BeOfType<CssAngle>().Which.Value.Should().Be(0.25);
    }

    [Fact]
    public void Time_dimensions_parse()
    {
        ParseSingle("250ms").Should().BeOfType<CssTime>().Which.Unit.Should().Be(CssTimeUnit.Milliseconds);
        ParseSingle("2s").Should().BeOfType<CssTime>().Which.Unit.Should().Be(CssTimeUnit.Seconds);
    }
}
