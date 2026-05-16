using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

public class JsVmTests
{
    // ----- Primitive evaluation -------------------------------------------

    [Fact]
    public void Numeric_literal_evaluates_to_itself()
        => Eval("42;").AsNumber.Should().Be(42);

    [Fact]
    public void Arithmetic_addition()
        => Eval("1 + 2;").AsNumber.Should().Be(3);

    [Fact]
    public void Arithmetic_full_precedence()
        => Eval("1 + 2 * 3;").AsNumber.Should().Be(7);

    [Fact]
    public void Subtraction_and_division()
    {
        Eval("10 - 3;").AsNumber.Should().Be(7);
        Eval("10 / 4;").AsNumber.Should().Be(2.5);
    }

    [Fact]
    public void Modulo()
        => Eval("10 % 3;").AsNumber.Should().Be(1);

    [Fact]
    public void Exponentiation_right_associative()
        => Eval("2 ** 3 ** 2;").AsNumber.Should().Be(512); // 2 ^ (3^2) = 2^9

    [Fact]
    public void Unary_minus()
        => Eval("-(3 + 4);").AsNumber.Should().Be(-7);

    [Fact]
    public void String_concat()
        => Eval("'hello, ' + 'world';").AsString.Should().Be("hello, world");

    [Fact]
    public void String_plus_number_concatenates()
        => Eval("'x=' + 42;").AsString.Should().Be("x=42");

    // ----- Comparison -----------------------------------------------------

    [Fact]
    public void Strict_equality()
    {
        JsValue.ToBoolean(Eval("1 === 1;")).Should().BeTrue();
        JsValue.ToBoolean(Eval("1 === '1';")).Should().BeFalse();
    }

    [Fact]
    public void Abstract_equality_coerces()
    {
        JsValue.ToBoolean(Eval("1 == '1';")).Should().BeTrue();
        JsValue.ToBoolean(Eval("null == undefined;")).Should().BeTrue();
    }

    [Fact]
    public void Less_than_and_greater_than()
    {
        JsValue.ToBoolean(Eval("2 < 3;")).Should().BeTrue();
        JsValue.ToBoolean(Eval("3 > 2;")).Should().BeTrue();
        JsValue.ToBoolean(Eval("3 <= 3;")).Should().BeTrue();
    }

    [Fact]
    public void NaN_compares_unordered()
    {
        JsValue.ToBoolean(Eval("(0/0) < 1;")).Should().BeFalse();
        JsValue.ToBoolean(Eval("(0/0) === (0/0);")).Should().BeFalse();
    }

    // ----- Logical / typeof -----------------------------------------------

    [Fact]
    public void Logical_and_short_circuits()
        => Eval("0 && 'never';").AsNumber.Should().Be(0);

    [Fact]
    public void Logical_or_short_circuits()
        => Eval("'first' || 'never';").AsString.Should().Be("first");

    [Fact]
    public void Nullish_coalescing()
    {
        Eval("null ?? 'fallback';").AsString.Should().Be("fallback");
        Eval("0 ?? 'never';").AsNumber.Should().Be(0); // 0 is not nullish
    }

    [Fact]
    public void Typeof_each_kind()
    {
        Eval("typeof 1;").AsString.Should().Be("number");
        Eval("typeof 'x';").AsString.Should().Be("string");
        Eval("typeof true;").AsString.Should().Be("boolean");
        Eval("typeof undefined;").AsString.Should().Be("undefined");
        Eval("typeof null;").AsString.Should().Be("object");
    }

    // ----- Variables ------------------------------------------------------

    [Fact]
    public void Var_declaration_and_reference()
        => Eval("var x = 7; x;").AsNumber.Should().Be(7);

    [Fact]
    public void Let_declaration_default_undefined()
        => Eval("let x; x;").IsUndefined.Should().BeTrue();

    [Fact]
    public void Assignment_propagates_value()
        => Eval("var x = 1; x = x + 10;").AsNumber.Should().Be(11);

    [Fact]
    public void Compound_assignment()
        => Eval("var x = 5; x *= 3;").AsNumber.Should().Be(15);

