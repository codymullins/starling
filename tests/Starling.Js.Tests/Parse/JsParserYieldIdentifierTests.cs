using AwesomeAssertions;
using Starling.Js.Ast;
using Starling.Js.Parse;

namespace Starling.Js.Tests.Parse;

/// <summary>
/// wp:M3-62a — <c>yield</c> as a BindingIdentifier / IdentifierReference in
/// non-generator, non-strict contexts. Per §12.7.1 and §13.1, <c>yield</c> is
/// only reserved inside generator bodies and in strict mode; everywhere else it
/// is a legal identifier.
/// </summary>
[TestClass]
public class JsParserYieldIdentifierTests
{
    // -----------------------------------------------------------------------
    // Sloppy non-generator: yield valid as identifier
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Yield_as_var_binding_name_in_sloppy_code()
    {
        // `var yield = 1;` is legal in sloppy non-generator code.
        var p = ParseProgram("var yield = 1;");
        var vd = p.Body[0].Should().BeOfType<VariableDeclaration>().Subject;
        ((Identifier)vd.Declarations[0].Id).Name.Should().Be("yield");
    }

    [TestMethod]
    public void Yield_as_identifier_reference_in_sloppy_expression()
    {
        // `yield + 1` in sloppy code: yield is an identifier.
        var p = ParseProgram("var yield = 4; yield + 1;");
        p.Body.Should().HaveCount(2);
    }

    [TestMethod]
    public void Yield_as_function_expression_binding_identifier_in_sloppy_code()
    {
        // `(function yield() {})` is legal in sloppy non-generator code.
        var expr = ParseExpr("(function yield() { return 1; })");
        var fe = expr.Should().BeOfType<FunctionExpression>().Subject;
        fe.Name.Should().NotBeNull();
        fe.Name!.Name.Should().Be("yield");
        fe.Generator.Should().BeFalse();
    }

    [TestMethod]
    public void Yield_as_param_name_in_sloppy_non_generator()
    {
        // `function f(yield) {}` is legal sloppy non-generator.
        var p = ParseProgram("function f(yield) { return yield; }");
        var fd = p.Body[0].Should().BeOfType<FunctionDeclaration>().Subject;
        ((Identifier)fd.Params[0]).Name.Should().Be("yield");
    }

    [TestMethod]
    public void Yield_as_destructuring_default_in_sloppy_code()
    {
        // `var [x = yield] = iter;` — yield is an identifier reference.
        var p = ParseProgram("var yield = 4; var [x = yield] = [];");
        p.Body.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Non-generator function expression with yield as name INSIDE a generator
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Yield_as_function_expression_name_inside_generator_body()
    {
        // ES2024 §15.2.1: the BindingIdentifier of a FunctionExpression uses the
        // grammar for the function's own context (non-generator here), so `yield`
        // is legal even though the outer scope is a generator.
        // Mirrors test262: yield-as-function-expression-binding-identifier.js
        var p = ParseProgram("function* g() { (function yield() {}); }");
        p.Body.Should().HaveCount(1);
        var gd = p.Body[0].Should().BeOfType<FunctionDeclaration>().Subject;
        gd.Generator.Should().BeTrue();
        var stmt = gd.Body.Body[0].Should().BeOfType<ExpressionStatement>().Subject;
        var fe = stmt.Expression.Should().BeOfType<FunctionExpression>().Subject;
        fe.Name!.Name.Should().Be("yield");
        fe.Generator.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Async non-generator: yield valid as identifier reference
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Yield_as_identifier_in_async_non_generator_function()
    {
        // Inside an async (non-generator) function, `yield` is a plain identifier.
        var p = ParseProgram("async function fn() { var yield = 1; return yield; }");
        p.Body.Should().HaveCount(1);
    }

    [TestMethod]
    public void Yield_as_for_await_destructuring_default_in_async_function()
    {
        // Mirrors test262: async-func-decl-dstr-array-elem-init-yield-ident-valid.js
        // `for await ([x = yield] of iter)` inside async non-generator.
        var p = ParseProgram("var yield = 4; async function fn() { for await ([x = yield] of iter) {} }");
        p.Body.Should().HaveCount(2);
    }

    [TestMethod]
    public void Yield_as_rest_destructuring_ident_in_async_function_for_await()
    {
        // Mirrors test262: async-func-decl-dstr-array-rest-yield-ident-valid.js
        // `for await ([...x[yield]] of ...)` — yield used as identifier in subscript.
        var p = ParseProgram("var yield = 'prop'; async function fn() { for await ([...x[yield]] of [[1,2]]) {} }");
        p.Body.Should().HaveCount(2);
    }

    [TestMethod]
    public void Yield_as_object_destructuring_init_in_async_function_for_await()
    {
        // Mirrors test262: async-func-decl-dstr-obj-id-init-yield-ident-valid.js
        var p = ParseProgram("var yield = 1; async function fn() { for await ({x = yield} of [{}]) {} }");
        p.Body.Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Error cases: yield MUST remain reserved in generators and strict mode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Yield_as_var_binding_name_inside_generator_is_error()
    {
        // §14.4.1 — inside a generator, yield is the keyword.
        Action act = () => ParseProgram("function* g() { var yield = 1; }");
        act.Should().Throw<JsParseException>()
            .WithMessage("*yield*");
    }

    [TestMethod]
    public void Yield_as_binding_identifier_in_strict_mode_is_error()
    {
        // §12.7.2 — in strict code, yield is a FutureReservedWord.
        Action act = () => ParseProgram("\"use strict\"; var yield = 1;");
        act.Should().Throw<JsParseException>()
            .WithMessage("*yield*");
    }

    [TestMethod]
    public void Yield_as_identifier_reference_inside_generator_is_error()
    {
        // Inside a generator, `void yield` must be rejected.
        Action act = () => ParseProgram("function* g() { void yield; }");
        act.Should().Throw<JsParseException>()
            .WithMessage("*yield*");
    }

    [TestMethod]
    public void Yield_as_param_in_generator_is_error()
    {
        // Generator parameters may not use `yield` as a binding identifier.
        Action act = () => ParseProgram("function* g(yield) {}");
        act.Should().Throw<JsParseException>()
            .WithMessage("*yield*");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Program ParseProgram(string src) => new JsParser(src).ParseProgram();
    private static Expression ParseExpr(string src) => new JsParser(src).ParseExpression();
}
