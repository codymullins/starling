using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-values-4", "https://www.w3.org/TR/css-values-4/")]

public sealed class CalcEvaluatorTests
{
    private static CssValue ParseSingle(string source)
    {
        // Wrap in `a:` to feed via the declaration parser; we want the value tree.
        var parser = new CssParser("a{x:" + source + "}");
        var sheet = parser.ParseStyleSheet();
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    [Fact]
    public void Calc_with_absolute_lengths_folds_to_a_single_length()
    {
        var value = ParseSingle("calc(10px + 5px)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(15);
    }

    [Fact]
    public void Calc_unit_conversion_between_absolute_units()
    {
        var value = ParseSingle("calc(1in - 48px)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(48);
    }

    [Fact]
    public void Calc_with_relative_units_remains_symbolic()
    {
        var value = ParseSingle("calc(100vh - 80px)");
        value.Should().BeOfType<CssCalc>();
    }

    [Fact]
    public void Calc_number_multiplication_preserves_length()
    {
        var value = ParseSingle("calc(10px * 3)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(30);
    }

    [Fact]
    public void Calc_division_preserves_length()
    {
        var value = ParseSingle("calc(10px / 2)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void Calc_respects_operator_precedence()
    {
        var value = ParseSingle("calc(2px + 3px * 4)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(14);
    }

    [Fact]
    public void Calc_handles_parenthesized_expressions()
    {
        var value = ParseSingle("calc((2px + 3px) * 4)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(20);
    }

    [Fact]
    public void Min_picks_the_smallest_absolute_length()
    {
        var value = ParseSingle("min(10px, 5px, 20px)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void Max_picks_the_largest_absolute_length()
    {
        var value = ParseSingle("max(10px, 5px, 20px)");
        value.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(20);
    }

    [Fact]
    public void Clamp_clamps_value_within_bounds()
    {
        ParseSingle("clamp(1px, 100px, 10px)").Should().BeOfType<CssLength>().Which.Value.Should().Be(10);
        ParseSingle("clamp(50px, 100px, 200px)").Should().BeOfType<CssLength>().Which.Value.Should().Be(100);
        ParseSingle("clamp(150px, 100px, 200px)").Should().BeOfType<CssLength>().Which.Value.Should().Be(150);
    }

    [Fact]
    public void Clamp_with_relative_units_remains_symbolic()
    {
        var value = ParseSingle("clamp(1rem, 2vw + 1rem, 3rem)");
        value.Should().BeOfType<CssCalc>();
    }

    [Fact]
    public void Round_default_strategy_is_nearest()
    {
        ParseSingle("round(2.7, 1)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(3);
        ParseSingle("round(2.4, 1)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(2);
    }

    [Fact]
    public void Round_with_up_strategy_ceilings()
    {
        ParseSingle("round(up, 2.1, 1)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(3);
    }

    [Fact]
    public void Round_with_down_strategy_floors()
    {
        ParseSingle("round(down, 2.9, 1)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(2);
    }

    [Fact]
    public void Round_with_to_zero_truncates()
    {
        ParseSingle("round(to-zero, -2.9, 1)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(-2);
    }

    [Fact]
    public void Mod_uses_floor_division()
    {
        // mod( -7, 3 ) = -7 - floor(-7/3) * 3 = -7 - (-3)*3 = 2
        ParseSingle("mod(-7, 3)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(2);
    }

    [Fact]
    public void Rem_uses_truncated_division()
    {
        // rem( -7, 3 ) = -7 - trunc(-7/3) * 3 = -7 - (-2)*3 = -1
        ParseSingle("rem(-7, 3)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(-1);
    }

    [Fact]
    public void Trig_sin_returns_number()
    {
        var v = ParseSingle("sin(0)");
        v.Should().BeOfType<CssNumber>().Which.Value.Should().Be(0);
    }

    [Fact]
    public void Trig_atan_returns_angle()
    {
        var v = ParseSingle("atan(1)");
        v.Should().BeOfType<CssAngle>()
            .Which.Value.Should().BeApproximately(45.0, 0.0001);
    }

    [Fact]
    public void Pow_and_sqrt_fold_numbers()
    {
        ParseSingle("pow(2, 10)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(1024);
        ParseSingle("sqrt(81)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(9);
    }

    [Fact]
    public void Hypot_folds_numbers()
    {
        ParseSingle("hypot(3, 4)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(5);
    }

    [Fact]
    public void Abs_and_sign_fold_numbers()
    {
        ParseSingle("abs(-3)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(3);
        ParseSingle("sign(-5)").Should().BeOfType<CssNumber>().Which.Value.Should().Be(-1);
    }

    [Fact]
    public void Pi_constant_is_recognized()
    {
        var v = ParseSingle("calc(pi)");
        v.Should().BeOfType<CssNumber>().Which.Value.Should().BeApproximately(Math.PI, 0.0001);
    }

    [Fact]
    public void Infinity_constant_is_recognized()
    {
        var v = ParseSingle("calc(infinity)");
        v.Should().BeOfType<CssNumber>().Which.Value.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void Length_times_length_marks_unknown_type()
    {
        // Should still parse — but stay symbolic with unknown type.
        var v = ParseSingle("calc(10px * 5px)");
        v.Should().BeOfType<CssCalc>();
    }

    [Fact]
    public void Percentage_plus_length_is_length_percentage()
    {
        var v = ParseSingle("calc(50% + 10px)");
        v.Should().BeOfType<CssCalc>();
        var calc = (CssCalc)v;
        calc.Expression.Type.Should().Be(NumericType.LengthPercentage);
    }

    [Fact]
    public void Calc_with_negation_works()
    {
        ParseSingle("calc(-5px + 10px)").Should().BeOfType<CssLength>().Which.Value.Should().Be(5);
    }

    [Fact]
    public void Calc_nested_min_max()
    {
        var v = ParseSingle("max(10px, min(20px, 30px))");
        v.Should().BeOfType<CssLength>().Which.Value.Should().Be(20);
    }
}
