using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B1b-2c — Generator tests. Generators run on a worker thread; the
/// dispatcher loop in the VM consults the SuspendedFrame on each .next()
/// to read the yielded value and resume.
/// </summary>
[TestClass]
public class JsGeneratorTests
{
    [TestMethod]
    public void Basic_generator_yields_two_values_then_done()
    {
        var r = Eval(@"
            function* g() { yield 1; yield 2; }
            var it = g();
            var a = it.next();
            var b = it.next();
            var c = it.next();
            '' + a.value + '|' + a.done + ',' + b.value + '|' + b.done + ',' + c.value + '|' + c.done
        ");
        r.AsString.Should().Be("1|false,2|false,undefined|true");
    }

    [TestMethod]
    public void Generator_next_with_value_is_yielded_back()
    {
        var r = Eval(@"
            function* g() {
                var x = yield 'first';
                yield 'got: ' + x;
            }
            var it = g();
            it.next();              // primer — yields 'first'
            it.next('hello').value  // resumed with 'hello'; yields 'got: hello'
        ");
        r.AsString.Should().Be("got: hello");
    }

    [TestMethod]
    public void Generator_return_value_appears_as_final_done_result()
    {
        var r = Eval(@"
            function* g() { return 42; }
            var v = g().next();
            '' + v.value + '|' + v.done
        ");
        r.AsString.Should().Be("42|true");
    }

    [TestMethod]
    public void Generator_for_of_iterates_yielded_values()
    {
        var r = Eval(@"
            function* range(n) {
                var i = 0;
                while (i < n) { yield i; i = i + 1; }
            }
            var sum = 0;
            for (var x of range(4)) sum = sum + x;
            sum
        ");
        r.AsNumber.Should().Be(6); // 0+1+2+3
    }

    [TestMethod]
    public void Generator_is_its_own_iterator_via_symbol_iterator()
    {
        var r = Eval(@"
            function* g() { yield 1 }
            var it = g();
            it[Symbol.iterator]() === it
        ");
        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Generator_throw_is_catchable_in_body()
    {
        var r = Eval(@"
            function* g() {
                try { yield 1 } catch (e) { yield 'caught: ' + e }
            }
            var it = g();
            it.next();
            it.throw('boom').value
        ");
        r.AsString.Should().Be("caught: boom");
    }

    [TestMethod]
    public void Generator_done_after_explicit_return()
    {
        var r = Eval(@"
            function* g() { yield 1; return 'fin'; yield 2; }
            var it = g();
            var a = it.next();
            var b = it.next();
            var c = it.next();
            '' + a.value + ',' + b.value + '/' + b.done + ',' + c.value + '/' + c.done
        ");
        r.AsString.Should().Be("1,fin/true,undefined/true");
    }

    [TestMethod]
    public void Yield_star_delegates_to_inner_generator()
    {
        var r = Eval(@"
            function* a() { yield 1; yield 2 }
            function* b() { yield* a(); yield 3 }
            var out = '';
            for (var x of b()) out = out + x;
            out
        ");
        r.AsString.Should().Be("123");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
