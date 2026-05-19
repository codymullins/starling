using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B1b-2c follow-ups — exercises the two partial-implementation gaps that
/// the original async/await/generator slice flagged:
/// (1) <c>yield*</c> must forward the outer generator's <c>.next(v)</c> /
///     <c>.return(v)</c> / <c>.throw(e)</c> into the delegated iterator's
///     matching method, and propagate the inner iterator's final value as
///     the result of the <c>yield*</c> expression.
/// (2) Generator <c>.return(v)</c> must run any enclosing <c>finally</c>
///     blocks before the body completes (and the finally's own completion
///     wins if it throws or returns a different value).
/// </summary>
[TestClass]
public class JsGeneratorAdvancedTests
{
    // -----------------------------------------------------------------
    //                   Task 1 — yield* protocol forwarding
    // -----------------------------------------------------------------

    [TestMethod]
    public void YieldStar_iterates_array_iterable()
    {
        var r = Eval(@"
            function* g() { yield* [1, 2, 3] }
            var sum = 0;
            for (var x of g()) sum = sum + x;
            sum
        ");
        r.AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void YieldStar_inner_return_value_is_value_of_yield_star_expression()
    {
        var r = Eval(@"
            function* inner() { let x = yield 'a'; return x }
            function* outer() { var r = yield* inner(); yield 'after:' + r }
            var it = outer();
            var a = it.next();        // 'a' from inner's yield
            var b = it.next('boo');   // resumes inner with 'boo'; inner returns 'boo'; outer yields 'after:boo'
            var c = it.next();        // outer falls off → done
            '' + a.value + ',' + b.value + '|' + b.done + ',' + c.value + '|' + c.done
        ");
        r.AsString.Should().Be("a,after:boo|false,undefined|true");
    }

    [TestMethod]
    public void YieldStar_forwards_next_value_into_inner_generator()
    {
        var r = Eval(@"
            function* inner() {
                var a = yield 'p1';
                var b = yield 'p2:' + a;
                return 'fin:' + b;
            }
            function* outer() { return yield* inner() }
            var it = outer();
            var x = it.next();           // 'p1'
            var y = it.next('one');      // 'p2:one'
            var z = it.next('two');      // outer returns 'fin:two', done
            '' + x.value + ',' + y.value + ',' + z.value + '|' + z.done
        ");
        r.AsString.Should().Be("p1,p2:one,fin:two|true");
    }

    [TestMethod]
    public void YieldStar_forwards_return_through_inner_finally()
    {
        var r = Eval(@"
            var cleanup = 'no';
            function* inner() {
                try { yield 1; yield 2 } finally { cleanup = 'yes' }
            }
            function* outer() { yield* inner(); yield 'unreached' }
            var it = outer();
            it.next();
            var r = it.return('done');
            '' + cleanup + '|' + r.value + '|' + r.done
        ");
        r.AsString.Should().Be("yes|done|true");
    }

    [TestMethod]
    public void YieldStar_forwards_throw_to_inner_catch()
    {
        var r = Eval(@"
            function* inner() {
                try { yield 1 } catch (e) { yield 'caught ' + e }
            }
            function* outer() { yield* inner(); yield 'after' }
            var it = outer();
            it.next();
            var t = it.throw('err');
            t.value
        ");
        r.AsString.Should().Be("caught err");
    }

    [TestMethod]
    public void YieldStar_into_iterable_without_throw_raises_typeerror()
    {
        // Custom iterable without a throw method — .throw must fail.
        var r = Eval(@"
            function makeIt() {
                var i = 0;
                return {
                    [Symbol.iterator]() { return this },
                    next() { return i < 3 ? { value: i++, done: false } : { value: undefined, done: true } }
                };
            }
            function* outer() { yield* makeIt() }
            var it = outer();
            it.next();
            var caught = '';
            try { it.throw('x') } catch (e) { caught = '' + e.name }
            caught
        ");
        r.AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void YieldStar_into_iterable_without_return_propagates_return_after_close()
    {
        // No return() on the inner — yield*'s .return propagates the return
        // value through the outer generator's body (which has no finally),
        // so the outer's final {value, done} matches.
        var r = Eval(@"
            function makeIt() {
                var i = 0;
                return {
                    [Symbol.iterator]() { return this },
                    next() { return { value: i++, done: false } }
                };
            }
            function* outer() { yield* makeIt() }
            var it = outer();
            it.next();
            var r = it.return('stop');
            '' + r.value + '|' + r.done
        ");
        r.AsString.Should().Be("stop|true");
    }

    // -----------------------------------------------------------------
    //                   Task 2 — Generator.return runs finally
    // -----------------------------------------------------------------

    [TestMethod]
    public void GeneratorReturn_runs_enclosing_finally()
    {
        var r = Eval(@"
            var ran = 'no';
            function* g() {
                try { yield 1; yield 2 } finally { ran = 'yes' }
            }
            var it = g();
            it.next();
            it.return('x');
            ran
        ");
        r.AsString.Should().Be("yes");
    }

    [TestMethod]
    public void GeneratorReturn_value_propagates_when_finally_completes_normally()
    {
        var r = Eval(@"
            function* g() { try { yield 1 } finally { } }
            var it = g();
            it.next();
            var r = it.return('hello');
            '' + r.value + '|' + r.done
        ");
        r.AsString.Should().Be("hello|true");
    }

    [TestMethod]
    public void GeneratorReturn_finally_throw_overrides_completion()
    {
        var r = Eval(@"
            var caught = '';
            function* g() { try { yield 1 } finally { throw 'overridden' } }
            var it = g();
            it.next();
            try { it.return('orig') } catch (e) { caught = e }
            caught
        ");
        r.AsString.Should().Be("overridden");
    }

    [TestMethod]
    public void GeneratorReturn_before_started_marks_done_with_value()
    {
        var r = Eval(@"
            function* g() { yield 1; yield 2 }
            var it = g();
            var r = it.return('early');
            var n = it.next();
            '' + r.value + '|' + r.done + ',' + n.value + '|' + n.done
        ");
        r.AsString.Should().Be("early|true,undefined|true");
    }

    [TestMethod]
    public void GeneratorReturn_skips_remaining_yields()
    {
        var r = Eval(@"
            var saw = '';
            function* g() {
                try { yield 1; saw += 'A'; yield 2; saw += 'B' }
                finally { saw += 'F' }
            }
            var it = g();
            it.next();
            it.return('x');
            saw
        ");
        r.AsString.Should().Be("F");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
