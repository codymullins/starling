using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Closure capture tests. Inner functions read and write enclosing-scope
/// locals via Cell-based upvalues (live binding semantics).
/// </summary>
[TestClass]
public class JsClosureTests
{
    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
    public void Closure_observes_later_local_reassignment()
    {
        // Closures use Cell-based upvalues (live binding semantics); a
        // reassignment of the captured local in the parent IS observed
        // by the closure on the next call.
        var r = Eval(@"
            function make() {
                var n = 10;
                var f = function() { return n; };
                n = 999;
                return f();
            }
            make();
        ");
        r.AsNumber.Should().Be(999);
    }

    [TestMethod]
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
