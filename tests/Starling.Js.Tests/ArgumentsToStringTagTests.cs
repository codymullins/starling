using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// §20.1.3.6 step 5 — an <c>arguments</c> exotic object (it has a
/// <c>[[ParameterMap]]</c> slot) makes <c>Object.prototype.toString</c> report
/// <c>"[object Arguments]"</c>. Legacy feature-detection (underscore.js /
/// lodash <c>isArguments</c>) relies on this; without it the fallback path
/// pokes the poisoned strict-mode <c>callee</c> accessor and throws, which is
/// what broke mcmaster.com's combined bundle.
/// </summary>
[TestClass]
public class ArgumentsToStringTagTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void ToString_tags_sloppy_arguments_as_Arguments()
    {
        Eval(@"
            function f(){ return Object.prototype.toString.call(arguments); }
            f();
        ").AsString.Should().Be("[object Arguments]");
    }

    [TestMethod]
    public void ToString_tags_strict_arguments_as_Arguments()
    {
        Eval(@"
            function f(){ 'use strict'; return Object.prototype.toString.call(arguments); }
            f();
        ").AsString.Should().Be("[object Arguments]");
    }

    [TestMethod]
    public void HasOwn_callee_on_strict_arguments_does_not_invoke_poison_pill()
    {
        Eval(@"
            function f(){ 'use strict'; return Object.prototype.hasOwnProperty.call(arguments, 'callee'); }
            f();
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Underscore_isArguments_detection_short_circuits_without_touching_callee()
    {
        // The exact shape of underscore.js's isArguments install sequence: the
        // toString test must succeed so the callee-poking fallback is never used.
        Eval(@"
            var toString = Object.prototype.toString;
            var has = function(n, k){ return Object.prototype.hasOwnProperty.call(n, k); };
            var isArguments = function(r){ return toString.call(r) === '[object Arguments]'; };
            function probe(){
              isArguments(arguments) || (isArguments = function(n){ return has(n, 'callee'); });
              return isArguments(arguments);
            }
            probe();
        ").AsBool.Should().BeTrue();
    }
}
