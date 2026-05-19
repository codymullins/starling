using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// End-to-end parse → compile → run coverage for ECMA-262 §13.15
/// destructuring assignment and §14.3.3 destructuring binding patterns.
/// </summary>
public class JsDestructuringTests
{
    [Fact]
    public void Array_binding_patterns_cover_defaults_holes_rest_and_nested()
    {
        Eval("let [a, b] = [1, 2]; a + b;").AsNumber.Should().Be(3);
        Eval("const [x, y, z] = [3, 4, 5]; x * 100 + y * 10 + z;").AsNumber.Should().Be(345);
        Eval("var [p, q] = [6, 7]; p + q;").AsNumber.Should().Be(13);
        Eval("let [a = 1, b = 2] = [undefined, 9]; a * 10 + b;").AsNumber.Should().Be(19);
        Eval("let [a = 1, b = 2] = [null, undefined]; (a === null ? 10 : 0) + b;").AsNumber.Should().Be(12);
        Eval("let [, , c] = [1, 2, 3]; c;").AsNumber.Should().Be(3);
        Eval("let [head, ...tail] = [1, 2, 3, 4]; head * 100 + tail.length * 10 + tail[0] + tail[2];").AsNumber.Should().Be(136);
        Eval("let [head, ...tail] = [1]; tail.length;").AsNumber.Should().Be(0);
        Eval("let [[a, b], c] = [[4, 5], 6]; a * 100 + b * 10 + c;").AsNumber.Should().Be(456);
    }

    [Fact]
    public void Object_binding_patterns_cover_shorthand_renames_defaults_computed_rest_and_nested()
    {
        Eval("let {x, y} = {x: 1, y: 2}; x + y;").AsNumber.Should().Be(3);
        Eval("let {x: a, y: b} = {x: 3, y: 4}; a * 10 + b;").AsNumber.Should().Be(34);
        Eval("let {x = 1, y: b = 2} = {x: undefined, y: 8}; x * 10 + b;").AsNumber.Should().Be(18);
        Eval("let {x = 1, y: b = 2} = {x: null}; (x === null ? 10 : 0) + b;").AsNumber.Should().Be(12);
        Eval("let k = 'dyn'; let {[k]: v} = {dyn: 42}; v;").AsNumber.Should().Be(42);
        Eval("let {a, b, ...rest} = {a: 1, b: 2, c: 3, d: 4}; rest.c * 10 + rest.d;").AsNumber.Should().Be(34);
        Eval("let {...rest} = {}; rest.length === undefined && rest.x === undefined;").AsBool.Should().BeTrue();
        Eval("let {a: {b, c}} = {a: {b: 5, c: 6}}; b * 10 + c;").AsNumber.Should().Be(56);
        Eval("let {arr: [x, y]} = {arr: [7, 8]}; x * 10 + y;").AsNumber.Should().Be(78);
        Eval("let {a: [x = 1, ...r], z = 9, ...rest} = {a: [undefined, 2, 3], keep: 4}; x * 1000 + r.length * 100 + r[1] * 10 + z + rest.keep;").AsNumber.Should().Be(1243);
    }

    [Fact]
    public void Function_and_arrow_parameters_can_destructure()
    {
        Eval("function f({x, y}, [a, b = 5]) { return x + y + a + b; } f({x: 1, y: 2}, [3]);").AsNumber.Should().Be(11);
        Eval("function f({x = 1} = {}) { return x; } f();").AsNumber.Should().Be(1);
        Eval("function f([a = 2, b] = [undefined, 3]) { return a * 10 + b; } f();").AsNumber.Should().Be(23);
        Eval("function f({x = 1} = {}) { return x; } f({x: 9});").AsNumber.Should().Be(9);
        Eval("var add = ({x, y}) => x + y; add({x: 4, y: 5});").AsNumber.Should().Be(9);
        Eval("var first = ([a, b = 6]) => a * 10 + b; first([7]);").AsNumber.Should().Be(76);
        Eval("function f({x}, [a, b = 5]) { return (x === undefined ? 10 : x) + (a === undefined ? 20 : a) + b; } f({}, []);").AsNumber.Should().Be(35);
    }

