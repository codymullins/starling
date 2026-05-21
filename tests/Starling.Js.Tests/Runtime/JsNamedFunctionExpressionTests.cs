using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// §15.2.5 InstantiateOrdinaryFunctionExpression — a named function
/// <em>expression</em> binds its own name (as an immutable binding) in a
/// dedicated function environment so the body can refer to itself for recursion
/// / self-reference. Starling previously left the name unbound, so references
/// resolved to <c>undefined</c>/the global.
///
/// Reproduces the mcmaster.com / jQuery-core blocker:
/// <c>var b = function e(t,n){ return new e.fn.init(t,n); };</c> — <c>e</c> was
/// undefined inside the body, so <c>new e.fn.init(...)</c> tried to construct
/// <c>undefined</c> (<c>not a constructor: undefined (new hint: 'init')</c>).
///
/// Spec rules covered:
/// - Only NON-arrow function <em>expressions</em> with a name get this binding.
/// - The self-name is shadowed by a same-named param or body var/function
///   declaration (§10.2.11) — those win.
/// - A nested closure (incl. arrow) referencing the name captures it correctly.
/// - The binding refers to the function instance actually executing.
/// - Anonymous function expressions and function declarations are unaffected.
/// </summary>
[TestClass]
public class JsNamedFunctionExpressionTests
{
    // --- Core repro: the bug from the WP ----------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Named_function_expression_name_is_bound_inside_its_body()
        // The literal repro from the bug report: must be "function", not "undefined".
        => Eval("var f = function g(){ return typeof g; }; f();").AsString.Should().Be("function");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Named_function_expression_supports_recursion_via_own_name()
        // factorial: the body recurses through its own expression name `f`.
        => Eval("var fac = function f(n){ return n<=1 ? 1 : n*f(n-1); }; fac(5);")
            .AsNumber.Should().Be(120);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Named_function_expression_jquery_core_shape_constructs_via_own_name()
        // The jQuery-core shape: `J` (the expression name) must be usable inside
        // the body to reach `J.fn.init`. Prior to the fix this threw
        // `not a constructor: undefined (new hint: 'init')`.
        => Eval(@"
            var j = function J(s){ return new J.fn.init(s); };
            j.fn = j.prototype = { init: function(s){ this.s = s; } };
            j.fn.init.prototype = j.fn;
            j('hi').s;
        ").AsString.Should().Be("hi");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Named_function_expression_self_name_refers_to_the_executing_instance()
        // The bound name is the function value itself: `g === g.prototype.constructor`
        // and calling it through the name yields the same closure.
        => Eval("var f = function g(){ return g === f; }; f();").AsBool.Should().BeTrue();

    // --- Shadowing (§10.2.11): param / body var / inner function win ------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Parameter_named_like_the_function_shadows_the_self_name()
        // A param named `g` shadows the self-name: `typeof g` sees the (number) arg.
        => Eval("var f = function g(g){ return typeof g; }; f(42);").AsString.Should().Be("number");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Body_var_named_like_the_function_shadows_the_self_name()
        // A body `var g` shadows the self-name; once assigned, `g` is the string.
        => Eval("var f = function g(){ var g = 'shadow'; return g; }; f();")
            .AsString.Should().Be("shadow");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Inner_function_declaration_named_like_the_function_shadows_the_self_name()
        // A nested function declaration `g` shadows the self-name binding.
        => Eval("var f = function g(){ function g(){ return 7; } return g(); }; f();")
            .AsNumber.Should().Be(7);

    // --- Nested closure / arrow capture -----------------------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Nested_arrow_captures_the_self_name()
        // An inner arrow reads the self-name as an upvalue (Cell-backed).
        => Eval("var f = function g(){ var a = () => typeof g; return a(); }; f();")
            .AsString.Should().Be("function");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Nested_arrow_recursing_through_self_name_works()
        // The recursion runs through an inner arrow that closes over `f`.
        => Eval("var fac = function f(n){ var step = () => n<=1 ? 1 : n*f(n-1); return step(); }; fac(4);")
            .AsNumber.Should().Be(24);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Nested_ordinary_function_captures_the_self_name()
        // An inner ordinary function expression closes over the outer self-name.
        => Eval("var f = function g(){ var h = function(){ return typeof g; }; return h(); }; f();")
            .AsString.Should().Be("function");

    // --- No regression: anonymous expressions & declarations --------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Anonymous_function_expression_has_no_self_binding()
        // No name to bind; an unrelated free `g` still resolves to undefined.
        => Eval("var f = function(){ return typeof g; }; f();").AsString.Should().Be("undefined");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Self_name_is_not_visible_outside_the_function_expression()
        // §15.2.5: the name is scoped to the body only — invisible to the caller.
        => Eval("var f = function g(){ return 1; }; typeof g;").AsString.Should().Be("undefined");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Generator_function_expression_binds_its_own_name()
        // §15.2.5 applies to generator expressions too — `g` is bound in the body.
        => Eval("var f = function* g(){ yield typeof g; }; f().next().value;")
            .AsString.Should().Be("function");

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-runtime-semantics-instantiateordinaryfunctionexpression", "15.2.5")]
    [SpecFact]
    public void Arrow_function_has_no_own_name_binding()
        // Arrows never have an own name — a free `g` resolves to undefined.
        => Eval("var f = () => typeof g; f();").AsString.Should().Be("undefined");

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
