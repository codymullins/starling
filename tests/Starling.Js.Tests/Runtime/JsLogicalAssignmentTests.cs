using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Tests for the logical assignment operators <c>&amp;&amp;=</c>, <c>||=</c>,
/// and <c>??=</c> (ES2021). These are NOT plain compound assignments: per
/// ECMAScript §13.15.2 they short-circuit, so the right-hand side is only
/// evaluated (and the assignment only performed) when the left operand fails
/// the short-circuit test. The target reference is evaluated exactly once.
/// </summary>
[TestClass]
public class JsLogicalAssignmentTests
{
    // -----------------------------------------------------------------
    //  ||=  (assign when LHS is falsy)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_identifier_falsy_assigns()
    {
        // 0 is falsy → assign 7
        Eval("var b=0; b ||= 7; b").AsNumber.Should().Be(7);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_identifier_truthy_keeps_value()
    {
        // 3 is truthy → keep 3, do not assign
        Eval("var b=3; b ||= 7; b").AsNumber.Should().Be(3);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_result_is_final_target_value()
    {
        // Result of the whole expression equals the final value of the target.
        Eval("var b=0; (b ||= 7)").AsNumber.Should().Be(7);
        Eval("var b=3; (b ||= 7)").AsNumber.Should().Be(3);
    }

    // -----------------------------------------------------------------
    //  &&=  (assign when LHS is truthy)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void AndAssign_identifier_truthy_assigns()
    {
        // 1 is truthy → assign 8
        Eval("var c=1; c &&= 8; c").AsNumber.Should().Be(8);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void AndAssign_identifier_falsy_keeps_value()
    {
        // 0 is falsy → keep 0, do not assign
        Eval("var c=0; c &&= 8; c").AsNumber.Should().Be(0);
    }

    // -----------------------------------------------------------------
    //  ??=  (assign when LHS is null/undefined)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_identifier_undefined_assigns()
    {
        // a is undefined → assign 5
        Eval("var a; a ??= 5; a").AsNumber.Should().Be(5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_identifier_null_assigns()
    {
        Eval("var a=null; a ??= 5; a").AsNumber.Should().Be(5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_identifier_zero_is_not_nullish_keeps_value()
    {
        // 0 is non-nullish → keep 0, do not assign (this is the key
        // difference from ||=).
        Eval("var a=0; a ??= 5; a").AsNumber.Should().Be(0);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_identifier_false_is_not_nullish_keeps_value()
    {
        Eval("var a=false; a ??= 5; a").AsBool.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    //  Short-circuit proof — RHS NOT evaluated when it short-circuits
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_does_not_evaluate_rhs_when_truthy()
    {
        // x is truthy → RHS (n++,9) must NOT run, so n stays 0 and x stays 1.
        Eval("var n=0; var x=1; x ||= (n++,9); n===0 && x===1").AsBool.Should().BeTrue();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void AndAssign_does_not_evaluate_rhs_when_falsy()
    {
        // x is falsy → RHS must NOT run, n stays 0, x stays 0.
        Eval("var n=0; var x=0; x &&= (n++,9); n===0 && x===0").AsBool.Should().BeTrue();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_does_not_evaluate_rhs_when_non_nullish()
    {
        // x is 0 (non-nullish) → RHS must NOT run.
        Eval("var n=0; var x=0; x ??= (n++,9); n===0 && x===0").AsBool.Should().BeTrue();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_evaluates_rhs_when_nullish()
    {
        // x is undefined → RHS runs once, n becomes 1, x becomes 9.
        Eval("var n=0; var x; x ??= (n++,9); n===1 && x===9").AsBool.Should().BeTrue();
    }

    // -----------------------------------------------------------------
    //  Member targets  (obj.x op= rhs)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_member_missing_property_assigns()
    {
        Eval("var o={}; o.x ??= 9; o.x").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_member_existing_property_keeps_value()
    {
        Eval("var o={x:2}; o.x ??= 9; o.x").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_member_falsy_property_assigns()
    {
        Eval("var o={x:0}; o.x ||= 9; o.x").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void AndAssign_member_truthy_property_assigns()
    {
        Eval("var o={x:1}; o.x &&= 9; o.x").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Member_assign_result_is_final_value()
    {
        Eval("var o={}; (o.x ??= 9)").AsNumber.Should().Be(9);
        Eval("var o={x:2}; (o.x ??= 9)").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Member_base_evaluated_once_on_assign()
    {
        // The base expression `g()` increments i; it must run exactly once
        // even when the assignment happens (read + write share one base).
        Eval(@"
            var i=0;
            var box={o:{}};
            var g=function(){ i++; return box.o; };
            g().x ??= 9;
            i + ',' + box.o.x
        ").AsString.Should().Be("1,9");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Member_base_evaluated_once_on_short_circuit()
    {
        // Even when short-circuiting (property already present), the base must
        // be evaluated exactly once.
        Eval(@"
            var i=0;
            var box={o:{x:2}};
            var g=function(){ i++; return box.o; };
            g().x ??= 9;
            i + ',' + box.o.x
        ").AsString.Should().Be("1,2");
    }

    // -----------------------------------------------------------------
    //  Computed targets  (obj[key] op= rhs)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_computed_missing_assigns()
    {
        Eval("var o={}; o['x'] ??= 9; o.x").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_computed_falsy_assigns()
    {
        Eval("var a=[0]; a[0] ||= 9; a[0]").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void AndAssign_computed_truthy_assigns()
    {
        Eval("var a=[5]; a[0] &&= 9; a[0]").AsNumber.Should().Be(9);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Computed_key_evaluated_once_on_assign()
    {
        // Both the base and the key expression must be evaluated exactly once
        // even though the read and the write both need them.
        Eval(@"
            var i=0;
            var k=function(){ i++; return 0; };
            var a=[];
            a[k()] ??= 9;
            i + ',' + a[0]
        ").AsString.Should().Be("1,9");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Computed_key_evaluated_once_on_short_circuit()
    {
        Eval(@"
            var i=0;
            var k=function(){ i++; return 0; };
            var a=[5];
            a[k()] ??= 9;
            i + ',' + a[0]
        ").AsString.Should().Be("1,5");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void Computed_assign_does_not_evaluate_rhs_when_short_circuit()
    {
        // RHS side effect must not run when the computed target short-circuits.
        Eval(@"
            var n=0;
            var a=[5];
            a[0] ??= (n++,9);
            n + ',' + a[0]
        ").AsString.Should().Be("0,5");
    }

    // -----------------------------------------------------------------
    //  Global / script-top targets
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void OrAssign_global_target()
    {
        Eval("g = 0; g ||= 11; g").AsNumber.Should().Be(11);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_chained()
    {
        // Right-associative chaining: a is undefined, b is undefined.
        // a ??= b ??= 5  →  b becomes 5, a becomes 5.
        Eval("var a; var b; a ??= b ??= 5; a + ',' + b").AsString.Should().Be("5,5");
    }

    // -----------------------------------------------------------------
    //  Closure / upvalue target write-back
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-assignment-operators", "13.15.2")]
    [SpecFact]
    public void NullishAssign_upvalue_target_writes_through_cell()
    {
        // The target is a captured variable; the logical assignment must
        // write back through the shared cell so the outer read sees it.
        Eval(@"
            var v;
            var set = function(){ v ??= 42; };
            set();
            v
        ").AsNumber.Should().Be(42);
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
