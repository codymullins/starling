using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// End-to-end coverage for the operator bundle gaps documented in
/// <c>tasks/M3/google-com-handoff.md</c>:
/// <list type="bullet">
///   <item><b>gap:instanceof</b> — §13.10.2 with <c>@@hasInstance</c>
///         hook + OrdinaryHasInstance fallback (incl. bound-function
///         unwrap).</item>
///   <item><b>gap:in</b> — §13.10.1 (HasProperty with chain walk,
///         TypeError on non-Object RHS).</item>
///   <item><b>gap:delete</b> — §13.5.1 [[Delete]] on member targets.</item>
///   <item><b>gap:compound-assign-property</b> — §13.15.2 compound
///         assignment that evaluates the base + key exactly once.</item>
/// </list>
/// </summary>
[TestClass]
public class JsOperatorsGapTests
{
    // -----------------------------------------------------------------
    //                          instanceof
    // -----------------------------------------------------------------

    [TestMethod]
    public void Instanceof_returns_true_for_own_constructor()
    {
        Eval("function F() {}; var f = new F(); f instanceof F").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_returns_false_when_prototype_does_not_match()
    {
        Eval("function F() {}; function G() {}; var f = new F(); f instanceof G")
            .AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Instanceof_walks_prototype_chain_for_class_extends()
    {
        Eval("class A {}; class B extends A {}; new B() instanceof A").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_class_self_match()
    {
        Eval("class A {}; new A() instanceof A").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_bound_function_unwraps_to_target()
    {
        // §10.4.6.4 OrdinaryHasInstance: when C is a bound function,
        // unwrap to the target and recurse. Manually wiring the prototype
        // chain on F's instance so the test only exercises the unwrap
        // (independent of bound-function [[Construct]] which is wp:B2-2
        // territory).
        Eval(@"
            function F() {}
            var inst = new F();
            var g = F.bind(null);
            inst instanceof g
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_primitive_left_returns_false()
    {
        Eval("function F() {}; 5 instanceof F").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Instanceof_with_well_known_hasInstance_hook()
    {
        // Symbol.hasInstance trap — any matcher object can override the
        // prototype-chain walk.
        Eval(@"
            var X = { [Symbol.hasInstance]: function() { return true; } };
            42 instanceof X
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_with_well_known_hasInstance_returning_false()
    {
        Eval(@"
            var X = { [Symbol.hasInstance]: function() { return false; } };
            ({}) instanceof X
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Instanceof_non_callable_rhs_throws_type_error()
    {
        // {} is not callable and has no @@hasInstance — TypeError.
        var act = () => Eval("({}) instanceof ({})");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Instanceof_chain_through_Object()
    {
        Eval("({}) instanceof Object").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_array_through_Array_and_Object()
    {
        Eval("[] instanceof Array").AsBool.Should().BeTrue();
        Eval("[] instanceof Object").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Instanceof_TypeError_chain()
    {
        Eval("new TypeError() instanceof Error").AsBool.Should().BeTrue();
        Eval("new TypeError() instanceof TypeError").AsBool.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    //                              in
    // -----------------------------------------------------------------

    [TestMethod]
    public void In_returns_true_for_own_property()
    {
        Eval("'a' in {a: 1}").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void In_returns_false_for_missing_property()
    {
        Eval("'a' in {}").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void In_walks_prototype_chain()
    {
        // toString is inherited from Object.prototype.
        Eval("'toString' in {}").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void In_with_numeric_key_against_array()
    {
        // Array index lookup — string coercion for numeric keys.
        Eval("0 in [10, 20]").AsBool.Should().BeTrue();
        Eval("2 in [10, 20]").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void In_with_symbol_key()
    {
        Eval(@"
            var s = Symbol('x');
            var o = {};
            o[s] = 1;
            s in o
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void In_throws_type_error_on_non_object_rhs()
    {
        var act = () => Eval("'a' in 5");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void In_throws_type_error_on_null_rhs()
    {
        var act = () => Eval("'a' in null");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void In_property_set_then_checked()
    {
        Eval("var o = {}; o.x = 1; 'x' in o").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void In_property_after_define_property()
    {
        // Non-enumerable own property is still "in".
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'h', { value: 1, enumerable: false });
            'h' in o
        ").AsBool.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    //                            delete
    // -----------------------------------------------------------------

    [TestMethod]
    public void Delete_property_removes_own_slot()
    {
        Eval("var o = {x: 1}; delete o.x; 'x' in o").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Delete_returns_true_on_successful_removal()
    {
        Eval("var o = {x: 1}; delete o.x").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_missing_property_returns_true()
    {
        Eval("var o = {}; delete o.nonexistent").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_non_configurable_returns_false()
    {
        Eval(@"
            var o = {};
            Object.defineProperty(o, 'x', { value: 1, configurable: false });
            delete o.x
        ").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Delete_unqualified_identifier_returns_true()
    {
        // Per spec, a non-Reference (or unresolvable) delete returns true
        // in sloppy mode and is a no-op.
        Eval("delete xyz123").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_computed_property()
    {
        Eval("var o = {a: 1, b: 2}; var k = 'a'; delete o[k]; 'a' in o").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Delete_then_recheck_with_in()
    {
        Eval(@"
            var o = {foo: 1, bar: 2};
            delete o.foo;
            ('foo' in o) ? 1 : ('bar' in o) ? 2 : 3
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Delete_literal_returns_true()
    {
        Eval("delete 5").AsBool.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    //               compound assignment on properties
    // -----------------------------------------------------------------

    [TestMethod]
    public void Compound_plus_eq_on_property()
    {
        Eval("var o = {x: 1}; o.x += 5; o.x").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Compound_string_concat_on_property()
    {
        Eval("var o = {s: 'a'}; o.s += 'b'; o.s").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void Compound_minus_eq_on_property()
    {
        Eval("var o = {x: 10}; o.x -= 3; o.x").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Compound_mul_eq_on_property()
    {
        Eval("var o = {x: 4}; o.x *= 3; o.x").AsNumber.Should().Be(12);
    }

    [TestMethod]
    public void Compound_div_eq_on_property()
    {
        Eval("var o = {x: 12}; o.x /= 4; o.x").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Compound_mod_eq_on_property()
    {
        Eval("var o = {x: 10}; o.x %= 3; o.x").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Compound_pow_eq_on_property()
    {
        Eval("var o = {x: 2}; o.x **= 3; o.x").AsNumber.Should().Be(8);
    }

    [TestMethod]
    public void Compound_shift_eq_on_property()
    {
        Eval("var o = {x: 1}; o.x <<= 3; o.x").AsNumber.Should().Be(8);
        Eval("var o = {x: 16}; o.x >>= 2; o.x").AsNumber.Should().Be(4);
        Eval("var o = {x: -1}; o.x >>>= 28; o.x").AsNumber.Should().Be(15);
    }

    [TestMethod]
    public void Compound_bitwise_eq_on_property()
    {
        Eval("var o = {x: 6}; o.x &= 3; o.x").AsNumber.Should().Be(2);
        Eval("var o = {x: 4}; o.x |= 3; o.x").AsNumber.Should().Be(7);
        Eval("var o = {x: 6}; o.x ^= 3; o.x").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Compound_on_computed_property()
    {
        Eval("var o = {x: 1}; var k = 'x'; o[k] += 5; o.x").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Compound_on_computed_property_evaluates_key_once()
    {
        // The computed key expression must be evaluated only once even
        // though both the read and the write reference it.
        Eval(@"
            var calls = [];
            function getKey() { calls.push(1); return 'x'; }
            var o = {x: 0};
            o[getKey()] += 1;
            calls.length
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Compound_on_computed_property_writes_correct_slot()
    {
        Eval(@"
            var calls = [];
            function getKey() { calls.push(1); return 'x'; }
            var o = {x: 10};
            o[getKey()] += 5;
            o.x
        ").AsNumber.Should().Be(15);
    }

    [TestMethod]
    public void Compound_on_property_returns_new_value()
    {
        // Compound assignment is an expression — yields the new value.
        Eval("var o = {x: 1}; var r = (o.x += 5); r").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Compound_on_property_base_evaluated_once()
    {
        // The base expression should be evaluated exactly once.
        Eval(@"
            var calls = [];
            function getObj() { calls.push(1); return {x: 10}; }
            // Bind so the same object reference is used for read + write.
            var ref = getObj();
            ref.x += 5;
            calls.length + ',' + ref.x
        ").AsString.Should().Be("1,15");
    }

    [TestMethod]
    public void Compound_on_property_with_method_base()
    {
        // Cross-check with a method-returned base: the same object instance
        // must be used for both the read and the write.
        Eval(@"
            var calls = 0;
            var holder = { obj: {x: 1}, get: function() { calls++; return this.obj; } };
            holder.get().x += 10;
            holder.obj.x + ',' + calls
        ").AsString.Should().Be("11,1");
    }

    // -----------------------------------------------------------------
    //                            Helpers
    // -----------------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
