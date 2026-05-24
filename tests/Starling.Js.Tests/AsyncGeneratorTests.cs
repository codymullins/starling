using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-04g — async generators (<c>async function*</c>). The body interleaves
/// <c>yield</c> and <c>await</c>; <c>next()/return()/throw()</c> each return a
/// Promise of <c>{value, done}</c>; <c>for await (… of …)</c> drives the async
/// iterator protocol. Tests are deterministic: the eval helper runs a top-level
/// chunk, which drains the realm's microtask queue before returning (the same
/// way the existing Promise/async tests force progress) — no wall-clock waits.
/// </summary>
[TestClass]
public class AsyncGeneratorTests
{
    [TestMethod]
    public void Next_returns_promise_of_value_done()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; yield 2; }
            var it = g();
            it.next().then(function(r) { globalThis.r1 = r.value + ':' + r.done });
            it.next().then(function(r) { globalThis.r2 = r.value + ':' + r.done });
            it.next().then(function(r) { globalThis.r3 = r.value + ':' + r.done });
        ");
        runtime.GetGlobal("r1").AsString.Should().Be("1:false");
        runtime.GetGlobal("r2").AsString.Should().Be("2:false");
        runtime.GetGlobal("r3").AsString.Should().Be("undefined:true");
    }

    [TestMethod]
    public void Await_inside_async_generator_actually_suspends()
    {
        // This is the key behavior the old stub could NOT do: await a promise
        // mid-body, then yield the resolved value. If await did not suspend,
        // the Suspend(kind=1) opcode would have thrown a SyntaxError and the
        // promise chain would never resolve.
        var (runtime, _) = Eval(@"
            async function* g() {
                var a = await Promise.resolve(10);
                yield a + 1;
                var b = await Promise.resolve(20);
                yield a + b;
            }
            var it = g();
            it.next().then(function(r) { globalThis.r1 = r.value });
            it.next().then(function(r) { globalThis.r2 = r.value });
        ");
        runtime.GetGlobal("r1").AsNumber.Should().Be(11);
        runtime.GetGlobal("r2").AsNumber.Should().Be(30);
    }

    [TestMethod]
    public void Yield_receives_value_sent_to_next()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                var x = yield 1;
                yield x + 100;
            }
            var it = g();
            it.next().then(function(r) { globalThis.r1 = r.value });
            it.next(5).then(function(r) { globalThis.r2 = r.value });
        ");
        runtime.GetGlobal("r1").AsNumber.Should().Be(1);
        runtime.GetGlobal("r2").AsNumber.Should().Be(105);
    }

    [TestMethod]
    public void Awaited_rejection_is_catchable_inside_body()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                try { await Promise.reject('boom'); yield 'unreached'; }
                catch (e) { yield 'caught ' + e; }
            }
            var it = g();
            it.next().then(function(r) { globalThis.r = r.value });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("caught boom");
    }

    [TestMethod]
    public void Body_return_value_completes_with_done_true()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; return 99; }
            var it = g();
            it.next().then(function(r) { globalThis.r1 = r.value + ':' + r.done });
            it.next().then(function(r) { globalThis.r2 = r.value + ':' + r.done });
            it.next().then(function(r) { globalThis.r3 = r.value + ':' + r.done });
        ");
        runtime.GetGlobal("r1").AsString.Should().Be("1:false");
        // The return value is delivered as {value:99, done:true}.
        runtime.GetGlobal("r2").AsString.Should().Be("99:true");
        // Subsequent next() → {value:undefined, done:true}.
        runtime.GetGlobal("r3").AsString.Should().Be("undefined:true");
    }

    [TestMethod]
    public void Return_method_finishes_the_generator()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; yield 2; yield 3; }
            var it = g();
            it.next().then(function(r) { globalThis.r1 = r.value + ':' + r.done });
            it.return(42).then(function(r) { globalThis.r2 = r.value + ':' + r.done });
            it.next().then(function(r) { globalThis.r3 = r.value + ':' + r.done });
        ");
        runtime.GetGlobal("r1").AsString.Should().Be("1:false");
        runtime.GetGlobal("r2").AsString.Should().Be("42:true");
        runtime.GetGlobal("r3").AsString.Should().Be("undefined:true");
    }

    [TestMethod]
    public void Return_runs_enclosing_finally()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                try { yield 1; yield 2; }
                finally { globalThis.cleanup = 'ran'; }
            }
            var it = g();
            it.next().then(function() {});
            it.return(7).then(function(r) { globalThis.r = r.value + ':' + r.done });
        ");
        runtime.GetGlobal("cleanup").AsString.Should().Be("ran");
        runtime.GetGlobal("r").AsString.Should().Be("7:true");
    }

    [TestMethod]
    public void Throw_method_injects_error_at_suspension_point()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                try { yield 1; }
                catch (e) { yield 'caught ' + e; }
            }
            var it = g();
            it.next().then(function() {});
            it.throw('oops').then(function(r) { globalThis.r = r.value });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("caught oops");
    }

    [TestMethod]
    public void Throw_propagates_when_uncaught_rejecting_the_request_promise()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; }
            var it = g();
            it.next().then(function() {});
            it.throw('fatal').then(
                function() { globalThis.r = 'resolved' },
                function(e) { globalThis.r = 'rejected ' + e });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("rejected fatal");
    }

    [TestMethod]
    public void Requests_are_serialized_in_fifo_order()
    {
        // Two next() calls issued back-to-back, before the first settles, must
        // be served in order: the second waits for the first's yield.
        var (runtime, _) = Eval(@"
            async function* g() {
                await Promise.resolve(0);
                yield 'a';
                await Promise.resolve(0);
                yield 'b';
            }
            var it = g();
            globalThis.order = '';
            it.next().then(function(r) { globalThis.order += r.value });
            it.next().then(function(r) { globalThis.order += r.value });
        ");
        runtime.GetGlobal("order").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void For_await_of_iterates_an_async_generator()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; yield 2; yield 3; }
            async function run() {
                var sum = 0;
                for await (var x of g()) { sum += x; }
                return sum;
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void For_await_of_awaits_each_yielded_promise()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                yield await Promise.resolve(10);
                yield await Promise.resolve(20);
            }
            async function run() {
                var parts = [];
                for await (var x of g()) { parts.push(x); }
                return parts.join(',');
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("10,20");
    }

    [TestMethod]
    public void For_await_of_over_a_sync_iterable_of_promises()
    {
        // No [Symbol.asyncIterator] — falls back to the sync iterator
        // (CreateAsyncFromSyncIterator) and awaits each element value.
        var (runtime, _) = Eval(@"
            async function run() {
                var out = [];
                for await (var x of [Promise.resolve('p'), 'q', Promise.resolve('r')]) {
                    out.push(x);
                }
                return out.join('');
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("pqr");
    }

    [TestMethod]
    public void For_await_of_break_closes_the_iterator()
    {
        var (runtime, _) = Eval(@"
            async function* g() {
                try { yield 1; yield 2; yield 3; }
                finally { globalThis.closed = 'yes'; }
            }
            async function run() {
                var first;
                for await (var x of g()) { first = x; break; }
                return first;
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
        runtime.GetGlobal("closed").AsString.Should().Be("yes");
    }

    [TestMethod]
    public void Symbol_asyncIterator_returns_self()
    {
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; }
            var it = g();
            globalThis.r = it[Symbol.asyncIterator]() === it;
        ");
        runtime.GetGlobal("r").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Yield_star_delegates_inside_async_generator()
    {
        var (runtime, _) = Eval(@"
            function* inner() { yield 1; yield 2; }
            async function* g() { yield* inner(); yield 3; }
            async function run() {
                var sum = 0;
                for await (var x of g()) { sum += x; }
                return sum;
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Yield_star_delegates_to_an_async_iterable()
    {
        // The inner is a true async iterator (has @@asyncIterator and its
        // next() returns a promise). Before the fix yield* used the sync
        // iteration protocol and threw "value is not iterable" because the
        // object had no @@iterator.
        var (runtime, _) = Eval(@"
            function makeAsyncIterable(values) {
                return {
                    [Symbol.asyncIterator]() {
                        var i = 0;
                        return {
                            next() {
                                return Promise.resolve(
                                    i < values.length
                                        ? { value: values[i++], done: false }
                                        : { value: undefined, done: true });
                            }
                        };
                    }
                };
            }
            async function* g() {
                yield* makeAsyncIterable([1, 2, 3]);
                yield 4;
            }
            async function run() {
                var sum = 0;
                for await (var x of g()) sum += x;
                return sum;
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Yield_star_delegates_to_async_generator()
    {
        var (runtime, _) = Eval(@"
            async function* inner() { yield 'a'; yield await Promise.resolve('b'); }
            async function* outer() { yield* inner(); yield 'c'; }
            async function run() {
                var out = '';
                for await (var x of outer()) out += x;
                return out;
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("abc");
    }

    [TestMethod]
    public void Yield_star_async_returns_inner_final_value()
    {
        // yield* evaluates to the inner iterator's return value.
        var (runtime, _) = Eval(@"
            async function* inner() { yield 1; return 99; }
            async function* outer() { var v = yield* inner(); yield v; }
            async function run() {
                var parts = [];
                for await (var x of outer()) parts.push(x);
                return parts.join(',');
            }
            run().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("1,99");
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
