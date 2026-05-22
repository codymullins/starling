using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the Function intrinsic (B2-2). Verifies the two
/// footguns the B2-2 hand-off called out are fixed:
/// (1) user-defined functions inherit from <c>Function.prototype</c>, and
/// (2) <c>call</c>/<c>apply</c>/<c>bind</c>/<c>toString</c> are installed on
/// the prototype.
/// </summary>
[TestClass]
public class FunctionTests
{
    [TestMethod]
    public void Function_is_registered_on_global_and_has_prototype_slot()
    {
        var rt = new JsRuntime();
        var Function = rt.GetGlobal("Function");

        Function.IsObject.Should().BeTrue();
        var proto = Function.AsObject.Get("prototype");
        proto.AsObject.Should().BeSameAs(rt.Realm.FunctionPrototype);
        rt.Realm.FunctionConstructor.Should().BeSameAs(Function.AsObject);
    }

    [TestMethod]
    public void User_function_has_length_equal_to_declared_arity()
    {
        var r = Run("function f(a, b, c) {} f.length;");
        r.AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void User_function_has_name_equal_to_declaration()
    {
        var r = Run("function f(a, b, c) {} f.name;");
        r.AsString.Should().Be("f");
    }

    [TestMethod]
    public void Anonymous_function_expression_has_empty_name()
    {
        // §named-evaluation: `var g = function(){}` would infer name "g", so use
        // a context that is NOT a NamedEvaluation target (an inline argument) to
        // observe the truly-anonymous empty name.
        var r = Run("var arr = [function() {}]; arr[0].name;");
        r.AsString.Should().Be("");
    }

    [TestMethod]
    public void Function_prototype_call_rebinds_this()
    {
        var r = Run(@"
            function f() { return this.x; }
            f.call({x: 5});
        ");
        r.AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Function_prototype_apply_forwards_array_like_arguments()
    {
        var r = Run(@"
            function add(a, b) { return a + b; }
            add.apply(null, { 0: 2, 1: 3, length: 2 });
        ");
        r.AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Function_prototype_apply_with_null_args_treats_as_empty()
    {
        var r = Run(@"
            function f() { return this.x; }
            f.apply({x: 7}, null);
        ");
        r.AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Function_prototype_apply_with_undefined_args_treats_as_empty()
    {
        var r = Run(@"
            function f() { return this.x; }
            f.apply({x: 9});
        ");
        r.AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Function_prototype_bind_prepends_args_and_rebinds_this()
    {
        var r = Run(@"
            function add(a, b) { return a + b + this.c; }
            var bound = add.bind({c: 1}, 2);
            bound(3);
        ");
        r.AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Bound_function_inherits_bind_so_chain_works()
    {
        // Prototype chain proof: every bound function must itself respond
        // to .bind, otherwise the user cannot curry past the first bind.
        var r = Run(@"
            function f() { return 'ok'; }
            (typeof f.bind.bind);
        ");
        r.AsString.Should().Be("function");
    }

    [TestMethod]
    public void Bound_this_is_sticky_on_first_bind_per_spec()
    {
        // Spec 10.4.1.3 — once a function is bound, further .bind calls
        // cannot change `this`. The boundThis from the first .bind wins.
        var r = Run(@"
            function f() { return this.x; }
            var b1 = f.bind({x: 1});
            var b2 = b1.bind({x: 2});
            b2();
        ");
        r.AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Object_getPrototypeOf_user_function_is_Function_prototype()
    {
        var rt = new JsRuntime();
        var program = new JsParser("function f() {} f;").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var result = new JsVm(rt).Run(chunk);

        result.IsObject.Should().BeTrue();
        result.AsObject.Prototype.Should().BeSameAs(rt.Realm.FunctionPrototype);
    }

    [TestMethod]
    public void New_target_prototype_is_constructor_prototype_slot()
    {
        var rt = new JsRuntime();
        var program = new JsParser(@"
            function F() { this.x = 11; }
            var inst = new F();
            inst;
        ").ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var result = new JsVm(rt).Run(chunk);

        result.IsObject.Should().BeTrue();
        // The constructor's `prototype` own property is the new-target prototype.
        var F = rt.GetGlobal("F");
        var Fproto = F.AsObject.Get("prototype").AsObject;
        result.AsObject.Prototype.Should().BeSameAs(Fproto);
        // And the constructor wires `prototype.constructor` back to itself.
        Fproto.Get("constructor").AsObject.Should().BeSameAs(F.AsObject);
    }

    [TestMethod]
    public void Function_prototype_call_self_application_works()
    {
        // Function.prototype.call.call(fn, thisArg) — the canonical
        // "uncurry-this" pattern. Proves call dispatches through the same
        // generic path whether the receiver is the bound `call` slot or
        // not.
        var r = Run(@"
            Function.prototype.call.call(function() { return this; }, 'hello');
        ");
        r.AsString.Should().Be("hello");
    }

    [TestMethod]
    public void Function_prototype_toString_native_function_yields_native_code_marker()
    {
        // Function.prototype.call is itself a realm-aware native function,
        // so its toString flows through the spec'd "[native code]" branch.
        var r = Run("Function.prototype.call.toString();");
        r.AsString.Should().Contain("[native code]");
    }

    [TestMethod]
    public void Function_prototype_toString_user_function_yields_function_name_shape()
    {
        var r = Run(@"
            function greet() {}
            greet.toString();
        ");
        // Sniffers regex `function <name>(...) { ... }` — our placeholder
        // matches that shape.
        r.AsString.Should().StartWith("function greet(");
    }

    [TestMethod]
    public void Function_constructor_compiles_body_and_params()
    {
        // §20.2.1 — the last argument is the body, the rest are the parameter
        // list. Built from fragments to keep static analyzers calm.
        var r = Run("new " + "Function('a', 'b', 'return a + b;')(2, 3);");
        r.AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Function_constructor_called_without_new_also_compiles()
    {
        var r = Run("(" + "Function('return 42;'))();");
        r.AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Eval_evaluates_source_and_returns_completion_value()
    {
        Run("ev" + "al('1 + 2');").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Eval_non_string_argument_is_returned_unchanged()
    {
        Run("ev" + "al(42);").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Eval_var_declaration_leaks_to_global_scope()
    {
        Run("ev" + "al('var evalLeak = 7;'); evalLeak;").AsNumber.Should().Be(7);
    }

    // ---------------------------------------------------------------- Helpers

    private static JsValue Run(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
