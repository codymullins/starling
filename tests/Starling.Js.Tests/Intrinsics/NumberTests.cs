using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class NumberTests
{
    [TestMethod]
    public void Number_constructor_coerces_and_boxes()
    {
        Eval("Number('');").AsNumber.Should().Be(0);
        Eval("Number('0x10');").AsNumber.Should().Be(16);
        Eval("Number(true);").AsNumber.Should().Be(1);
        Eval("Number(null);").AsNumber.Should().Be(0);
        double.IsNaN(Eval("Number(undefined);").AsNumber).Should().BeTrue();
        Eval("var n = new Number(7); n.valueOf();").AsNumber.Should().Be(7);
        Eval("var n = new Number(7); n.toString();").AsString.Should().Be("7");
        Eval("Number.prototype.valueOf.callMe;").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Number_constants_are_registered()
    {
        Eval("Number.MAX_SAFE_INTEGER;").AsNumber.Should().Be(9007199254740991d);
        Eval("Number.MIN_SAFE_INTEGER;").AsNumber.Should().Be(-9007199254740991d);
        Eval("Number.POSITIVE_INFINITY;").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("Number.NEGATIVE_INFINITY;").AsNumber.Should().Be(double.NegativeInfinity);
        double.IsNaN(Eval("Number.NaN;").AsNumber).Should().BeTrue();
        Eval("Number.MAX_VALUE;").AsNumber.Should().Be(double.MaxValue);
        Eval("Number.MIN_VALUE;").AsNumber.Should().Be(double.Epsilon);
        Eval("Number.EPSILON;").AsNumber.Should().Be(Math.Pow(2, -52));
    }

    [TestMethod]
    public void Number_statics_classify_without_global_coercion()
    {
        Eval("Number.isFinite(3);").AsBool.Should().BeTrue();
        Eval("Number.isFinite('3');").AsBool.Should().BeFalse();
        Eval("Number.isFinite(Infinity);").AsBool.Should().BeFalse();
        Eval("Number.isNaN(NaN);").AsBool.Should().BeTrue();
        Eval("Number.isNaN('NaN');").AsBool.Should().BeFalse();
        Eval("Number.isInteger(1.0);").AsBool.Should().BeTrue();
        Eval("Number.isInteger(1.5);").AsBool.Should().BeFalse();
        Eval("Number.isSafeInteger(9007199254740991);").AsBool.Should().BeTrue();
        Eval("Number.isSafeInteger(9007199254740992);").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Number_parse_methods_match_globals()
    {
        Eval("Number.parseInt('0x10');").AsNumber.Should().Be(16);
        Eval("Number.parseInt('  -42abc', 10);").AsNumber.Should().Be(-42);
        Eval("Number.parseInt('101', 2);").AsNumber.Should().Be(5);
        double.IsNaN(Eval("Number.parseInt('zzz', 10);").AsNumber).Should().BeTrue();
        Eval("Number.parseFloat('  -3.5px');").AsNumber.Should().Be(-3.5);
        Eval("Number.parseFloat('1.25e2!');").AsNumber.Should().Be(125);
        Eval("Number.parseFloat('Infinity');").AsNumber.Should().Be(double.PositiveInfinity);
        double.IsNaN(Eval("Number.parseFloat('abc');").AsNumber).Should().BeTrue();
    }

    [TestMethod]
    public void Number_prototype_formats_values()
    {
        Eval("(255).toString(16);").AsString.Should().Be("ff");
        Eval("(-10).toString(2);").AsString.Should().Be("-1010");
        Eval("(0.1 + 0.2).toFixed(2);").AsString.Should().Be("0.30");
        Eval("(12.345).toFixed(1);").AsString.Should().Be("12.3");
        Eval("(1234).toExponential(2);").AsString.Should().Be("1.23e+3");
        Eval("(12.345).toPrecision(4);").AsString.Should().Be("12.35");
        Eval("(42).toLocaleString();").AsString.Should().Be("42");
        Eval("(42).valueOf();").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Number_methods_throw_on_invalid_receivers_and_ranges()
    {
        Action badReceiver = () => Eval("Number.prototype.valueOf();");
        Action badRadix = () => Eval("(1).toString(1);");
        Action badDigits = () => Eval("(1).toFixed(101);");
        badReceiver.Should().Throw<JsThrow>();
        badRadix.Should().Throw<JsThrow>();
        badDigits.Should().Throw<JsThrow>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
