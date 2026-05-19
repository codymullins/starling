using FluentAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-values-4", "https://www.w3.org/TR/css-values-4/")]

[TestClass]
public sealed class LengthUnitTests
{
    private static CssValue ParseSingle(string source)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + source + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    [TestMethod]
    [DataRow("10px", CssLengthUnit.Px)]
    [DataRow("10em", CssLengthUnit.Em)]
    [DataRow("10rem", CssLengthUnit.Rem)]
    [DataRow("10pt", CssLengthUnit.Pt)]
    [DataRow("10pc", CssLengthUnit.Pc)]
    [DataRow("10in", CssLengthUnit.In)]
    [DataRow("10cm", CssLengthUnit.Cm)]
    [DataRow("10mm", CssLengthUnit.Mm)]
    [DataRow("10q", CssLengthUnit.Q)]
    [DataRow("10ch", CssLengthUnit.Ch)]
    [DataRow("10ex", CssLengthUnit.Ex)]
    public void Existing_units_continue_to_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10lh", CssLengthUnit.Lh)]
    [DataRow("10rlh", CssLengthUnit.Rlh)]
    [DataRow("10cap", CssLengthUnit.Cap)]
    [DataRow("10ic", CssLengthUnit.Ic)]
    public void Font_relative_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10vh", CssLengthUnit.Vh)]
    [DataRow("10vw", CssLengthUnit.Vw)]
    [DataRow("10vmin", CssLengthUnit.Vmin)]
    [DataRow("10vmax", CssLengthUnit.Vmax)]
    [DataRow("10vi", CssLengthUnit.Vi)]
    [DataRow("10vb", CssLengthUnit.Vb)]
    public void Viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10svh", CssLengthUnit.Svh)]
    [DataRow("10svw", CssLengthUnit.Svw)]
    [DataRow("10svmin", CssLengthUnit.Svmin)]
    [DataRow("10svmax", CssLengthUnit.Svmax)]
    public void Small_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10lvh", CssLengthUnit.Lvh)]
    [DataRow("10lvw", CssLengthUnit.Lvw)]
    [DataRow("10lvmin", CssLengthUnit.Lvmin)]
    [DataRow("10lvmax", CssLengthUnit.Lvmax)]
    public void Large_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10dvh", CssLengthUnit.Dvh)]
    [DataRow("10dvw", CssLengthUnit.Dvw)]
    [DataRow("10dvmin", CssLengthUnit.Dvmin)]
    [DataRow("10dvmax", CssLengthUnit.Dvmax)]
    public void Dynamic_viewport_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("10cqw", CssLengthUnit.Cqw)]
    [DataRow("10cqh", CssLengthUnit.Cqh)]
    [DataRow("10cqi", CssLengthUnit.Cqi)]
    [DataRow("10cqb", CssLengthUnit.Cqb)]
    [DataRow("10cqmin", CssLengthUnit.Cqmin)]
    [DataRow("10cqmax", CssLengthUnit.Cqmax)]
    public void Container_query_units_parse(string css, CssLengthUnit expected)
    {
        var value = ParseSingle(css);
        value.Should().BeOfType<CssLength>().Which.Unit.Should().Be(expected);
    }

    [TestMethod]
    public void To_css_text_round_trips_unit_name()
    {
        // Sample a few to verify ToCssText covers the new units.
        CssLengthUnit.Lh.ToCssText().Should().Be("lh");
        CssLengthUnit.Svh.ToCssText().Should().Be("svh");
        CssLengthUnit.Dvw.ToCssText().Should().Be("dvw");
        CssLengthUnit.Cqmin.ToCssText().Should().Be("cqmin");
    }

    [TestMethod]
    public void Angle_dimensions_parse()
    {
        ParseSingle("45deg").Should().BeOfType<CssAngle>().Which.Unit.Should().Be(CssAngleUnit.Degrees);
        ParseSingle("0.25turn").Should().BeOfType<CssAngle>().Which.Value.Should().Be(0.25);
    }

    [TestMethod]
    public void Time_dimensions_parse()
    {
        ParseSingle("250ms").Should().BeOfType<CssTime>().Which.Unit.Should().Be(CssTimeUnit.Milliseconds);
        ParseSingle("2s").Should().BeOfType<CssTime>().Which.Unit.Should().Be(CssTimeUnit.Seconds);
    }
}
