using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// The <c>arguments</c> exotic object (§10.4.4 — Starling builds the unmapped
/// form, §10.4.4.6 CreateUnmappedArgumentsObject). Inside a non-arrow function
/// body, the identifier <c>arguments</c> resolves to an array-like object
/// carrying every received argument plus a <c>length</c>.
///
/// Reproduces the mcmaster.com / YUI 2.6 blocker: <c>YAHOO.namespace</c> reads
/// <c>arguments.length</c> in a loop to create <c>YAHOO.util</c>; before this
/// support, <c>arguments</c> resolved to the (undefined) global, the loop never
/// ran, and <c>YAHOO.util.CustomEvent</c> was never installed — surfacing later
/// as <c>not a constructor: undefined (new hint: 'CustomEvent')</c>.
/// </summary>
[TestClass]
public class JsArgumentsObjectTests
{
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_length_reflects_actual_argument_count()
        => Eval("function f(){ return arguments.length; } f(1,2,3)").AsNumber.Should().Be(3);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_indexed_access_returns_passed_values()
        => Eval("function f(){ return arguments[0]+':'+arguments[1]+':'+arguments[2]; } f('a','b','c')")
            .AsString.Should().Be("a:b:c");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_length_is_zero_when_no_args()
        => Eval("function f(){ return arguments.length; } f()").AsNumber.Should().Be(0);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_extra_args_beyond_named_params_are_visible()
        => Eval("function f(a){ return arguments.length + ',' + arguments[2]; } f(10,20,30)")
            .AsString.Should().Be("3,30");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_out_of_range_index_is_undefined()
        => Eval("function f(){ return typeof arguments[5]; } f(1)").AsString.Should().Be("undefined");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_is_iterable_via_spread()
        // @@iterator aliases Array.prototype.values, so spread collects the args.
        => Eval("function f(){ return [...arguments].join('-'); } f('x','y','z')")
            .AsString.Should().Be("x-y-z");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_works_with_Array_prototype_slice_call()
        => Eval(@"
            function f(){ return Array.prototype.slice.call(arguments).join(','); }
            f(1,2,3)
        ").AsString.Should().Be("1,2,3");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_loop_over_length_creates_namespace_chain()
    {
        // Direct reduction of YAHOO.namespace: iterate arguments.length and
        // build nested objects from each dotted name.
        var src = @"
            var Y = {};
            Y.ns = function(){
                var e,t,n,r=arguments,i=null;
                for(e=0;e<r.length;e+=1)
                    for(n=r[e].split('.'),i=Y,t='Y'==n[0]?1:0;t<n.length;t+=1)
                        i[n[t]]=i[n[t]]||{},i=i[n[t]];
                return i
            };
            Y.ns('util','widget','example');
            typeof Y.util + ',' + typeof Y.widget + ',' + typeof Y.example
        ";
        Eval(src).AsString.Should().Be("object,object,object");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arrow-function-definitions-runtime-semantics-evaluation", "15.3 Arrow Function Definitions")]
    [SpecFact]
    public void Arrow_inherits_enclosing_arguments_lexically()
        // The arrow has no own `arguments`; it reads the enclosing function's.
        => Eval(@"
            function f(){ var g = () => arguments[0] + arguments.length; return g(); }
            f(7, 8)
        ").AsNumber.Should().Be(9);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    [SpecFact]
    public void Explicit_arguments_parameter_shadows_implicit_object()
        // A parameter named `arguments` wins over the synthesized object.
        => Eval("function f(arguments){ return arguments; } f(42)").AsNumber.Should().Be(42);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    [SpecFact]
    public void Explicit_arguments_var_shadows_implicit_object()
        => Eval("function f(){ var arguments = 99; return arguments; } f(1,2,3)")
            .AsNumber.Should().Be(99);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Nested_function_has_its_own_arguments()
        => Eval(@"
            function outer(){
                function inner(){ return arguments.length; }
                return inner(1,2) + ',' + arguments.length;
            }
            outer('a','b','c')
        ").AsString.Should().Be("2,3");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_object_is_not_an_array()
        // Unmapped arguments inherits Object.prototype, not Array.prototype.
        => Eval("function f(){ return Array.isArray(arguments); } f(1,2)")
            .AsBool.Should().BeFalse();

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Arguments_in_class_method()
        => Eval(@"
            class C { m(){ return arguments.length + ':' + arguments[0]; } }
            new C().m('z', 'y')
        ").AsString.Should().Be("2:z");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-arguments-exotic-objects", "10.4.4 Arguments Exotic Objects")]
    [SpecFact]
    public void Function_expression_arguments()
        => Eval("var f = function(){ return arguments.length; }; f(1,2,3,4)")
            .AsNumber.Should().Be(4);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
