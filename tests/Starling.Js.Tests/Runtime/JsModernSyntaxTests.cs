using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// End-to-end (parse → compile → run) tests for B1b-1's modern syntax slice:
/// template literals and arrow functions.
/// </summary>
public class JsModernSyntaxTests
{
    // ----------------------------------------------------- Template literals

    [Fact]
    public void Template_no_substitution_yields_string()
    {
        Eval("`hello world`;").AsString.Should().Be("hello world");
    }

    [Fact]
    public void Template_single_substitution()
    {
        Eval("var n = 7; `n is ${n}!`;").AsString.Should().Be("n is 7!");
    }

    [Fact]
    public void Template_multiple_substitutions_with_middle()
    {
        Eval("var a = 1, b = 2; `${a}+${b}=${a+b}`;").AsString.Should().Be("1+2=3");
    }

    [Fact]
    public void Template_with_arithmetic_substitution()
    {
        Eval("`sum: ${1 + 2 * 3}`;").AsString.Should().Be("sum: 7");
    }

    [Fact]
    public void Template_preserves_literal_newlines()
    {
        Eval("`one\ntwo`;").AsString.Should().Be("one\ntwo");
    }

    // -------------------------------------------------------- Arrow functions

    [Fact]
    public void Arrow_concise_body_single_param_no_parens()
    {
        // x => x * 2
        Eval("var dbl = x => x * 2; dbl(21);").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Arrow_concise_body_parenthesized_params()
    {
        Eval("var add = (a, b) => a + b; add(3, 4);").AsNumber.Should().Be(7);
    }

    [Fact]
    public void Arrow_block_body_with_return()
    {
        Eval("var f = (x) => { return x + 1; }; f(10);").AsNumber.Should().Be(11);
    }

    [Fact]
    public void Arrow_with_zero_params()
    {
        Eval("var nul = () => 42; nul();").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Arrow_inside_call_argument()
    {
        Eval(@"
            function apply(fn, x) { return fn(x); }
            apply(n => n * n, 5);
        ").AsNumber.Should().Be(25);
    }

    // -------------------------------------------------- Object literal extras

    [Fact]
    public void Object_method_shorthand_callable_on_receiver()
    {
        var v = Eval(@"
            var o = {
                x: 7,
                add(n) { return this.x + n; }
            };
            o.add(3);
        ");
        v.AsNumber.Should().Be(10);
    }

    [Fact]
    public void Object_shorthand_property_uses_outer_binding()
    {
        Eval(@"
            var name = 'starling';
            var version = 1;
            var pkg = { name, version };
            pkg.name + '-' + pkg.version;
        ").AsString.Should().Be("starling-1");
    }

    [Fact]
    public void Object_spread_copies_enumerable_own_props()
    {
        Eval(@"
            var a = { x: 1, y: 2 };
            var b = { ...a, y: 99, z: 3 };
            b.x + ',' + b.y + ',' + b.z;
        ").AsString.Should().Be("1,99,3");
    }

    [Fact]
    public void Computed_object_key_resolves_dynamically()
    {
        Eval(@"
            var k = 'dyn';
            var o = { [k + 'Key']: 42 };
            o.dynKey;
        ").AsNumber.Should().Be(42);
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
