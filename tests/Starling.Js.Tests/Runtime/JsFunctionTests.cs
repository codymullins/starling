using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

public class JsFunctionTests
{
    [Fact]
    public void Function_declaration_without_call_returns_undefined()
    {
        // Declaring a function without invoking it leaves nothing on the
        // expression stack — the script halts on Undefined.
        Eval("function f() { return 1; }").IsUndefined.Should().BeTrue();
    }

    [Fact]
    public void Function_call_returns_value()
        => Eval("function f() { return 42; } f();").AsNumber.Should().Be(42);

    [Fact]
    public void Function_with_two_parameters()
        => Eval("function add(a, b) { return a + b; } add(2, 3);")
            .AsNumber.Should().Be(5);

    [Fact]
    public void Function_with_locals()
        => Eval(@"
            function poly(x) {
                var sq = x * x;
                var cu = sq * x;
                return sq + cu;
            }
            poly(3);
        ").AsNumber.Should().Be(36); // 9 + 27

    [Fact]
    public void Bare_return_yields_undefined()
        => Eval("function f() { return; } f();")
            .IsUndefined.Should().BeTrue();

    [Fact]
    public void Implicit_return_undefined()
        => Eval("function f() {} f();")
            .IsUndefined.Should().BeTrue();

    [Fact]
    public void Recursive_factorial()
        => Eval(@"
            function fact(n) {
                if (n <= 1) return 1;
                return n * fact(n - 1);
            }
            fact(5);
        ").AsNumber.Should().Be(120);

    [Fact]
    public void Recursive_fibonacci()
        => Eval(@"
            function fib(n) {
                if (n < 2) return n;
                return fib(n - 1) + fib(n - 2);
            }
            fib(10);
        ").AsNumber.Should().Be(55);

    [Fact]
    public void Function_hoisting_call_before_declaration_works()
    {
        // function declarations are hoisted, so calling them before their
        // textual position should still work.
        Eval(@"
            var x = f(7);
            function f(n) { return n + 1; }
            x;
        ").AsNumber.Should().Be(8);
    }

    [Fact]
    public void Nested_function_calls()
        => Eval(@"
            function inc(x) { return x + 1; }
            function double(x) { return x * 2; }
            double(inc(4));
        ").AsNumber.Should().Be(10);

    [Fact]
    public void Function_value_stored_in_variable_is_callable()
        => Eval(@"
            function f(x) { return x + 100; }
            var g = f;
            g(5);
        ").AsNumber.Should().Be(105);

    [Fact]
    public void Extra_args_ignored_missing_params_undefined()
    {
        // f(a, b) called as f(1) — b is undefined.
        Eval(@"
            function f(a, b) { return typeof b; }
            f(1);
        ").AsString.Should().Be("undefined");

        // f(a) called as f(1, 2, 99) — extras ignored.
        Eval(@"
            function f(a) { return a; }
            f(1, 2, 99);
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Function_can_call_host_native()
    {
        var runtime = new JsRuntime();
        var captured = new List<double>();
        runtime.RegisterGlobal("record",
            args => { captured.Add(args[0].AsNumber); return JsValue.Undefined; });

        var program = new JsParser(@"
            function emit(x) { record(x * 10); }
            emit(1); emit(2); emit(3);
        ").ParseProgram();
        new JsVm(runtime).Run(JsCompiler.Compile(program));

        captured.Should().Equal(10.0, 20.0, 30.0);
    }

    [Fact]
    public void Calling_non_function_throws()
    {
        var act = () => Eval("var x = 42; x();");
        act.Should().Throw<JsThrow>();
    }

    // ----- Helpers --------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
