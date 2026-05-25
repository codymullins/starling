using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// End-to-end parse → compile → run coverage for ECMA-262 §13.15
/// destructuring assignment and §14.3.3 destructuring binding patterns.
/// </summary>
[TestClass]
public class JsDestructuringTests
{
    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
    public void Assignment_patterns_write_existing_bindings_and_return_rhs()
    {
        Eval("var a = 0, b = 0; [a, b] = [8, 9]; a * 10 + b;").AsNumber.Should().Be(89);
        Eval("var x = 0, y = 0; ({x, y} = {x: 2, y: 3}); x * 10 + y;").AsNumber.Should().Be(23);
        Eval("var a = 0, b = 0, r; [a = 1, ...r] = [undefined, 5, 6]; a * 100 + r.length * 10 + r[1];").AsNumber.Should().Be(126);
        Eval("var b = 0, rest; ({a: {b = 4}, ...rest} = {a: {}, c: 7}); b * 10 + rest.c;").AsNumber.Should().Be(47);
        Eval("var obj = {a: 1, b: 2}; var r = ({a, b} = obj); r === obj;").AsBool.Should().BeTrue();
    }


    [TestMethod]
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

    [TestMethod]
    public void Assignment_patterns_cover_holes_renames_computed_keys_and_nested_targets()
    {
        // Array assignment: holes and array-in-array nesting writing existing bindings.
        Eval("var a = 0; [, a] = [1, 2]; a;").AsNumber.Should().Be(2);
        Eval("var a = 0, b = 0; [[a], { b }] = [[1], { b: 2 }]; a * 10 + b;").AsNumber.Should().Be(12);
        // Rest into an existing binding.
        Eval("var a = 0, r; [a, ...r] = [1, 2, 3]; a * 100 + r.length * 10 + r[1];").AsNumber.Should().Be(123);
        // Object assignment: rename + default + computed key targeting existing bindings.
        Eval("var x1 = 0; ({ a: x1 } = { a: 7 }); x1;").AsNumber.Should().Be(7);
        Eval("var a = 0; ({ a = 9 } = {}); a;").AsNumber.Should().Be(9);
        Eval("var k = 'z', v = 0; ({ [k]: v } = { z: 4 }); v;").AsNumber.Should().Be(4);
        Eval("var a = 0, r; ({ a, ...r } = { a: 1, b: 2, c: 3 }); a * 100 + r.b * 10 + r.c;").AsNumber.Should().Be(123);
        // Short RHS yields undefined for missing elements.
        Eval("var a = 0, b = 0, c = 0; [a, b, c] = [1, 2]; (c === undefined ? 1 : 0) * 100 + a * 10 + b;").AsNumber.Should().Be(112);
    }

    [TestMethod]
    public void Assignment_pattern_member_targets_cover_dotted_and_computed_forms()
    {
        // Mixed identifier + dotted-member + computed-member targets in one array pattern.
        Eval("var obj = {}, arr = [], i = 0; [obj.a, arr[i]] = [5, 6]; obj.a * 10 + arr[0];").AsNumber.Should().Be(56);
        // Computed-member target inside an array pattern, with a hole before it.
        Eval("var arr = [], i = 1; [, arr[i]] = [9, 8]; arr[1];").AsNumber.Should().Be(8);
        // Object pattern writing through a member target (renamed key -> member).
        Eval("var obj = {}; ({ p: obj.a } = { p: 8 }); obj.a;").AsNumber.Should().Be(8);
        // Nested array pattern under an object property, target with default, writing a member.
        Eval("var o = {}; ({ x: [o.a = 3] } = { x: [] }); o.a;").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Assignment_pattern_evaluation_order_is_left_to_right()
    {
        // §13.15.5: member-target reference (base + computed key) is resolved in
        // source order as each element is processed, left to right.
        Eval("var log = ''; function L(n) { log += n; return {}; } [L('a').p, L('b').q] = [1, 2]; log;")
            .AsString.Should().Be("ab");
        // Computed-key member target evaluates the key expression.
        Eval("var log = ''; var o = {}; function K() { log += 'k'; return 'p'; } [o[K()]] = [5]; log + o.p;")
            .AsString.Should().Be("k5");
        // The destructuring assignment expression evaluates to (and returns) the RHS itself.
        Eval("var rhs = { a: 1 }; var x = 0; var res = ({ a: x } = rhs); res === rhs;").AsBool.Should().BeTrue();
        Eval("var a = 0, b = 0; var rhs = [3, 4]; var res = ([a, b] = rhs); res === rhs;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Compound_operator_with_destructuring_target_is_rejected()
    {
        // §13.15.1: a destructuring pattern only pairs with the plain `=` operator;
        // any compound form is an early SyntaxError (surfaced as JsParseException).
        var compile = () =>
        {
            var program = new JsParser("var a = 0; [a] += [1];").ParseProgram();
            JsCompiler.CompileForEval(program);
        };
        compile.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Arrow_captures_enclosing_method_this_lexically()
    {
        // §10.2.1.1 / §13.2.5 — an arrow has no own `this`; it resolves `this`
        // to the nearest enclosing ordinary function (here the method `m`). The
        // earlier compiler used the arrow's call-time `this`, yielding undefined;
        // wp:M3-77 makes the arrow capture the method's `this` as an upvalue.
        Eval("var o = { x: 3, m() { var f = () => this.x; return f(); } }; o.m();")
            .AsNumber.Should().Be(3);
        Eval("var o = { m() { var f = () => this; return f() === this; } }; o.m();")
            .AsBool.Should().BeTrue();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
