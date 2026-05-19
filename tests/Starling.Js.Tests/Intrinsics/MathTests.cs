using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the <c>Math</c> intrinsic installed by
/// <c>MathObj.Install</c>. Each test compiles a small script that exercises a
/// constant, method, or edge case and asserts on the runtime value.
/// </summary>
public class MathTests
{
    // ---------------------------------------------------------- constants

    [Fact]
    public void Math_PI_matches_system_pi()
    {
        Eval("Math.PI;").AsNumber.Should().Be(System.Math.PI);
    }

    [Fact]
    public void Math_E_matches_system_e()
    {
        Eval("Math.E;").AsNumber.Should().Be(System.Math.E);
    }

    [Fact]
    public void Math_all_eight_constants_present_with_expected_values()
    {
        Eval("Math.LN10;").AsNumber.Should().Be(System.Math.Log(10));
        Eval("Math.LN2;").AsNumber.Should().Be(System.Math.Log(2));
        Eval("Math.LOG10E;").AsNumber.Should().Be(1.0 / System.Math.Log(10));
        Eval("Math.LOG2E;").AsNumber.Should().Be(1.0 / System.Math.Log(2));
        Eval("Math.SQRT1_2;").AsNumber.Should().Be(System.Math.Sqrt(0.5));
        Eval("Math.SQRT2;").AsNumber.Should().Be(System.Math.Sqrt(2));
    }

    // ---------------------------------------------------------- abs

    [Fact]
    public void Math_abs_returns_magnitude()
    {
        Eval("Math.abs(-3);").AsNumber.Should().Be(3);
    }

    [Fact]
    public void Math_abs_of_NaN_is_NaN()
    {
        double.IsNaN(Eval("Math.abs(NaN);").AsNumber).Should().BeTrue();
    }

    // ---------------------------------------------------------- max / min

    [Fact]
    public void Math_max_with_args_returns_largest()
    {
        Eval("Math.max(1, 2, 3);").AsNumber.Should().Be(3);
    }

    [Fact]
    public void Math_max_with_no_args_is_negative_infinity()
    {
        Eval("Math.max();").AsNumber.Should().Be(double.NegativeInfinity);
    }

    [Fact]
    public void Math_min_with_args_returns_smallest()
    {
        Eval("Math.min(1, 2, 3);").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Math_min_with_no_args_is_positive_infinity()
    {
        Eval("Math.min();").AsNumber.Should().Be(double.PositiveInfinity);
    }

    // ---------------------------------------------------------- round

    [Fact]
    public void Math_round_half_rounds_up_for_positive()
    {
        // JS-specific: rounds half toward +∞, not banker's-rounding.
        Eval("Math.round(0.5);").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Math_round_half_rounds_toward_zero_for_negative()
    {
        // Math.round(-0.5) is 0 in JS (half toward +∞ ⇒ -0.5 → -0).
        Eval("Math.round(-0.5);").AsNumber.Should().Be(0);
    }

    // ---------------------------------------------------------- floor / ceil / trunc

    [Fact]
    public void Math_floor_drops_fractional()
    {
        Eval("Math.floor(1.7);").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Math_ceil_rounds_up()
    {
        Eval("Math.ceil(1.2);").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Math_trunc_drops_fractional_toward_zero()
    {
        Eval("Math.trunc(-1.7);").AsNumber.Should().Be(-1);
    }

    // ---------------------------------------------------------- sqrt / pow

    [Fact]
    public void Math_sqrt_of_perfect_square()
    {
        Eval("Math.sqrt(9);").AsNumber.Should().Be(3);
    }

    [Fact]
    public void Math_pow_2_to_the_10_is_1024()
    {
        Eval("Math.pow(2, 10);").AsNumber.Should().Be(1024);
    }

    [Fact]
    public void Math_pow_NaN_to_zero_is_one()
    {
        // ES spec quirk: pow(NaN, 0) === 1.
        Eval("Math.pow(NaN, 0);").AsNumber.Should().Be(1);
    }

    // ---------------------------------------------------------- hypot

    [Fact]
    public void Math_hypot_classic_3_4_5_triangle()
    {
        Eval("Math.hypot(3, 4);").AsNumber.Should().Be(5);
    }

    // ---------------------------------------------------------- sign

    [Fact]
    public void Math_sign_of_negative_is_minus_one()
    {
        Eval("Math.sign(-5);").AsNumber.Should().Be(-1);
    }

    [Fact]
    public void Math_sign_of_positive_zero_is_positive_zero()
    {
        var v = Eval("Math.sign(0);").AsNumber;
        v.Should().Be(0);
        double.IsNegative(v).Should().BeFalse();
    }

    [Fact]
    public void Math_sign_of_negative_zero_is_negative_zero()
    {
        // Discriminate -0 from +0 via 1/x — Object.is isn't available yet.
        var v = Eval("1 / Math.sign(-0);").AsNumber;
        v.Should().Be(double.NegativeInfinity);
    }

    // ---------------------------------------------------------- imul

    [Fact]
    public void Math_imul_handles_uint32_overflow()
    {
        // 0xffffffff (== -1 as Int32) * 5 == -5 in Int32 mod 2^32.
        Eval("Math.imul(0xffffffff, 5);").AsNumber.Should().Be(-5);
    }

    // ---------------------------------------------------------- clz32

    [Fact]
    public void Math_clz32_of_one_is_thirty_one()
    {
        Eval("Math.clz32(1);").AsNumber.Should().Be(31);
    }

    [Fact]
    public void Math_clz32_of_zero_is_thirty_two()
    {
        Eval("Math.clz32(0);").AsNumber.Should().Be(32);
    }

    // ---------------------------------------------------------- helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
