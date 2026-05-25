using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-75 — compound assignment (<c>+=</c>, <c>*=</c>, <c>%=</c>,
/// <c>&gt;&gt;&gt;=</c>, …) conformance, covering the three Test262 failure
/// clusters in <c>expressions/compound-assignment</c>:
/// <list type="number">
///   <item>§13.15.2 reference reuse — the LHS Reference base is resolved once
///         and reused for the write, so a self-deleting getter inside a
///         <c>with</c> still writes the SAME object (S11.13.2_A5.* series).</item>
///   <item>Object-operand coercion — object operands coerce through the same
///         ToPrimitive/ToNumeric path as the matching binary operator.</item>
///   <item>Invalid target — compound-assign to a <c>const</c> throws TypeError.</item>
/// </list>
/// </summary>
[TestClass]
public class CompoundAssignmentTests
{
    // ----- Sub-cause 1: §13.15.2 reference reuse through `with` -----

    [TestMethod]
    public void With_compound_uses_once_resolved_reference_self_deleting_getter()
        // S11.13.2_A5.1 shape: the getter deletes the binding mid-read; the
        // write must still land on `scope` (scope.x === 6), NOT the outer x.
        => Eval("""
            var x = 0;
            var scope = { get x() { delete this.x; return 2; } };
            with (scope) { x *= 3; }
            scope.x + ',' + x;
            """).AsString.Should().Be("6,0");

    [TestMethod]
    public void With_compound_nested_self_deleting_getter_targets_inner_scope()
        // S11.13.2_A5.1_T3 shape: nested `with`. The reference resolves to the
        // innermost scope holding the binding; the write returns there.
        => Eval("""
            var outerScope = { x: 0 };
            var innerScope = { get x() { delete this.x; return 2; } };
            with (outerScope) { with (innerScope) { x *= 3; } }
            innerScope.x + ',' + outerScope.x;
            """).AsString.Should().Be("6,0");

    [TestMethod]
    public void With_compound_addition_self_deleting_getter()
        => Eval("""
            var x = 100;
            var scope = { get x() { delete this.x; return 5; } };
            with (scope) { x += 10; }
            scope.x + ',' + x;
            """).AsString.Should().Be("15,100");

    [TestMethod]
    public void With_compound_plain_property_writes_through_with_object()
        // No getter trickery: an ordinary with-bound property compound-assigns
        // in place (and does NOT leak to the outer binding).
        => Eval("""
            var x = 1;
            var scope = { x: 10 };
            with (scope) { x *= 4; }
            scope.x + ',' + x;
            """).AsString.Should().Be("40,1");

    [TestMethod]
    public void With_compound_missing_binding_falls_back_to_outer()
        // The name is NOT on the with-object → the compound assignment targets
        // the outer binding (static fallback path).
        => Eval("""
            var x = 7;
            var scope = { y: 1 };
            with (scope) { x += 3; }
            x + ',' + ('x' in scope);
            """).AsString.Should().Be("10,false");

    // ----- Sub-cause 2: object-operand coercion via the binary-op seam -----

    [TestMethod]
    public void Compound_mod_coerces_String_object_operand()
        // `x %= new String("2")` must ToNumeric the wrapper → 2 (NOT NaN).
        => Eval("var x = 5; x %= new String('2'); x;").AsNumber.Should().Be(1);

    [TestMethod]
    public void Compound_mul_coerces_Number_object_operand()
        => Eval("var x = 6; x *= new Number(7); x;").AsNumber.Should().Be(42);

    [TestMethod]
    public void Compound_ushr_coerces_Boolean_object_operand()
        // `x >>>= new Boolean(true)` → ToNumeric(true) = 1 → 8 >>> 1 = 4.
        => Eval("var x = 8; x >>>= new Boolean(true); x;").AsNumber.Should().Be(4);

    [TestMethod]
    public void Compound_add_concatenates_String_object_operand()
        => Eval("var x = 'a'; x += new String('b'); x;").AsString.Should().Be("ab");

    [TestMethod]
    public void With_compound_coerces_object_operand_through_reference_reuse()
        // Both fixes together: object-operand coercion on the with-routed path.
        => Eval("""
            var x = 0;
            var scope = { get x() { delete this.x; return 3; } };
            with (scope) { x %= new Number(2); }
            scope.x + ',' + x;
            """).AsString.Should().Be("1,0");

    // ----- Sub-cause 3: invalid target → TypeError -----

    [TestMethod]
    public void Compound_assign_to_const_binding_throws_type_error()
    {
        // §13.15.2 — a compound assignment to a const binding is a runtime
        // TypeError. (Function-scoped: a block/function-level const is a real
        // lexical local; top-level/eval const still binds on the global object
        // and is a separate, deferred gap — see report.)
        var act = () => Eval("function f() { const c = 1; c += 2; return c; } f();");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Plain_assign_to_const_binding_throws_type_error()
        => ((Action)(() => Eval("function f() { const c = 1; c = 2; } f();")))
            .Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");

    [TestMethod]
    public void Block_scoped_const_compound_throws_type_error()
        => ((Action)(() => Eval("{ const c = 1; c *= 3; }")))
            .Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");

    [TestMethod]
    public void Const_read_after_init_still_works()
        // Regression: marking const must NOT break ordinary const reads/initializers.
        => Eval("function f() { const c = 5; return c + 1; } f();").AsNumber.Should().Be(6);

    [TestMethod]
    public void Compound_assign_to_non_writable_property_throws_in_strict()
    {
        var act = () => Eval("""
            'use strict';
            var o = {};
            Object.defineProperty(o, 'p', { value: 1, writable: false });
            o.p += 1;
            """);
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Compound_assign_to_accessor_without_setter_throws_in_strict()
    {
        // 11.13.2-34-s shape: getter-only accessor, strict mode → TypeError.
        var act = () => Eval("""
            'use strict';
            var o = {};
            Object.defineProperty(o, 'p', { get: function () { return 11; }, set: undefined });
            o.p *= 20;
            """);
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Compound_assign_to_unresolvable_reference_throws_reference_error()
    {
        // 11.13.2-2-s shape: the LHS read is GetValue, so an unresolvable free
        // identifier throws ReferenceError (in any mode).
        var act = () => Eval("notDeclaredAnywhere /= 1;");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("ReferenceError");
    }

    // ----- Regression: ordinary compound assignments still work -----

    [TestMethod]
    public void Plain_local_compound_still_works()
        => Eval("var x = 10; x -= 3; x *= 2; x;").AsNumber.Should().Be(14);

    [TestMethod]
    public void Plain_property_compound_still_works()
        => Eval("var o = { n: 4 }; o.n += 6; o.n;").AsNumber.Should().Be(10);

    [TestMethod]
    public void Plain_computed_compound_still_works()
        => Eval("var o = { n: 4 }; var k = 'n'; o[k] <<= 2; o.n;").AsNumber.Should().Be(16);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
