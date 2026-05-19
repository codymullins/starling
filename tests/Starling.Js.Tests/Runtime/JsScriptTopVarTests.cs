using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Runtime;

/// <summary>
/// gap:script-top-var-not-global — pins §16.1.7 ScriptEvaluation semantics
/// for top-level <c>var</c> declarations: they install configurable
/// (writable=true, enumerable=true, configurable=false) own data properties
/// on the global object, so nested functions and host code see the same
/// binding by name through <see cref="Opcode.LoadGlobal"/> /
/// <see cref="Opcode.StoreGlobal"/>.
/// </summary>
public class JsScriptTopVarTests
{
    [Fact]
    public void Nested_function_reads_script_top_var()
    {
        // The pin from the gap row.
        Eval("var x = 1; function read() { return x } read()")
            .AsNumber.Should().Be(1);
    }

    [Fact]
    public void Nested_function_writes_back_to_script_top_var()
    {
        Eval("var x = 1; function write() { x = 5 } write(); x")
            .AsNumber.Should().Be(5);
    }

    [Fact]
    public void Var_creates_own_property_on_global_object()
    {
        Eval("var x = 1; globalThis.x").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Var_redeclaration_does_not_reset_value()
    {
        // §9.1.1.4.16 CreateGlobalVarBinding is idempotent: the second
        // `var x` declarator must NOT clobber the initialized value.
        Eval("var x = 1; var x; x").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Var_redeclaration_with_init_uses_second_value()
    {
        Eval("var x = 1; var x = 2; x").AsNumber.Should().Be(2);
        Eval("var x = 1; var x = 2; globalThis.x").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Var_without_init_seeds_undefined_as_global_property()
    {
        // `var x` (no init) must still create the global property — checked
        // via the `in` operator so we don't depend on hasOwnProperty.
        Eval("var x; 'x' in globalThis").AsBool.Should().BeTrue();
        Eval("var x; typeof x").AsString.Should().Be("undefined");
    }

    [Fact]
    public void Function_decl_hoist_wins_over_later_var_redeclaration()
    {
        // §9.1.1.4 hoisting order: function declarations are installed
        // first, then `var` declarators run — but a bare `var f;` without
        // an initializer must NOT overwrite the function value, because
        // CreateGlobalVarBinding skips a name that already binds.
        Eval("function f() { return 1 } var f; typeof f")
            .AsString.Should().Be("function");
        Eval("function f() { return 1 } var f; f()")
            .AsNumber.Should().Be(1);
    }

    [Fact]
    public void Var_inside_function_stays_local_regression()
    {
        // Regression for the closure-write-back agent's work: function-
        // local `var x` must NOT leak to the global object.
        Eval(@"
            var x = 1;
            function f() { var x = 2; return x }
            var inner = f();
            var outer = x;
            inner * 10 + outer;
        ").AsNumber.Should().Be(21);
        Eval(@"
            function f() { var hidden = 7; return hidden }
            f();
            typeof hidden;
        ").AsString.Should().Be("undefined");
    }

    [Fact]
    public void Closure_increments_script_top_var_via_global()
    {
        // Regression for closure-write-back at script top: a nested
        // function's `x++` must observe and mutate the same global
        // binding the outer code sees.
        Eval(@"
            var x = 0;
            function inc() { x++ }
            inc(); inc();
            x;
        ").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Compound_assignment_at_script_top_goes_through_global()
    {
        Eval("var x = 1; x += 10; x").AsNumber.Should().Be(11);
        Eval("var x = 1; x += 10; globalThis.x").AsNumber.Should().Be(11);
    }

    [Fact]
    public void Postfix_increment_at_script_top_yields_old_returns_new()
    {
        Eval("var x = 5; var y = x++; y * 100 + x").AsNumber.Should().Be(506);
    }

    [Fact]
    public void Var_inside_nested_block_at_script_top_still_becomes_global()
    {
        // `var` is function-scoped, not block-scoped — so a `var` inside a
        // block at the top level still creates a global property.
        Eval("{ var inner = 42 } globalThis.inner").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Bare_assignment_then_var_keeps_assigned_value()
    {
        // A bare `x = 5` (LooseGlobalStore) at script top creates an own
        // property; a later `var x;` declarator must not reset it.
        Eval("x = 5; var x; x").AsNumber.Should().Be(5);
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
