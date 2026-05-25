using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-71 — section 19.2.1.1 PerformEval direct path. A DIRECT eval (the
/// call's callee is the bare eval IdentifierReference resolving to the realm
/// %eval% intrinsic) inherits the caller's lexical context: strictness,
/// in-function-ness (new.target), in-method-ness (super.x via the caller's
/// [[HomeObject]]), this, and derived-constructor-ness. An INDIRECT eval
/// ((0,eval)(...), window.eval, a reassigned or a shadowed eval) is
/// sloppy-by-default and global-scoped — it inherits none of this. DEFERRED:
/// caller variable-scope access (EvalDeclarationInstantiation) — the evaluated
/// code cannot read the caller's let/const/var locals by name.
/// </summary>
[TestClass]
public class DirectEvalContextTests
{
    // ---- super.x via the caller's [[HomeObject]] ----

    [TestMethod]
    public void Direct_eval_of_super_property_in_object_method_resolves_via_home_object()
    {
        // A direct eval inside a concise method is "inside a method", so a
        // SuperProperty in the evaluated code parses and resolves against the
        // method's home object (the object's prototype).
        Run(@"
            var proto = { x: 'base-x' };
            var object = { read() { return eval('super.x'); } };
            Object.setPrototypeOf(object, proto);
            object.read();
        ").AsString.Should().Be("base-x");
    }

    [TestMethod]
    public void Direct_eval_of_super_property_in_class_field_initializer_executes()
    {
        // derived-cls-direct-eval-contains-superproperty-1.js — a direct eval in
        // a class field initializer is "inside a method", so super.x parses and
        // runs; the field initializer body executes (no SyntaxError).
        Run(@"
            var executed = false;
            var A = class {};
            var C = class extends A {
              x = eval('executed = true; super.x;');
            };
            new C();
            executed;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Indirect_eval_of_super_property_in_class_field_initializer_throws_syntax_error()
    {
        // derived-cls-indirect-eval-contains-superproperty-1.js — (0, eval)(...)
        // is an INDIRECT eval: it inherits NO method context, so super.x in its
        // code is an early SyntaxError (thrown when the field initializer runs).
        var act = () => Run(@"
            var A = class {};
            var C = class extends A {
              x = (0, eval)('super.x;');
            };
            new C();
        ");
        act.Should().Throw<JsThrow>();
    }

    // ---- strictness inheritance (direct only) ----

    [TestMethod]
    public void Direct_eval_inherits_caller_strict_mode_so_strict_only_early_error_throws()
    {
        // A with statement is a strict-mode-only early SyntaxError. A direct eval
        // inside strict caller code inherits the caller's strictness, so the with
        // in the evaluated source is a SyntaxError.
        var act = () => Run(@"
            'use strict';
            eval('with ({}) {}');
        ");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Indirect_eval_stays_sloppy_so_with_does_not_throw()
    {
        // (0, eval) is INDIRECT and sloppy-by-default, so with is legal — no
        // throw even though the surrounding code is strict.
        var act = () => Run(@"
            'use strict';
            (0, eval)('with ({}) {}');
        ");
        act.Should().NotThrow();
    }

    // ---- new.target availability by caller context ----

    [TestMethod]
    public void Direct_eval_of_new_target_in_function_is_allowed()
    {
        // new.target-fn.js — a direct eval inside a non-arrow function may
        // contain new.target; called plainly it is undefined.
        Run(@"
            var nt = 'sentinel';
            function f() { nt = eval('new.target'); }
            f();
            nt === undefined;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Direct_eval_of_new_target_in_global_code_throws_syntax_error()
    {
        // new.target.js — a direct eval in GLOBAL code may not contain
        // new.target (not in function code) — early SyntaxError.
        var act = () => Run(@"eval('new.target;');");
        act.Should().Throw<JsThrow>();
    }

    [TestMethod]
    public void Direct_eval_of_super_property_in_global_code_throws_syntax_error()
    {
        // super-prop.js — a direct eval in GLOBAL code may not contain a
        // SuperProperty (not inside a method) — early SyntaxError.
        var act = () => Run(@"eval('super.property;');");
        act.Should().Throw<JsThrow>();
    }

    // ---- direct/indirect split: shadowing & callee shape ----

    [TestMethod]
    public void Shadowed_local_eval_is_an_ordinary_call_not_direct_eval()
    {
        // A local binding named eval shadows the global intrinsic, so the call is
        // an ORDINARY call to that local — NOT a direct eval. Here the local
        // returns a marker, so super.x is never parsed/evaluated.
        Run(@"
            var proto = { x: 'base-x' };
            var object = {
              read() {
                var eval = function (s) { return 'shadowed:' + s; };
                return eval('super.x');
              }
            };
            Object.setPrototypeOf(object, proto);
            object.read();
        ").AsString.Should().Be("shadowed:super.x");
    }

    [TestMethod]
    public void Reassigned_global_eval_is_an_indirect_call()
    {
        // Reassigning the global eval means the DirectEval runtime check fails
        // (the callee is no longer the %eval% intrinsic), so the call dispatches
        // ordinarily to the replacement function.
        Run(@"
            eval = function (s) { return 'replaced:' + s; };
            eval('1 + 1');
        ").AsString.Should().Be("replaced:1 + 1");
    }

    [TestMethod]
    public void Indirect_eval_returns_completion_value_and_is_global_scoped()
    {
        // A genuine indirect eval still evaluates source and returns its
        // completion value (global scope), proving the indirect path is intact.
        Run(@"(0, eval)('40 + 2');").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Direct_eval_returns_completion_value()
    {
        // The common case: a plain global direct eval still returns the
        // completion value of the evaluated source.
        Run(@"eval('40 + 2');").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Non_string_argument_is_returned_unchanged_by_direct_eval()
    {
        // section 19.2.1 step 2 — a non-String argument is returned unchanged.
        Run(@"eval({a: 1}).a;").AsNumber.Should().Be(1);
    }

    private static JsValue Run(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
