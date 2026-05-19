using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end (parse → compile → run) tests for B4-3 BigInt: literal syntax,
/// arithmetic/bitwise operators, comparison rules, the BigInt constructor +
/// static helpers (asIntN/asUintN), and prototype methods.
/// </summary>
[TestClass]
public class BigIntTests
{
    // ----------------------------------------------------------- Literals
    [TestMethod] public void Literal_decimal_zero() =>
        Eval("0n === 0n").AsBool.Should().BeTrue();

    [TestMethod] public void Literal_decimal_addition() =>
        Eval("123n + 1n === 124n").AsBool.Should().BeTrue();

    [TestMethod] public void Literal_hex() =>
        Eval("0xFFn === 255n").AsBool.Should().BeTrue();

    [TestMethod] public void Literal_binary() =>
        Eval("0b101n === 5n").AsBool.Should().BeTrue();

    [TestMethod] public void Literal_octal() =>
        Eval("0o17n === 15n").AsBool.Should().BeTrue();

    // ------------------------------------------------------------- typeof
    [TestMethod] public void Typeof_bigint() =>
        Eval("typeof 1n").AsString.Should().Be("bigint");

    // ------------------------------------------------------- BigInt(value)
    [TestMethod] public void Ctor_from_number() =>
        Eval("BigInt(5) === 5n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_decimal_string() =>
        Eval("BigInt('100') === 100n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_hex_string() =>
        Eval("BigInt('0x10') === 16n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_binary_string() =>
        Eval("BigInt('0b1010') === 10n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_octal_string() =>
        Eval("BigInt('0o17') === 15n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_boolean_true() =>
        Eval("BigInt(true) === 1n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_boolean_false() =>
        Eval("BigInt(false) === 0n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_from_bigint_identity() =>
        Eval("BigInt(42n) === 42n").AsBool.Should().BeTrue();

    [TestMethod] public void Ctor_non_integer_number_throws() =>
        ((Action)(() => Eval("BigInt(1.5)"))).Should().Throw<JsThrow>();

    [TestMethod] public void Ctor_null_throws() =>
        ((Action)(() => Eval("BigInt(null)"))).Should().Throw<JsThrow>();

    [TestMethod] public void Ctor_undefined_throws() =>
        ((Action)(() => Eval("BigInt(undefined)"))).Should().Throw<JsThrow>();

    [TestMethod] public void Ctor_symbol_throws() =>
        ((Action)(() => Eval("BigInt(Symbol())"))).Should().Throw<JsThrow>();

    [TestMethod] public void New_BigInt_throws() =>
        ((Action)(() => Eval("new BigInt(1)"))).Should().Throw<JsThrow>();

    // -------------------------------------------------------- Arithmetic
    [TestMethod] public void Add() => Eval("1n + 2n === 3n").AsBool.Should().BeTrue();
    [TestMethod] public void Subtract() => Eval("5n - 3n === 2n").AsBool.Should().BeTrue();
    [TestMethod] public void Multiply() => Eval("4n * 6n === 24n").AsBool.Should().BeTrue();
    [TestMethod] public void Divide_truncates_toward_zero() => Eval("10n / 3n === 3n").AsBool.Should().BeTrue();
    [TestMethod] public void Divide_negative_truncates() => Eval("(-10n) / 3n === -3n").AsBool.Should().BeTrue();
    [TestMethod] public void Modulo_dividend_sign() => Eval("(-10n) % 3n === -1n").AsBool.Should().BeTrue();
    [TestMethod] public void Pow() => Eval("2n ** 10n === 1024n").AsBool.Should().BeTrue();

    [TestMethod] public void Negative_exponent_throws() =>
        ((Action)(() => Eval("2n ** -1n"))).Should().Throw<JsThrow>();

    [TestMethod] public void Division_by_zero_throws() =>
        ((Action)(() => Eval("1n / 0n"))).Should().Throw<JsThrow>();

    // ---------------------------------------------------------- Bitwise
    [TestMethod] public void BitAnd() => Eval("(0xFFn & 0x0Fn) === 0x0Fn").AsBool.Should().BeTrue();
    [TestMethod] public void BitOr() => Eval("(0xF0n | 0x0Fn) === 0xFFn").AsBool.Should().BeTrue();
    [TestMethod] public void BitXor() => Eval("(0xFFn ^ 0x0Fn) === 0xF0n").AsBool.Should().BeTrue();
    [TestMethod] public void BitNot() => Eval("~0n === -1n").AsBool.Should().BeTrue();
    [TestMethod] public void ShiftLeft() => Eval("1n << 8n === 256n").AsBool.Should().BeTrue();
    [TestMethod] public void ShiftRight() => Eval("256n >> 4n === 16n").AsBool.Should().BeTrue();

    // ---------------------------------------------------------- Unary
    [TestMethod] public void Unary_minus_works() =>
        Eval("(-5n) === 0n - 5n").AsBool.Should().BeTrue();

    [TestMethod] public void Unary_plus_throws() =>
        ((Action)(() => Eval("+1n"))).Should().Throw<JsThrow>();

    // ---------------------------------------------------------- Mixed-type
    [TestMethod] public void Mixed_add_throws() =>
        ((Action)(() => Eval("1n + 1"))).Should().Throw<JsThrow>();

    [TestMethod] public void Mixed_multiply_throws() =>
        ((Action)(() => Eval("1n * 2"))).Should().Throw<JsThrow>();

    [TestMethod] public void Mixed_divide_throws() =>
        ((Action)(() => Eval("4n / 2"))).Should().Throw<JsThrow>();

    [TestMethod] public void Unsigned_shift_on_bigint_throws() =>
        ((Action)(() => Eval("1n >>> 0n"))).Should().Throw<JsThrow>();

    // ---------------------------------------------------------- Equality
    [TestMethod] public void Strict_equal_same_value() => Eval("1n === 1n").AsBool.Should().BeTrue();
    [TestMethod] public void Strict_equal_different_kind_is_false() => Eval("1n === 1").AsBool.Should().BeFalse();
    [TestMethod] public void Loose_equal_to_number_integer() => Eval("1n == 1").AsBool.Should().BeTrue();
    [TestMethod] public void Loose_equal_to_number_fractional_is_false() => Eval("1n == 1.5").AsBool.Should().BeFalse();
    [TestMethod] public void Loose_equal_to_string() => Eval("1n == '1'").AsBool.Should().BeTrue();
    [TestMethod] public void Loose_unequal_to_unparseable_string() => Eval("1n == 'abc'").AsBool.Should().BeFalse();

    // ---------------------------------------------------------- Comparison
    [TestMethod] public void Compare_gt_bigint_bigint() => Eval("2n > 1n").AsBool.Should().BeTrue();
    [TestMethod] public void Compare_lt_bigint_number_integer() => Eval("1n < 2").AsBool.Should().BeTrue();
    [TestMethod] public void Compare_lt_bigint_number_fractional() => Eval("1n < 2.5").AsBool.Should().BeTrue();
    [TestMethod] public void Compare_lt_bigint_number_just_below() => Eval("2n < 1.5").AsBool.Should().BeFalse();

    // ---------------------------------------------------------- toString
    [TestMethod] public void Proto_toString_default_decimal() =>
        Eval("(255n).toString()").AsString.Should().Be("255");

    [TestMethod] public void Proto_toString_hex() =>
        Eval("(255n).toString(16)").AsString.Should().Be("ff");

    [TestMethod] public void Proto_toString_binary() =>
        Eval("(5n).toString(2)").AsString.Should().Be("101");

    [TestMethod] public void Proto_valueOf_returns_bigint() =>
        Eval("typeof (1n).valueOf()").AsString.Should().Be("bigint");

    // ----------------------------------------------------- asIntN / asUintN
    [TestMethod] public void AsIntN_8_negative_via_sign_bit() =>
        Eval("BigInt.asIntN(8, 200n) === -56n").AsBool.Should().BeTrue();

    [TestMethod] public void AsIntN_8_positive() =>
        Eval("BigInt.asIntN(8, 100n) === 100n").AsBool.Should().BeTrue();

    [TestMethod] public void AsUintN_8_overflow_wraps_to_zero() =>
        Eval("BigInt.asUintN(8, 256n) === 0n").AsBool.Should().BeTrue();

    [TestMethod] public void AsUintN_8_negative_wraps() =>
        Eval("BigInt.asUintN(8, -1n) === 255n").AsBool.Should().BeTrue();

    [TestMethod] public void AsIntN_negative_bits_throws() =>
        ((Action)(() => Eval("BigInt.asIntN(-1, 0n)"))).Should().Throw<JsThrow>();

    // ------------------------------------------------------- JSON.stringify
    [TestMethod] public void Json_stringify_throws() =>
        ((Action)(() => Eval("JSON.stringify(1n)"))).Should().Throw<JsThrow>();

    // ----------------------------------------------------- Helpers
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
