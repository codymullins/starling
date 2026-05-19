using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// gap:closure-write-back / wp:M3-04c2 — closures must observe writes that
/// nested functions make to captured outer locals. Before this fix,
/// closures snapshotted upvalues read-only, so any mutation in the inner
/// function vanished. These tests pin the live-binding semantics required
/// by real-world JS (counters, event handlers, module patterns).
/// </summary>
[TestClass]
public class JsClosureWriteBackTests
{
    [TestMethod]
    public void Inner_assignment_writes_back_to_outer_var()
    {
        Eval(@"
            function f() { var x = 0; function inner() { x = 5; } inner(); return x; }
            f();
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Counter_pattern_returns_increasing_values()
    {
        Eval(@"
            function makeCounter() { var n = 0; return function() { return ++n; }; }
            var c = makeCounter();
            c(); c(); c();
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Multiple_writes_through_closure_accumulate()
    {
        Eval(@"
            function f() {
                var x = 1;
                function inc() { x++; }
                inc(); inc(); inc();
                return x;
            }
            f();
        ").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Outer_observes_write_after_inner_call()
    {
        Eval(@"
            function f() { var x = 0; function g() { x = 1; } g(); return x; }
            f();
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Reader_and_writer_closures_share_binding()
    {
        Eval(@"
            function f() {
                var x = 10;
                return [function() { return x; }, function() { x = 99; }];
            }
            var pair = f();
            var r = pair[0];
            var w = pair[1];
            var before = r();
            w();
            var after = r();
            before * 1000 + after;
        ").AsNumber.Should().Be(10099);
    }

    [TestMethod]
    public void Mutual_capture_reads_and_writes_same_cell()
    {
        Eval(@"
            function f() {
                var x = 1;
                function a() { return x + 1; }
                function b() { x = a(); }
                b();
                return x;
            }
            f();
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Inner_declaration_shadows_and_does_not_write_back()
    {
        Eval(@"
            function f() {
                var x = 1;
                function g() { var x = 99; return x; }
                g();
                return x;
            }
            f();
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Inner_shadow_value_returned_is_inner_value()
    {
        Eval(@"
            function f() {
                var x = 1;
                function g() { var x = 99; return x; }
                return g();
            }
            f();
        ").AsNumber.Should().Be(99);
    }

    [TestMethod]
    public void Nested_nested_closure_writes_through_to_outermost()
    {
        Eval(@"
            function f() {
                var x = 0;
                function g() {
                    function h() { x = 5; }
                    h();
                }
                g();
                return x;
            }
            f();
        ").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Let_binding_inside_function_writes_back_through_arrow()
    {
        Eval(@"
            function f() {
                let x = 0;
                var bump = () => { x = x + 7; };
                bump(); bump();
                return x;
            }
            f();
        ").AsNumber.Should().Be(14);
    }

    [TestMethod]
    public void Captured_parameter_is_writable_through_closure()
    {
        Eval(@"
            function makeAdder(x) {
                return function(y) { x = x + y; return x; };
            }
            var add = makeAdder(10);
            add(1); add(2); add(3);
        ").AsNumber.Should().Be(16);
    }

    [TestMethod]
    public void Two_counters_have_independent_state()
    {
        Eval(@"
            function makeCounter() { var n = 0; return function() { return ++n; }; }
            var a = makeCounter();
            var b = makeCounter();
            a(); a(); a();
            b();
            a() * 10 + b();
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Non_captured_local_still_works()
    {
        // Sanity: a function that does NOT capture anything should still
        // get the fast path (plain LoadLocal / StoreLocal). The visible
        // semantics don't change.
        Eval(@"
            function f(a, b) { var c = a + b; return c * 2; }
            f(3, 4);
        ").AsNumber.Should().Be(14);
    }

    [TestMethod]
    public void Closure_over_loop_iteration_var_sees_live_value()
    {
        // The closure captures `total`, an outer var that gets mutated by
        // both the loop body and a callback. With live-binding semantics,
        // the callback sees the running total.
        Eval(@"
            function f() {
                var total = 0;
                function add(n) { total = total + n; }
                add(1); add(2); add(3); add(4);
                return total;
            }
            f();
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Compound_assignment_to_upvalue_propagates()
    {
        Eval(@"
            function f() {
                var x = 5;
                function bump() { x += 3; }
                bump(); bump();
                return x;
            }
            f();
        ").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Prefix_decrement_on_upvalue_propagates()
    {
        Eval(@"
            function f() {
                var x = 10;
                function dec() { return --x; }
                var a = dec();
                var b = dec();
                return a * 100 + b * 10 + x;
            }
            f();
        ").AsNumber.Should().Be(988);
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
