using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-23 — runtime JS errors thrown by the VM for the throw-prone opcodes
/// (Call / CallMethod / New / member loads) must carry the originating
/// source position as a trailing <c>(at line:col)</c> so a blocker in a
/// minified bundle can be pinpointed instead of guessed at. The compiler
/// records a sparse offset→position table on the <see cref="Chunk"/>; the VM
/// looks it up by the current ip at the throw site.
/// </summary>
[TestClass]
public class JsErrorPositionTests
{
    [TestMethod]
    public void Method_call_on_missing_property_carries_position()
    {
        // `o.missing()` — missing is undefined, CallMethod throws.
        var act = () => Eval("var o={}; o.missing();");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Contain("(at 1:");
    }

    [TestMethod]
    public void Bare_call_to_undefined_global_carries_position()
    {
        // `nope` is declared (resolvable read, no ReferenceError) but
        // undefined-valued, so calling it is a not-a-function TypeError that must
        // carry position. (A *bare undeclared* call now throws ReferenceError
        // before the call — see the unresolved-global-read tests.)
        var act = () => Eval("var nope;\nnope();");
        var msg = act.Should().Throw<JsThrow>().Which.Value.AsString;
        msg.Should().Contain("(at 2:1)");
    }

    [TestMethod]
    public void New_on_undefined_carries_position()
    {
        var act = () => Eval("new undefined();");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Contain("(at 1:1)");
    }

    [TestMethod]
    public void Call_deep_in_a_function_reports_inner_line()
    {
        // The throw originates inside the function body on line 3, NOT the
        // call site on line 5. The chunk for the function carries its own
        // position table, so the reported line is the body's.
        const string src = """
            function outer() {
              var o = {};
              return o.boom();
            }
            outer();
            """;
        var act = () => Eval(src);
        act.Should().Throw<JsThrow>()
            .Which.Value.AsString.Should().Contain("(at 3:");
    }

    [TestMethod]
    public void Position_points_at_the_column_of_the_member_expression()
    {
        // Two calls on one line; the failing one is the second. Its column
        // must be reported, not the first call's.
        const string src = "var o={f:function(){}}; o.f(); o.g();";
        var act = () => Eval(src);
        var msg = act.Should().Throw<JsThrow>().Which.Value.AsString;
        msg.Should().Contain("g");
        // `o.g()` member expression starts at column 32 (1-based).
        msg.Should().Contain("(at 1:32)");
    }

    [TestMethod]
    public void Successful_calls_do_not_get_a_position_suffix()
    {
        // Sanity: the position table is inert on the happy path.
        Eval("var o={f:function(){return 7;}}; o.f();").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Thrown_error_stack_includes_function_source_line_and_caller()
    {
        const string src = """
            function inner() {
              throw new TypeError('bundle boom');
            }
            function outer() {
              inner();
            }
            outer();
            """;

        var thrown = ((Action)(() => Eval(src))).Should().Throw<JsThrow>().Which.Value;
        var stack = thrown.AsObject.Get("stack").AsString;
        stack.Should().StartWith("TypeError: bundle boom");
        stack.Should().Contain("at inner (<eval>:2:3)");
        stack.Should().Contain("at outer (<eval>:5:3)");
    }

    [TestMethod]
    public void Direct_eval_error_stack_uses_stable_eval_source_name()
    {
        var thrown = ((Action)(() => Eval("eval(\"function f(){\\n  throw new Error('eval boom');\\n}\\nf();\");")))
            .Should().Throw<JsThrow>().Which.Value;

        var stack = thrown.AsObject.Get("stack").AsString;
        stack.Should().StartWith("Error: eval boom");
        stack.Should().Contain("at f (<eval>:2:3)");
    }

    [TestMethod]
    public void Reference_error_stack_includes_identifier_position()
    {
        const string src = """
            function inner() {
              missingBundleGlobal;
            }
            inner();
            """;

        var thrown = ((Action)(() => Eval(src))).Should().Throw<JsThrow>().Which.Value;
        var stack = thrown.AsObject.Get("stack").AsString;
        stack.Should().StartWith("ReferenceError: missingBundleGlobal is not defined");
        stack.Should().Contain("at inner (<eval>:2:3)");
    }

    // ----- Helpers --------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
