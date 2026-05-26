using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

/// <summary>wp:M3-79 — statement completion values (§13–§14 with §13.2.13
/// UpdateEmpty): what an <c>eval</c> of a source string returns. Exercised
/// through the same <see cref="JsCompiler.CompileForEval"/> entry the global
/// <c>eval</c> builtin uses. Mirrors the tc39/test262
/// <c>language/statements/*/cptn-*</c> corpus.</summary>
[TestClass]
public class StatementCompletionValueTests
{
    [TestMethod]
    public void Expression_and_empty_statements()
    {
        EvalNum("1; ;").Should().Be(1);          // EmptyStatement is empty → keep prior
        EvalNum("2;;").Should().Be(2);
        EvalNum("3;;;").Should().Be(3);
        Eval(";").IsUndefined.Should().BeTrue();  // lone EmptyStatement → undefined
        EvalNum("1 + 2").Should().Be(3);          // last expr without trailing `;`
    }

    [TestMethod]
    public void Declarations_have_empty_completion()
    {
        // var/let/const/function/class declarations are empty (UpdateEmpty keeps
        // the prior value); a lone declaration yields undefined.
        EvalNum("1; var x = 2").Should().Be(1);
        EvalNum("7; let a;").Should().Be(7);
        EvalNum("9; let b = 10;").Should().Be(9);
        EvalNum("4; const c = 5;").Should().Be(4);
        EvalNum("1; function f() {}").Should().Be(1);
        EvalNum("1; class C {}").Should().Be(1);
        Eval("var x = 2").IsUndefined.Should().BeTrue();
        Eval("function f() {}").IsUndefined.Should().BeTrue();
        Eval("class C {}").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Block_completion_is_transparent_keep_prior_when_empty()
    {
        Eval("{ }").IsUndefined.Should().BeTrue();   // empty block → empty → undefined
        EvalNum("99; { }").Should().Be(99);          // empty block keeps prior
        EvalNum("{ 3 }").Should().Be(3);
        EvalNum("99; { 1; var x = 2 }").Should().Be(1);
    }

    [TestMethod]
    public void If_completion_overwrites_with_branch_value_or_undefined()
    {
        Eval("if(false) 1").IsUndefined.Should().BeTrue();
        EvalNum("if(true) 3").Should().Be(3);
        Eval("1; if (false) { }").IsUndefined.Should().BeTrue();   // overwrites the 1
        EvalNum("2; if (true) { 3; }").Should().Be(3);
        Eval("1; if (true) { }").IsUndefined.Should().BeTrue();
        EvalNum("2; if (false) { } else { 3; }").Should().Be(3);
        EvalNum("6; if (false) { 7; } else { 8; }").Should().Be(8);
    }

    [TestMethod]
    public void Switch_accumulates_matched_clauses_with_update_empty()
    {
        EvalNum("switch(1){case 1: 5}").Should().Be(5);
        EvalNum("1; switch (\"a\") { case \"a\": 2; default: 3; }").Should().Be(3);
        EvalNum("6; switch (\"a\") { case \"a\": 7; default: }").Should().Be(7); // fall-thru empty keeps 7
        Eval("1; switch (\"a\") { case null: }").IsUndefined.Should().BeTrue();  // no match → undefined
        Eval("2; switch (\"a\") { case null: 3; }").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Loops_init_running_value_to_undefined_then_accumulate()
    {
        EvalNum("for(var i=0;i<3;i++) i").Should().Be(2);
        Eval("1; for (var run = false; run; ) { 3; }").IsUndefined.Should().BeTrue(); // zero iterations
        Eval("while(false) 1").IsUndefined.Should().BeTrue();
        Eval("1; while (false) { 3; }").IsUndefined.Should().BeTrue();
        EvalNum("1; do { 3; } while (false)").Should().Be(3);
        Eval("1; do { } while (false)").IsUndefined.Should().BeTrue();
        // break/continue preserve the running value.
        EvalNum("2; while (true) { 3; break; }").Should().Be(3);
        Eval("1; while (true) { break; }").IsUndefined.Should().BeTrue();
        EvalNum("8; do { 9; if (true) { 10; continue; } 11; } while (false)").Should().Be(10);
    }

    [TestMethod]
    public void ForIn_and_ForOf_completion()
    {
        EvalNum("var s = 0; for (var k in {a:1, b:2}) s").Should().Be(0);
        Eval("1; for (var k in {}) { 3; }").IsUndefined.Should().BeTrue(); // zero iterations
        EvalNum("for (var v of [10, 20, 30]) v").Should().Be(30);
        Eval("1; for (var v of []) { 3; }").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Try_completion_is_block_or_catch_value_finally_discarded()
    {
        EvalNum("try{7}finally{9}").Should().Be(7);
        Eval("1; try { } catch (err) { }").IsUndefined.Should().BeTrue();
        EvalNum("2; try { 3; } catch (err) { }").Should().Be(3);
        EvalNum("2; try { throw null; } catch (err) { 3; }").Should().Be(3);
        EvalNum("6; try { 7; } finally { 8; }").Should().Be(7); // finally value discarded
        Eval("4; try { } finally { 5; }").IsUndefined.Should().BeTrue();
        Eval("1; try { } catch (err) { } finally { }").IsUndefined.Should().BeTrue();
        EvalNum("9; try { 10; } catch (err) { } finally { }").Should().Be(10);
    }

    [TestMethod]
    public void Labeled_and_with_completion()
    {
        EvalNum("test262id: 2;").Should().Be(2);
        EvalNum("test262id: { 5; break test262id; 9; }").Should().Be(5);
        Eval("1; with({}) { }").IsUndefined.Should().BeTrue();
        EvalNum("2; with({}) { 3; }").Should().Be(3);
    }

    private static double EvalNum(string src) => Eval(src).AsNumber;

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
