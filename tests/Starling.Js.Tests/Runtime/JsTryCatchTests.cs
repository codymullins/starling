using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// gap:try-catch — end-to-end coverage for §14.15 try/catch/finally.
/// Drives the new EnterTry/LeaveTry/EndFinally opcodes through every
/// completion shape (normal, throw, return) so the VM's try-frame
/// stack stays honest on real bundles.
/// </summary>
/// <remarks>
/// Tests that need a function body to mutate outer state use an object
/// reference (gap:closure-write-back is still open at the time of writing,
/// so plain captured-var assignment from inside a nested function would
/// not write back through the upvalue snapshot).
/// </remarks>
public class JsTryCatchTests
{
    [Fact]
    public void Catch_user_throw_with_typeerror_object()
    {
        Eval(@"
            var name = '';
            try { throw new TypeError('x'); } catch (e) { name = e.name; }
            name;
        ").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Catch_primitive_throw_value_is_preserved()
    {
        Eval(@"
            var v;
            try { throw 'hello'; } catch (e) { v = e; }
            v;
        ").AsString.Should().Be("hello");
    }

    [Fact]
    public void Catch_caught_intrinsic_typeerror_explicit_throw()
    {
        // Direct test that an explicitly-thrown TypeError flows through
        // the catch with its prototype-derived `name` intact.
        Eval(@"
            var n;
            try { throw new TypeError('bad'); } catch (e) { n = e.name + '/' + e.message; }
            n;
        ").AsString.Should().Be("TypeError/bad");
    }

    [Fact]
    public void Rethrow_from_inner_catch_is_caught_by_outer_handler()
    {
        Eval(@"
            var got;
            try {
                try { throw 1; } catch (e) { throw e * 2; }
            } catch (e) { got = e; }
            got;
        ").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Finally_runs_on_normal_completion()
    {
        Eval(@"
            var ran = 0;
            try { } finally { ran = 1; }
            ran;
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Finally_runs_after_throw_plus_catch()
    {
        Eval(@"
            var ran = 0;
            try { throw 1; } catch (e) { } finally { ran = 1; }
            ran;
        ").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Finally_runs_when_no_handler_and_throw_propagates_to_outer_catch()
    {
        Eval(@"
            var ran = 0;
            var caught;
            try {
                try { throw 7; } finally { ran = 1; }
            } catch (e) { caught = e; }
            ran === 1 && caught === 7;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Optional_catch_binding_omits_parameter()
    {
        Eval(@"
            var ok = false;
            try { throw 1; } catch { ok = true; }
            ok;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Return_inside_try_with_finally_runs_finally_then_returns_value()
    {
        // Use an object side-effect channel to observe the finalizer
        // without depending on gap:closure-write-back.
        Eval(@"
            var bag = { ran: 0 };
            function f(b) { try { return 1; } finally { b.ran = 2; } }
            var rv = f(bag);
            rv === 1 && bag.ran === 2;
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Finally_return_overrides_try_return()
    {
        // §14.15.8 — abrupt completion in the finalizer replaces the
        // saved Return completion. Common spec gotcha.
        Eval(@"
            function f() { try { return 1; } finally { return 2; } }
            f();
        ").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Inner_catch_fires_for_inner_throw_not_outer()
    {
        Eval(@"
            var inner, outer;
            try {
                try { throw 'in'; } catch (e) { inner = e; }
            } catch (e) { outer = e; }
            inner === 'in' && typeof outer === 'undefined';
        ").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Outer_catch_fires_when_inner_block_has_no_handler()
    {
        Eval(@"
            var got;
            try {
                try { throw 'x'; } finally { }
            } catch (e) { got = e; }
            got;
        ").AsString.Should().Be("x");
    }

    [Fact]
    public void Catch_param_is_not_visible_after_block_exits()
    {
        Eval(@"
            try { throw 1; } catch (e) { }
            typeof e;
        ").AsString.Should().Be("undefined");
    }

    [Fact]
    public void Catch_runs_with_clean_eval_stack_after_partial_expression()
    {
        // Inner function throws partway through `1 + ...`; the partially-
        // evaluated `1` on the operand stack must be unwound before the
        // catch handler binds `e`.
        Eval(@"
            var got;
            try { var x = 1 + (function(){ throw 99; })(); } catch (e) { got = e; }
            got;
        ").AsNumber.Should().Be(99);
    }

    [Fact]
    public void Finally_runs_when_try_body_completes_normally_without_handler()
    {
        Eval(@"
            var t = 0, f = 0;
            try { t = 1; } finally { f = 1; }
            t + f;
        ").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Finally_throw_overrides_try_value()
    {
        Eval(@"
            var caught;
            try {
                try { throw 1; } finally { throw 2; }
            } catch (e) { caught = e; }
            caught;
        ").AsNumber.Should().Be(2);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
