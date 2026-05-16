using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Values;
using Xunit;

namespace Tessera.Css.Tests;

/// <summary>End-to-end smoke checks for the Values lane done-criteria.</summary>
public sealed class ValuesSmokeTests
{
    private static CssValue ParseSingle(string source)
    {
        var sheet = CssParser.ParseStyleSheet("a{x:" + source + "}");
        var rule = (StyleRule)sheet.Rules.Single();
        return CssValueParser.Parse(rule.Declarations.Single().Value);
    }

    [Fact]
    public void Calc_100vh_minus_80px_round_trips_as_inspectable_calc_tree()
    {
        var v = ParseSingle("calc(100vh - 80px)");
        var calc = v.Should().BeOfType<CssCalc>().Subject;
        calc.Expression.Should().BeOfType<CalcBinary>()
            .Which.Op.Should().Be(CalcOperator.Subtract);
    }

    [Fact]
    public void Oklch_70_15_50_round_trips_as_oklch_color()
    {
        var c = (CssColor)ParseSingle("oklch(0.7 0.15 50)");
        c.Space.Should().Be(ColorSpace.Oklch);
        c.C1.Should().BeApproximately(0.7, 1e-6);
        c.C2.Should().BeApproximately(0.15, 1e-6);
        c.C3.Should().BeApproximately(50.0, 1e-6);
    }

    [Fact]
    public void Color_mix_in_oklch_red_blue_yields_inspectable_color()
    {
        var c = (CssColor)ParseSingle("color-mix(in oklch, red, blue)");
        c.Space.Should().Be(ColorSpace.Oklch);
        // Some non-zero sRGB result.
        (c.R + c.G + c.B).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Clamp_with_calc_expression_inside()
    {
        var v = ParseSingle("clamp(1rem, 2vw + 1rem, 3rem)");
        v.Should().BeOfType<CssCalc>();
    }

    [Fact]
    public void Env_value_recognized_with_fallback()
    {
        var v = ParseSingle("env(safe-area-inset-top, 0px)");
        var env = v.Should().BeOfType<CssEnvReference>().Subject;
        env.Name.Should().Be("safe-area-inset-top");
        env.Fallback.Should().BeOfType<CssLength>();
    }

    [Fact]
    public void Attr_value_with_name_only()
    {
        var v = ParseSingle("attr(data-foo)");
        var attr = v.Should().BeOfType<CssAttrReference>().Subject;
        attr.AttrName.Should().Be("data-foo");
        attr.TypeOrUnit.Should().BeNull();
        attr.Fallback.Should().BeNull();
    }

    [Fact]
    public void Attr_value_with_type_and_fallback()
    {
        var v = ParseSingle("attr(data-count number, 0)");
        var attr = v.Should().BeOfType<CssAttrReference>().Subject;
        attr.AttrName.Should().Be("data-count");
        attr.TypeOrUnit.Should().Be("number");
        attr.Fallback.Should().BeOfType<CssNumber>().Which.Value.Should().Be(0);
    }
}
