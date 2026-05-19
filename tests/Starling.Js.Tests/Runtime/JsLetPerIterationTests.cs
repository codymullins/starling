using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Per-iteration <c>let</c> / <c>const</c> binding in <c>for</c> /
/// <c>for…in</c> / <c>for…of</c> loops, per §14.7.4.4
/// CreatePerIterationEnvironment. Closures formed inside the iteration body
/// capture the iteration's own binding, not a single slot shared across all
/// iterations.
/// </summary>
[TestClass]
public class JsLetPerIterationTests
{
    // -----------------------------------------------------------------------
    // Classic closure-in-loop bug fixed by per-iteration let.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void For_let_captures_per_iteration_binding()
    {
        Eval(@"
            var arr = [];
            for (let i = 0; i < 3; i++) arr.push(() => i);
            arr[0]() + ',' + arr[1]() + ',' + arr[2]()
        ").AsString.Should().Be("0,1,2");
    }

    [TestMethod]
    public void For_var_retains_shared_binding()
    {
        // `var` is function-scoped (here, script-scoped → global); closures
        // share one binding, so all three see the final value.
        Eval(@"
            var arr = [];
            for (var i = 0; i < 3; i++) arr.push(function () { return i });
            arr[0]() + ',' + arr[1]() + ',' + arr[2]()
        ").AsString.Should().Be("3,3,3");
    }

    [TestMethod]
    public void ForOf_const_captures_per_iteration_binding()
    {
        Eval(@"
            var arr = [];
            for (const x of [1, 2, 3]) arr.push(() => x);
            arr[0]() + ',' + arr[1]() + ',' + arr[2]()
        ").AsString.Should().Be("1,2,3");
    }

    [TestMethod]
    public void ForIn_let_captures_per_iteration_binding()
    {
        Eval(@"
            var keys = [];
            for (let k in {a: 1, b: 2, c: 3}) keys.push(() => k);
            keys[0]() + ',' + keys[1]() + ',' + keys[2]()
        ").AsString.Should().Be("a,b,c");
    }

    // -----------------------------------------------------------------------
    // Body mutation lands in the iteration's own cell.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void For_let_body_mutation_visible_to_closure()
    {
        // i=0 → body: i=0*10=0, capture(0), update i_iter1++=1.
        // i_iter2 = 1 → body: i=1*10=10, capture(10), update i_iter2++=11.
        // i_iter3 = 11 → test 11<3 false. Only two closures pushed.
        Eval(@"
            var arr = [];
            for (let i = 0; i < 3; i++) { i *= 10; arr.push(() => i) }
            arr.length + ':' + arr[0]() + ',' + arr[1]()
        ").AsString.Should().Be("2:0,10");
    }

    // -----------------------------------------------------------------------
    // Inner loop's let shadowing doesn't pollute the outer.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Inner_for_let_does_not_pollute_outer()
    {
        Eval(@"
            var outer = [];
            for (let i = 0; i < 3; i++) {
              for (let i = 0; i < 2; i++) {}
              outer.push(i);
            }
            outer.join(',')
        ").AsString.Should().Be("0,1,2");
    }

    // -----------------------------------------------------------------------
    // Update sees the fresh binding (body's i++ is visible to update step).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void For_let_update_sees_body_mutation()
    {
        // iter1: i=0 → body: ran.push(0); i_iter1++=1. update: i_iter1++=2.
        // iter2: test 2<2 false → exit. ran === [0].
        Eval(@"
            var ran = [];
            for (let i = 0; i < 2; i++) { ran.push(i); i++ }
            ran.length + ':' + ran.join(',')
        ").AsString.Should().Be("1:0");
    }

    // -----------------------------------------------------------------------
    // Non-loop let still gets closure-write-back semantics from
    // gap:closure-write-back (sanity check that this change didn't regress it).
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Non_loop_let_write_back_unaffected()
    {
        // Inside a function body so `let x` is a real lexical binding (not a
        // script-top global) and capture analysis can see it. Pin from
        // gap:closure-write-back — closures see the writer's latest value.
        Eval(@"
            function run() {
              let x = 1;
              function f() { return x }
              x = 2;
              return f();
            }
            run()
        ").AsNumber.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Inside a function: per-iteration semantics work for function-local let.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Function_local_for_let_captures_per_iteration()
    {
        Eval(@"
            function make() {
              var fns = [];
              for (let i = 0; i < 3; i++) fns.push(() => i * 10);
              return fns;
            }
            var fns = make();
            fns[0]() + ',' + fns[1]() + ',' + fns[2]()
        ").AsString.Should().Be("0,10,20");
    }

    // -----------------------------------------------------------------------
    // for…of inside a function with closure capturing per-iteration binding.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Function_local_for_of_let_captures_per_iteration()
    {
        Eval(@"
            function make() {
              var fns = [];
              for (let v of [10, 20, 30]) fns.push(() => v);
              return fns;
            }
            var fns = make();
            fns[0]() + ',' + fns[1]() + ',' + fns[2]()
        ").AsString.Should().Be("10,20,30");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