    [Fact]
    public void Assignment_patterns_write_existing_bindings_and_return_rhs()
    {
        Eval("var a = 0, b = 0; [a, b] = [8, 9]; a * 10 + b;").AsNumber.Should().Be(89);
        Eval("var x = 0, y = 0; ({x, y} = {x: 2, y: 3}); x * 10 + y;").AsNumber.Should().Be(23);
        Eval("var a = 0, b = 0, r; [a = 1, ...r] = [undefined, 5, 6]; a * 100 + r.length * 10 + r[1];").AsNumber.Should().Be(126);
        Eval("var b = 0, rest; ({a: {b = 4}, ...rest} = {a: {}, c: 7}); b * 10 + rest.c;").AsNumber.Should().Be(47);
        Eval("var obj = {a: 1, b: 2}; var r = ({a, b} = obj); r === obj;").AsBool.Should().BeTrue();
    }


    [Fact]
    public void Destructuring_edge_cases_cover_missing_values_members_and_computed_rest()
    {
        Eval("let [a = 1] = []; a;").AsNumber.Should().Be(1);
        Eval("let [a = 1] = [undefined]; a;").AsNumber.Should().Be(1);
        Eval("let [a = 1] = [null]; a === null;").AsBool.Should().BeTrue();
        Eval("let [,,] = []; 42;").AsNumber.Should().Be(42);
        Eval("let [a,,b = 4, ...r] = [1, 2, undefined, 5, 6]; a * 1000 + b * 100 + r.length * 10 + r[1];").AsNumber.Should().Be(1426);
        Eval("let {missing: value = 6} = {}; value;").AsNumber.Should().Be(6);
        Eval("let {a: {b = 2} = {}} = {}; b;").AsNumber.Should().Be(2);
        Eval("let {a: [x = 3] = []} = {}; x;").AsNumber.Should().Be(3);
        Eval("let k = 'x'; let {[k]: v = 7} = {}; v;").AsNumber.Should().Be(7);
        Eval("let k = 'a'; let {[k]: v, ...r} = {a: 1, b: 2}; v * 10 + r.b;").AsNumber.Should().Be(12);
        Eval("let k = 'a'; let {[k]: v, ...r} = {a: 1, b: 2}; r.a === undefined;").AsBool.Should().BeTrue();
        Eval("var o = {}; [o.a] = [3]; o.a;").AsNumber.Should().Be(3);
        Eval("var o = {}; ({x: o.x} = {x: 4}); o.x;").AsNumber.Should().Be(4);
        Eval("var a = 0; ({x: [a = 8]} = {x: []}); a;").AsNumber.Should().Be(8);
        Eval("var rest; ({...rest} = {}); rest.foo === undefined;").AsBool.Should().BeTrue();
        Eval("function f([a = 1]) { return a; } f([]);").AsNumber.Should().Be(1);
        Eval("function f({a: [x, ...r]}) { return x * 100 + r.length * 10 + r[0]; } f({a: [5, 6, 7]});").AsNumber.Should().Be(526);
        Eval("var f = ([a] = [9]) => a; f();").AsNumber.Should().Be(9);
        Eval("var f = ({x = 10} = {}) => x; f({x: undefined});").AsNumber.Should().Be(10);
        Eval("var a = 1, b = 2; [b, a] = [a, b]; a * 10 + b;").AsNumber.Should().Be(21);
    }

    [Fact]
    public void Arrow_this_binding_gap_is_pinned_for_follow_up_work()
    {
        Eval("var o = { x: 3, m() { var f = () => this.x; return f(); } }; o.m() === undefined;").AsBool.Should().BeTrue();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
