using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// Regression tests for the B2-2 follow-up sweep that migrated every intrinsic
/// (Math, JSON, console, String/Number/Boolean prototypes, global helpers,
/// <c>RegisterGlobal</c>) to the realm-aware <see cref="JsNativeFunction"/>
/// constructor so each method inherits from <c>Function.prototype</c>.
/// </summary>
[TestClass]
public class IntrinsicChainTests
{
    // ---------------------------------------------------------- Math chain

    [TestMethod]
    public void Math_max_bound_inherits_Function_prototype()
    {
        var rt = new JsRuntime();
        var r = RunWith(rt, "Math.max.bind(null, 1, 2) instanceof Function;");
        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Math_max_bound_call_returns_largest()
    {
        Eval("Math.max.bind(null, 1, 2)();").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Math_max_length_is_two()
    {
        Eval("Math.max.length;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Math_atan2_length_is_two()
    {
        Eval("Math.atan2.length;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Math_abs_length_is_one()
    {
        Eval("Math.abs.length;").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Math_pow_apply_threads_through_function_prototype()
    {
        Eval("Math.pow.apply(null, [2, 8]);").AsNumber.Should().Be(256);
    }

    // ---------------------------------------------------------- JSON chain

    [TestMethod]
    public void JSON_stringify_call_is_callable()
    {
        Eval("typeof JSON.stringify.call;").AsString.Should().Be("function");
    }

    [TestMethod]
    public void JSON_stringify_call_serializes_payload()
    {
        Eval("JSON.stringify.call(null, {a:1});").AsString.Should().Be("{\"a\":1}");
    }

    [TestMethod]
    public void JSON_parse_length_is_two()
    {
        Eval("JSON.parse.length;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void JSON_stringify_length_is_three()
    {
        Eval("JSON.stringify.length;").AsNumber.Should().Be(3);
    }

    // ---------------------------------------------------------- String chain

    [TestMethod]
    public void String_prototype_trim_callable_via_call()
    {
        Eval("String.prototype.trim.call('  hi  ');").AsString.Should().Be("hi");
    }

    [TestMethod]
    public void String_prototype_replace_bind_chain_is_callable()
    {
        Eval("typeof String.prototype.replace.bind;").AsString.Should().Be("function");
    }

    // ---------------------------------------------------------- Number chain

    [TestMethod]
    public void Number_isInteger_bound_evaluates_arg()
    {
        Eval("Number.isInteger.bind(null)(3);").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Number_isInteger_length_is_one()
    {
        Eval("Number.isInteger.length;").AsNumber.Should().Be(1);
    }

    // ---------------------------------------------------------- Boolean chain

    [TestMethod]
    public void Boolean_prototype_toString_callable_via_call()
    {
        Eval("typeof Boolean.prototype.toString.call;").AsString.Should().Be("function");
    }

    // ---------------------------------------------------------- console chain

    [TestMethod]
    public void Console_log_apply_is_a_function()
    {
        Eval("typeof console.log.apply;").AsString.Should().Be("function");
    }

    // ---------------------------------------------------------- Globals chain

    [TestMethod]
    public void Globals_parseInt_length_is_two()
    {
        Eval("parseInt.length;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Globals_parseFloat_length_is_one()
    {
        Eval("parseFloat.length;").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Globals_parseInt_bind_threads_through_function_prototype()
    {
        Eval("parseInt.bind(null, '42')();").AsNumber.Should().Be(42);
    }

    // ---------------------------------------------------------- RegisterGlobal

    [TestMethod]
    public void RegisterGlobal_args_only_overload_returns_callable_chain()
    {
        var rt = new JsRuntime();
        rt.RegisterGlobal("hostEcho", (JsValue[] args) => args.Length > 0 ? args[0] : JsValue.Undefined);
        var r = RunWith(rt, "typeof hostEcho.call;");
        r.AsString.Should().Be("function");
    }

    [TestMethod]
    public void RegisterGlobal_full_overload_returns_callable_chain()
    {
        var rt = new JsRuntime();
        rt.RegisterGlobal("hostAdd", (JsValue _, JsValue[] args) =>
            JsValue.Number((args.Length > 0 ? JsValue.ToNumber(args[0]) : 0)
                + (args.Length > 1 ? JsValue.ToNumber(args[1]) : 0)));
        var r = RunWith(rt, "typeof hostAdd.bind;");
        r.AsString.Should().Be("function");
    }

    [TestMethod]
    public void RegisterGlobal_function_inherits_Function_prototype()
    {
        var rt = new JsRuntime();
        rt.RegisterGlobal("hostFoo", (JsValue[] _) => JsValue.Undefined);
        var r = RunWith(rt, "hostFoo instanceof Function;");
        r.AsBool.Should().BeTrue();
    }

    // ---------------------------------------------------------- helpers

    private static JsValue Eval(string src) => RunWith(new JsRuntime(), src);

    private static JsValue RunWith(JsRuntime rt, string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(rt).Run(chunk);
    }
}
