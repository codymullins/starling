using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// A <c>let</c>/<c>const</c> declared at the top of a <c>catch</c> block must be
/// TDZ-hoisted into the catch block's scope, like any other block. Regression
/// for a compiler bug where the catch body was emitted without the lexical-hoist
/// pass (unlike the BlockStatement path), so <c>const x</c> inside <c>catch</c>
/// threw "missing declared lexical 'x'" at compile time. This is the exact shape
/// WPT's testharness.js uses (a <c>const</c> + <c>for (const […] of …)</c> inside
/// a catch), which blocked the entire web-platform test surface from running.
/// </summary>
[TestClass]
public class CatchBlockLexicalTests
{
    [TestMethod]
    public void Const_in_catch_block_is_hoisted()
        => Eval("let out = ''; try { throw 1; } catch (e) { const x = 5; out = 'x=' + x; } out;")
            .AsString.Should().Be("x=5");

    [TestMethod]
    public void Let_in_catch_block_is_hoisted()
        => Eval("let out = ''; try { throw 1; } catch (e) { let y = 7; y += 1; out = 'y=' + y; } out;")
            .AsString.Should().Be("y=8");

    [TestMethod]
    public void For_of_const_destructuring_in_catch_block()
        => Eval("""
            let seen = '';
            try { throw 0; }
            catch (e) {
                const props = { a: 1, b: 2 };
                for (const [k, v] of Object.entries(props)) { seen += k + v; }
            }
            seen;
            """).AsString.Should().Be("a1b2");

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
