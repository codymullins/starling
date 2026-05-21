using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B1b-2c — Async/await tests. Async function invocation returns a
/// Promise immediately and runs the body on a worker thread; each await
/// re-suspends until the awaited promise settles.
/// </summary>
[TestClass]
public class JsAsyncAwaitTests
{
    [TestMethod]
    public void Async_function_returns_a_promise_resolving_to_body_value()
    {
        var (runtime, r) = Eval(@"
            async function f() { return 1 }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Await_resolves_to_promise_value()
    {
        var (runtime, _) = Eval(@"
            async function f() { return await Promise.resolve(42); }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Sequential_awaits_add_correctly()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                var a = await Promise.resolve(1);
                var b = await Promise.resolve(2);
                return a + b;
            }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Awaited_rejection_is_catchable()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                try { await Promise.reject('bad'); return 'unreached'; }
                catch (e) { return 'caught ' + e; }
            }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("caught bad");
    }

    [TestMethod]
    public void Async_function_that_throws_yields_rejected_promise()
    {
        var (runtime, _) = Eval(@"
            async function f() { throw 1 }
            f().catch(function(e) { globalThis.r = e });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Async_arrow_returns_promise()
    {
        var (runtime, _) = Eval(@"
            var f = async () => await Promise.resolve(5);
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Async_arrow_with_concise_body_returns_promise()
    {
        var (runtime, _) = Eval(@"
            var f = async x => x + 1;
            f(10).then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Async_generator_yields_promises()
    {
        // wp:M3-04g — .next() returns a Promise of {value, done}. (Full
        // yield/await interleaving + for-await is covered in
        // AsyncGeneratorTests.)
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; yield 2; }
            var it = g();
            it.next().then(function(r) { globalThis.r = r.value });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    private static (JsRuntime runtime, JsValue result) Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var rt = new JsRuntime();
        var r = new JsVm(rt).Run(chunk);
        return (rt, r);
    }
}
