using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-72 — direct eval caller-scope read/write access and the §19.2.1.3
/// EvalDeclarationInstantiation early errors. A direct eval(...) call resolves
/// free identifiers against the calling function's live variable environment
/// (params / let / const / var) and rejects a var/function declaration in the
/// eval'd code that would collide with one of the caller's lexical bindings.
/// Indirect eval stays global-scoped (no caller access).
/// </summary>
[TestClass]
public class DirectEvalCallerScopeTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    /// <summary>Run source expecting a JsThrow whose error object has the given
    /// constructor name (SyntaxError / ReferenceError / ...).</summary>
    private static void EvalThrows(string src, string expectedName)
    {
        try
        {
            Eval(src);
        }
        catch (JsThrow jt)
        {
            var actual = jt.Value.IsObject
                ? JsValue.ToStringValue(AbstractOperations.Get(new JsVm(new JsRuntime()), jt.Value.AsObject, "name"))
                : JsValue.ToStringValue(jt.Value);
            actual.Should().Be(expectedName);
            return;
        }
        Assert.Fail("Expected a " + expectedName + " from `" + src + "`, but no exception was thrown.");
    }

    // ---- Script-top direct eval (no enclosing function) ---------------------

    [TestMethod]
    public void Direct_eval_at_script_top_reads_global_var()
        // At script top-level a `var` binds on the global object; a direct eval
        // there must still see it (through the global fallback, since script-top
        // bindings are not function locals).
        => Eval("var g = 5; eval('g + 100');").AsNumber.Should().Be(105);

    [TestMethod]
    public void Direct_eval_at_script_top_does_not_crash_on_toplevel_let()
        // Regression: the scope descriptor must not mis-address a script-top
        // let/const as a function local slot (they live on the global object).
        => Eval("let tl = 9; eval('1 + 1');").AsNumber.Should().Be(2);

    // ---- Caller-local READ access -------------------------------------------

    [TestMethod]
    public void Direct_eval_reads_caller_var()
        => Eval("(function(){ var x = 42; return eval('x'); })();")
            .AsNumber.Should().Be(42);

    [TestMethod]
    public void Direct_eval_reads_caller_let()
        => Eval("(function(){ let x = 7; return eval('x + 1'); })();")
            .AsNumber.Should().Be(8);

    [TestMethod]
    public void Direct_eval_reads_caller_const()
        => Eval("(function(){ const k = 5; return eval('k * 2'); })();")
            .AsNumber.Should().Be(10);

    [TestMethod]
    public void Direct_eval_reads_caller_param()
        => Eval("(function(p){ return eval('p * 2'); })(21);")
            .AsNumber.Should().Be(42);

    [TestMethod]
    public void Direct_eval_writes_caller_var_live()
        => Eval("(function(){ var x = 1; eval('x = 99'); return x; })();")
            .AsNumber.Should().Be(99);

    [TestMethod]
    public void Direct_eval_reads_captured_caller_local()
        // x is captured by an inner closure (Cell storage); direct eval must
        // read through the same live cell.
        => Eval("(function(){ var x = 3; var f = () => x; eval('x = 8'); return f(); })();")
            .AsNumber.Should().Be(8);

    // ---- EvalDeclarationInstantiation early errors --------------------------

    [TestMethod]
    public void Direct_eval_var_colliding_with_caller_let_throws_SyntaxError()
        => EvalThrows("(function(){ { let x; { eval('var x;'); } } })();", "SyntaxError");

    [TestMethod]
    public void Direct_eval_var_arguments_colliding_with_caller_let_arguments_throws()
        => EvalThrows("(function f(p){ let arguments; eval('var arguments'); })(1);", "SyntaxError");

    [TestMethod]
    public void Strict_direct_eval_var_does_not_collide_with_caller_let()
        // A strict direct eval gets its OWN variable environment, so its var x
        // does not collide with the caller's lexical x.
        => Eval("(function(){ { let x; { return eval('\"use strict\"; var x; 1'); } } })();")
            .AsNumber.Should().Be(1);

    [TestMethod]
    public void Strict_direct_eval_var_eval_throws_SyntaxError()
        => EvalThrows("(function(){ eval('\"use strict\"; var eval;'); })();", "SyntaxError");

    // ---- Indirect eval stays global-scoped ----------------------------------

    [TestMethod]
    public void Indirect_eval_cannot_see_caller_param()
        // (0, eval) / aliased eval is indirect: caller's p is invisible, so
        // `typeof p` is "undefined" (no caller-scope access).
        => Eval("(function(p){ var g = eval; return g('typeof p'); })(5);")
            .AsString.Should().Be("undefined");

    [TestMethod]
    public void Indirect_eval_reading_caller_local_throws_ReferenceError()
        => EvalThrows("(function(p){ var g = eval; return g('p'); })(5);", "ReferenceError");
}