    [Fact]
    public void Postfix_increment_yields_old_value_pops_pop()
    {
        // The expression value of `x++` is the original; x is updated.
        var runtime = new JsRuntime();
        var compiled = JsCompiler.CompileForEval(new JsParser(
            "var x = 5; var y = x++;").ParseProgram());
        new JsVm(runtime).Run(compiled);
        // Read back via a second script
        var v = Eval("var x = 5; var y = x++; y;");
        v.AsNumber.Should().Be(5);
        Eval("var x = 5; x++; x;").AsNumber.Should().Be(6);
    }

    // ----- Control flow ---------------------------------------------------

    [Fact]
    public void If_else_chooses_branch()
    {
        Eval("var r = 0; if (true) r = 1; else r = 2; r;").AsNumber.Should().Be(1);
        Eval("var r = 0; if (false) r = 1; else r = 2; r;").AsNumber.Should().Be(2);
    }

    [Fact]
    public void While_loop_sums_values()
    {
        // Sum 1..10
        var v = Eval(@"
            var i = 1, s = 0;
            while (i <= 10) {
                s = s + i;
                i = i + 1;
            }
            s;
        ");
        v.AsNumber.Should().Be(55);
    }

    [Fact]
    public void Conditional_expression()
        => Eval("var x = 5; x > 0 ? 'pos' : 'neg';").AsString.Should().Be("pos");

    // ----- Globals + host functions ---------------------------------------

    [Fact]
    public void Host_function_callable_from_JS()
    {
        var runtime = new JsRuntime();
        var captured = new List<JsValue>();
        runtime.RegisterGlobal("print", args => { captured.AddRange(args); return JsValue.Undefined; });
        new JsVm(runtime).Run(
            JsCompiler.CompileForEval(new JsParser("print(1 + 2);").ParseProgram()));
        captured.Should().ContainSingle().Which.AsNumber.Should().Be(3);
    }

    [Fact]
    public void Multiple_host_args_pass_in_order()
    {
        var runtime = new JsRuntime();
        var captured = new List<JsValue>();
        runtime.RegisterGlobal("collect",
            args => { captured.AddRange(args); return JsValue.Number(args.Length); });
        var r = new JsVm(runtime).Run(
            JsCompiler.CompileForEval(new JsParser("collect(1, 'two', true);").ParseProgram()));
        r.AsNumber.Should().Be(3);
        captured.Should().HaveCount(3);
        captured[0].AsNumber.Should().Be(1);
        captured[1].AsString.Should().Be("two");
        captured[2].AsBool.Should().BeTrue();
    }

    [Fact]
    public void Member_access_on_global_object()
    {
        var runtime = new JsRuntime();
        var math = new JsObject();
        math.Set("PI", JsValue.Number(Math.PI));
        math.Set("E", JsValue.Number(Math.E));
        runtime.SetGlobal("Math", JsValue.Object(math));
        var pi = new JsVm(runtime).Run(
            JsCompiler.CompileForEval(new JsParser("Math.PI;").ParseProgram()));
        pi.AsNumber.Should().Be(Math.PI);
    }

    // ----- Bitwise + Coercion ---------------------------------------------

    [Fact]
    public void Bitwise_ops()
    {
        Eval("0xF | 0x1;").AsNumber.Should().Be(15);
        Eval("0xF & 0x3;").AsNumber.Should().Be(3);
        Eval("0xF ^ 0xA;").AsNumber.Should().Be(5);
        Eval("~0;").AsNumber.Should().Be(-1);
    }

    [Fact]
    public void Shift_operators()
    {
        Eval("1 << 4;").AsNumber.Should().Be(16);
        Eval("16 >> 2;").AsNumber.Should().Be(4);
        Eval("-1 >>> 28;").AsNumber.Should().Be(15);
    }

    // ----- Smoke test: real-ish algorithm ---------------------------------

    [Fact]
    public void Fibonacci_iterative()
    {
        // Compute fib(10) = 55 with a while loop.
        var v = Eval(@"
            var n = 10;
            var a = 0, b = 1;
            var i = 0;
            while (i < n) {
                var t = a + b;
                a = b;
                b = t;
                i = i + 1;
            }
            a;
        ");
        v.AsNumber.Should().Be(55);
    }

    // ----- Throw ----------------------------------------------------------

    [Fact]
    public void Uncaught_throw_surfaces_as_JsThrow()
    {
        var act = () => Eval("throw 'boom';");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Be("boom");
    }

    // ----- Helpers --------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
