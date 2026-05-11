using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// wp:M3-04c — snapshot-semantics closures. Inner functions can READ
/// enclosing-scope locals (snapshotted at MakeClosure time), but
/// mutation through an upvalue is deferred to wp:M3-04c2 with Cell-
/// based slots.
/// </summary>
public class JsClosureTests
{
    [Fact]
    public void MakeAdder_returns_a_function_that_remembers_n()
    {
        var r = Eval(@"
            function makeAdder(n) {
                return function(x) { return x + n; };
            }
            var add5 = makeAdder(5);
            add5(3);
        ");
        r.AsNumber.Should().Be(8);
    }

    [Fact]
    public void Closure_captures_param_at_definition_time()
    {
        // Each call to makeAdder produces an independent closure with
        // its own snapshot of n.
        var r = Eval(@"
            function makeAdder(n) {
                return function(x) { return x + n; };
            }
            var add5 = makeAdder(5);
            var add10 = makeAdder(10);
            add5(1) + add10(1);  // 6 + 11
        ");
        r.AsNumber.Should().Be(17);
    }

    [Fact]
    public void Closure_captures_local_var()
    {
        var r = Eval(@"
            function makeGreeter() {
                var greeting = 'hello, ';
                return function(name) { return greeting + name; };
            }
            makeGreeter()('world');
        ");
        r.AsString.Should().Be("hello, world");
    }

    [Fact]
    public void Function_expression_assigned_to_var_captures_outer_var()
    {
        // Function-expression form is what works in this slice; nested
        // FunctionDeclaration hoisting into the enclosing function's
        // local slots is queued for a follow-up (the current compiler
        // only hoists FunctionDeclarations at script top).
        var r = Eval(@"
            function outer() {
                var k = 7;
                var inner = function() { return k; };
                return inner();
            }
            outer();
        ");
        r.AsNumber.Should().Be(7);
    }

    [Fact]
    public void Sibling_closures_do_not_share_captured_state()
    {
        // Snapshot semantics: each call to make() captures a fresh
        // value of v at the moment MakeClosure runs.
        var r = Eval(@"
            function make(v) {
                return function() { return v; };
            }
            var a = make(1);
            var b = make(2);
            var c = make(3);
            a() * 100 + b() * 10 + c();
        ");
        r.AsNumber.Should().Be(123);
    }

    [Fact]
    public void Chained_capture_skipping_intermediate_function()
    {
        // The middle function doesn't reference n itself, but the
        // innermost one does. Lazy resolution should route the upvalue
        // through the intermediate via a chained (non-local) upvalue
        // reference.
        var r = Eval(@"
            function outer(n) {
                return function middle() {
                    return function inner() { return n; };
                };
            }
            outer(42)()();
        ");
        r.AsNumber.Should().Be(42);
    }

    [Fact]
    public void Curry_two_levels_of_capture()
    {
        var r = Eval(@"
            function curryAdd(a) {
                return function(b) {
                    return function(c) { return a + b + c; };
                };
            }
            curryAdd(1)(20)(300);
        ");
        r.AsNumber.Should().Be(321);
    }

    [Fact]
    public void Snapshot_does_not_observe_later_local_reassignment()
    {
        // After the closure is built, reassigning the captured local
        // in the parent must NOT change what the closure returns —
        // that's the defining property of snapshot semantics. (When
        // wp:M3-04c2 introduces Cell-based upvalues, this test will
        // change behavior; we'll update it then.)
        var r = Eval(@"
            function make() {
                var n = 10;
                var f = function() { return n; };
                n = 999;
                return f();
            }
            make();
        ");
        r.AsNumber.Should().Be(10);
    }

    [Fact]
    public void Closure_value_captured_per_call_not_shared_across_calls()
    {
        // Two activations of the same outer function must yield two
        // closures with independent captured snapshots.
        var r = Eval(@"
            function makeCounter(start) {
                return function() { return start; };
            }
            var c1 = makeCounter(100);
            var c2 = makeCounter(200);
            c1() + c2() + c1();  // 100 + 200 + 100
        ");
        r.AsNumber.Should().Be(400);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
