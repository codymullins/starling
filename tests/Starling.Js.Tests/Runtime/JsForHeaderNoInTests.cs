using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// The `for` header's [NoIn] grammar restriction (which disambiguates
/// `for (x in y)` from `for (expr;;)`) must NOT propagate into nested function /
/// arrow bodies inside a for-initializer expression — those are [+In]. Minified
/// bundles (mcmaster.com / jQuery) emit `for(!function(){ … "k" in o … }(); …)`.
/// </summary>
[TestClass]
public class JsForHeaderNoInTests
{
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-for-statement", "14.7.4 The for Statement")]
    public void In_operator_is_allowed_inside_a_function_body_in_a_for_initializer()
        => Eval("var r=''; for(!function(){ var a={x:1}; if('x' in a) r='Y'; }(); false; ){} r")
            .AsString.Should().Be("Y");

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-for-statement", "14.7.4 The for Statement")]
    public void In_operator_allowed_inside_arrow_concise_body_in_for_initializer()
        => Eval("var r=false; for(var f=()=>'x' in {x:1}; !r; r=true){ r = f(); } r")
            .AsBool.Should().BeTrue();

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-for-statement", "14.7.4 The for Statement")]
    public void For_in_loop_still_parses_after_the_fix()
        => Eval("var s=''; for(var k in {a:1,b:2}) s+=k; s").AsString.Should().Be("ab");

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-for-statement", "14.7.4 The for Statement")]
    public void Plain_for_with_no_in_in_init_still_parses()
        => Eval("var n=0; for(var i=0; i<3; i++) n++; n").AsNumber.Should().Be(3);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
