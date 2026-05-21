using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Tests for update expressions (++ / --) applied to member-expression
/// targets: <c>obj.x++</c>, <c>++obj.x</c>, <c>obj[k]++</c>, etc.
/// ECMAScript §13.4 Update Expressions.
/// </summary>
[TestClass]
public class JsMemberUpdateTests
{
    // -----------------------------------------------------------------
    //  Non-computed (obj.name++)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Postfix_increment_named_returns_old_value()
    {
        // r = old value (5), p.n becomes 6
        Eval("var p={n:5}; var r=p.n++; r").AsNumber.Should().Be(5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Postfix_increment_named_mutates_property()
    {
        Eval("var o={x:1}; o.x++; o.x").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-increment-operator", "13.4.2 Prefix Increment Operator")]
    [SpecFact]
    public void Prefix_increment_named_returns_new_value()
    {
        // s = new value (6), q.n becomes 6
        Eval("var q={n:5}; var s=++q.n; s").AsNumber.Should().Be(6);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-increment-operator", "13.4.2 Prefix Increment Operator")]
    [SpecFact]
    public void Prefix_increment_named_mutates_property()
    {
        Eval("var q={n:5}; ++q.n; q.n").AsNumber.Should().Be(6);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-decrement-operator", "13.4.5 Postfix Decrement Operator")]
    [SpecFact]
    public void Postfix_decrement_named_returns_old_value()
    {
        Eval("var o={x:3}; var r=o.x--; r").AsNumber.Should().Be(3);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-decrement-operator", "13.4.5 Postfix Decrement Operator")]
    [SpecFact]
    public void Postfix_decrement_named_mutates_property()
    {
        Eval("var o={x:3}; o.x--; o.x").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-decrement-operator", "13.4.3 Prefix Decrement Operator")]
    [SpecFact]
    public void Prefix_decrement_named_returns_new_value()
    {
        Eval("var o={x:3}; var r=--o.x; r").AsNumber.Should().Be(2);
    }

    // -----------------------------------------------------------------
    //  Computed (obj[k]++)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Postfix_increment_computed_returns_old_value()
    {
        Eval("var a=[5]; var r=a[0]++; r").AsNumber.Should().Be(5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Postfix_increment_computed_mutates_element()
    {
        Eval("var a=[5]; a[0]++; a[0]").AsNumber.Should().Be(6);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-increment-operator", "13.4.2 Prefix Increment Operator")]
    [SpecFact]
    public void Prefix_increment_computed_returns_new_value()
    {
        Eval("var a=[5]; var r=++a[0]; r").AsNumber.Should().Be(6);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-decrement-operator", "13.4.3 Prefix Decrement Operator")]
    [SpecFact]
    public void Prefix_decrement_computed_returns_new_value()
    {
        Eval("var a=[5]; var r=--a[0]; r").AsNumber.Should().Be(4);
    }

    // -----------------------------------------------------------------
    //  Key expression evaluated exactly once (§13.4, ref evaluation)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Computed_key_side_effect_executed_once()
    {
        // The key expression (a function call that increments `i`) must be
        // evaluated exactly once even though we need it for both the read
        // and the write.
        Eval(@"
            var i = 0;
            var f = function() { i++; return 0; };
            var a = [10];
            a[f()]++;
            i + ',' + a[0]
        ").AsString.Should().Be("1,11");
    }

    // -----------------------------------------------------------------
    //  Combined repro: o.x===2, a[0]===6, r===5 (postfix), p.n===6
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Combined_member_update_repro()
    {
        Eval(@"
            var o={x:1}; o.x++;
            var a=[5]; a[0]++;
            var p={n:5}; var r=p.n++;
            var q={n:5}; var s=++q.n;
            o.x + ',' + a[0] + ',' + r + ',' + p.n + ',' + s + ',' + q.n
        ").AsString.Should().Be("2,6,5,6,6,6");
    }

    // -----------------------------------------------------------------
    //  String-to-number coercion (ToNumber on the old value, §7.1.4)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Named_update_coerces_string_value_to_number()
    {
        // "5" (string) + 1 → 6 (number), not "51" (concatenation)
        Eval("var o={x:'5'}; o.x++; o.x").AsNumber.Should().Be(6);
    }

    // -----------------------------------------------------------------
    //  Statement-form stack is balanced (no extra value left)
    // -----------------------------------------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Named_update_as_statement_leaves_correct_final_value()
    {
        // Confirm the script ends cleanly and the final value read is right.
        Eval("var o={x:1}; o.x++; o.x++; o.x").AsNumber.Should().Be(3);
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
