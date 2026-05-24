using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-60 — a thrown error must keep its exact identity (prototype, <c>name</c>,
/// custom <c>toString</c>) as it flows through async error propagation: an
/// async-function throw, an <c>await</c> of a rejected promise, an async
/// generator throw, and a <c>for await</c> rejection. The matching regression
/// was that <c>String(error)</c> / object→string coercion returned the flat
/// <c>"[object Object]"</c> instead of invoking <c>Error.prototype.toString</c>
/// (so test262's async <c>doneprintHandle</c> printed <c>[object Object]</c>).
/// The error object itself was never re-wrapped — identity (<c>instanceof</c>)
/// already held; only the stringification was wrong.
/// </summary>
[TestClass]
public class AsyncErrorIdentityTests
{
    [TestMethod]
    public void Sync_error_stringifies_via_prototype_toString()
    {
        // Baseline: String(new TypeError('boom')) must use Error.prototype.toString.
        var (runtime, _) = Eval(@"
            var e = new TypeError('boom');
            globalThis.str = String(e);
            globalThis.concat = '' + e;
            globalThis.isTE = (e instanceof TypeError);
            globalThis.nm = e.name;
            globalThis.hasName = ('name' in e);
        ");
        runtime.GetGlobal("str").AsString.Should().Be("TypeError: boom");
        runtime.GetGlobal("concat").AsString.Should().Be("TypeError: boom");
        runtime.GetGlobal("isTE").AsBool.Should().BeTrue();
        runtime.GetGlobal("nm").AsString.Should().Be("TypeError");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Async_function_throw_preserves_error_identity()
    {
        var (runtime, _) = Eval(@"
            async function f(){ throw new TypeError('boom'); }
            f().catch(function(e){
                globalThis.isTE = (e instanceof TypeError);
                globalThis.nm = e.name;
                globalThis.str = String(e);
                globalThis.hasName = ('name' in e);
            });
        ");
        runtime.GetGlobal("isTE").AsBool.Should().BeTrue();
        runtime.GetGlobal("nm").AsString.Should().Be("TypeError");
        runtime.GetGlobal("str").AsString.Should().Be("TypeError: boom");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Await_of_rejected_promise_preserves_error_identity()
    {
        var (runtime, _) = Eval(@"
            async function f(){
                try { await Promise.reject(new RangeError('oops')); }
                catch (e) {
                    globalThis.isRE = (e instanceof RangeError);
                    globalThis.nm = e.name;
                    globalThis.str = String(e);
                    globalThis.hasName = ('name' in e);
                }
            }
            f();
        ");
        runtime.GetGlobal("isRE").AsBool.Should().BeTrue();
        runtime.GetGlobal("nm").AsString.Should().Be("RangeError");
        runtime.GetGlobal("str").AsString.Should().Be("RangeError: oops");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Async_generator_throw_preserves_error_identity()
    {
        var (runtime, _) = Eval(@"
            async function* g(){ throw new TypeError('gen'); }
            var it = g();
            it.next().then(
                function(){ globalThis.r = 'resolved'; },
                function(e){
                    globalThis.isTE = (e instanceof TypeError);
                    globalThis.nm = e.name;
                    globalThis.str = String(e);
                    globalThis.hasName = ('name' in e);
                });
        ");
        runtime.GetGlobal("isTE").AsBool.Should().BeTrue();
        runtime.GetGlobal("nm").AsString.Should().Be("TypeError");
        runtime.GetGlobal("str").AsString.Should().Be("TypeError: gen");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void For_await_rejection_preserves_error_identity()
    {
        var (runtime, _) = Eval(@"
            async function* g(){ yield 1; throw new TypeError('fa'); }
            async function run(){
                try { for await (var x of g()) {} }
                catch (e) {
                    globalThis.isTE = (e instanceof TypeError);
                    globalThis.nm = e.name;
                    globalThis.str = String(e);
                    globalThis.hasName = ('name' in e);
                }
            }
            run();
        ");
        runtime.GetGlobal("isTE").AsBool.Should().BeTrue();
        runtime.GetGlobal("nm").AsString.Should().Be("TypeError");
        runtime.GetGlobal("str").AsString.Should().Be("TypeError: fa");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Custom_toString_object_preserved_through_async_rejection()
    {
        // A plain thrown object with its own toString must keep identity AND
        // have its custom toString invoked by String() after async propagation.
        var (runtime, _) = Eval(@"
            var thrown = { name: 'Boom', toString: function(){ return 'Boom: detail'; } };
            async function f(){ throw thrown; }
            f().catch(function(e){
                globalThis.same = (e === thrown);
                globalThis.str = String(e);
                globalThis.hasName = ('name' in e);
            });
        ");
        runtime.GetGlobal("same").AsBool.Should().BeTrue();
        runtime.GetGlobal("str").AsString.Should().Be("Boom: detail");
        runtime.GetGlobal("hasName").AsBool.Should().BeTrue();
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
