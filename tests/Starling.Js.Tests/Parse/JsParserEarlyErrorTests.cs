using AwesomeAssertions;
using Starling.Js.Parse;
namespace Starling.Js.Tests.Parse;

// wp:M3-69 — parse-phase early errors added in a batch (return outside a
// function, await/yield in formals and async/static contexts, optional-chain
// restrictions, coalesce-without-parens, labelled function statements in
// iteration bodies, for-of/for-in head and destructuring rules, class/object
// early errors, catch-binding conflicts). Each cluster pairs an invalid form
// (must be a JsParseException) with a valid neighbor (must parse).
[TestClass]
public class JsParserEarlyErrorTests
{
    private static void Bad(string src)
    {
        Action act = () => new JsParser(src).ParseProgram();
        act.Should().Throw<JsParseException>(src);
    }

    private static void Ok(string src)
    {
        Action act = () => new JsParser(src).ParseProgram();
        act.Should().NotThrow(src);
    }

    // ----- return outside a function (§13.10.1) ----------------------------

    [TestMethod]
    public void Return_outside_function_is_error()
    {
        Bad("return;");
        Bad("return 1;");
        Bad("{ return; }");
        Bad("if (x) return;");
        Bad("for (;;) return;");
        Ok("function f() { return; }");
        Ok("function f() { return 1; }");
        Ok("() => { return 1; };");
        Ok("({ m() { return 1; } });");
    }

    // ----- coalesce without parentheses (§12.6) ----------------------------

    [TestMethod]
    public void Coalesce_mixed_with_logical_is_error()
    {
        Bad("a ?? b || c;");
        Bad("a ?? b && c;");
        Bad("a || b ?? c;");
        Bad("a && b ?? c;");
        Ok("a ?? (b || c);");
        Ok("(a || b) ?? c;");
        Ok("a ?? b ?? c;");
        Ok("a ?? b;");
    }

    // ----- optional chaining restrictions (§13.3.1.1) ----------------------

    [TestMethod]
    public void Optional_chain_restrictions_are_errors()
    {
        Bad("a?.b`tpl`;");          // tagged template on optional chain
        Bad("a?.b()`tpl`;");
        Bad("a?.b++;");             // postfix update on optional chain
        Bad("a?.b--;");
        Bad("--a?.b;");            // prefix update on optional chain
        Bad("++a?.b;");
        Ok("a?.b;");
        Ok("a?.b();");
        Ok("(a?.b)`tpl`;");        // parenthesis breaks the chain — valid
        Ok("a.b`tpl`;");
        Ok("a.b++;");
    }

    // ----- await in async formals / async binding (§15.8.1) ----------------

    [TestMethod]
    public void Await_in_async_arrow_formals_is_error()
    {
        Bad("async (await) => {};");
        Bad("async (x = await) => {};");
        Bad("async await => {};");
        Bad("async (a = await => {}) => {};");      // nested-arrow param position
        Bad("async (a = (await) => {}) => {};");
        Ok("async (a, b) => a;");
        Ok("async (x = 1) => x;");
        Ok("async function f() { await 1; };");      // await expr in body is fine
    }

    [TestMethod]
    public void Await_as_async_function_name_is_error()
    {
        Bad("(async function await() {});");
        Bad("(async function* await() {});");
        Ok("(async function f() {});");
        Ok("(function await() {});");               // sloppy non-async — valid
    }

    // ----- yield/await expressions in formal parameters --------------------

    [TestMethod]
    public void Yield_or_await_expr_in_formals_is_error()
    {
        Bad("function* g(x = yield) {}");
        Bad("function* g() { (x = yield) => {}; }");
        Bad("(async function* (x = await 1) {});");
        Bad("(async function* (x = yield) {});");
        Ok("function* g(x = 1) {}");
        Ok("(async function (x = 1) {});");
    }

    // ----- arrow ASI / duplicate params (§15.3) ----------------------------

    [TestMethod]
    public void Arrow_line_terminator_before_thick_arrow_is_error()
    {
        Bad("var f = ()\n=> {};");
        Bad("var f = x\n=> {};");
        Bad("var f = (a, b)\n=> {};");
        Ok("var f = () => {};");
        Ok("var f = x => x;");
    }

    [TestMethod]
    public void Duplicate_arrow_and_method_params_are_errors()
    {
        Bad("(a, a) => {};");
        Bad("async (a, a) => {};");
        Bad("({ m(a, a) {} });");
        Bad("({ async m(a, a) {} });");
        Ok("(a, b) => {};");
        Ok("({ m(a, b) {} });");
        Ok("function f(a, a) {}");                   // sloppy simple list — still legal
    }

    // ----- accessor arity (§15.4.1) ----------------------------------------

    [TestMethod]
    public void Accessor_arity_is_enforced()
    {
        Bad("({ get x(v) {} });");
        Bad("({ set x() {} });");
        Bad("({ set x(a, b) {} });");
        Bad("({ set x(...r) {} });");
        Bad("(class { get x(v) {} });");
        Bad("(class { set x() {} });");
        Bad("(class { set x(...r) {} });");
        Ok("({ get x() { return 1; }, set x(v) {} });");
        Ok("(class { get x() { return 1; } set x(v) {} });");
    }

    // ----- labelled function statement in iteration body (Annex B.3.2) -----

    [TestMethod]
    public void Labelled_function_in_iteration_body_is_error()
    {
        Bad("for (;;) lbl: function f() {}");
        Bad("for (var x in {}) lbl: function f() {}");
        Bad("for (var x of []) lbl: function f() {}");
        Bad("while (false) lbl: function f() {}");
        Bad("do lbl: function f() {} while (false);");
        Bad("if (false) a: b: function f() {}");      // double label anywhere
        Ok("lbl: function f() {}");                    // sloppy single label — legal
        Ok("for (;;) { lbl: function f() {} }");        // function inside a block — legal
        Ok("if (false) lbl: function f() {}");          // single label in if body — legal
    }

    // ----- for-of / for-in head & destructuring (§14.7.5) ------------------

    [TestMethod]
    public void For_of_head_and_dstr_rules()
    {
        Bad("for (var x of [], []) {}");               // RHS is AssignmentExpression
        Bad("for (var x o\\u0066 []) ;");              // escaped 'of'
        Bad("for (async of [1]) ;");                    // bare async LHS
        Bad("for ([...x,] of [[]]) ;");                // rest-before-comma in dstr
        Ok("for (var x of []) {}");
        Ok("for (x of a) {}");
    }

    [TestMethod]
    public void For_of_rhs_is_single_assignment_expression()
    {
        Bad("for (x of a, b) {}");
        Ok("for (x of (a, b)) {}");                     // parenthesized sequence is one AssignmentExpression
        Ok("for (x of a) {}");
    }

    [TestMethod]
    public void Array_rest_before_comma_in_dstr_is_error()
    {
        Bad("[...x,] = [];");
        Bad("[...x, y] = [];");
        Ok("[...x] = [];");
        Ok("[a, ...x] = [];");
    }

    // ----- cover-initialized name (§13.2.5.1) ------------------------------

    [TestMethod]
    public void Cover_initialized_name_only_valid_in_pattern()
    {
        Bad("({ a = 1 });");
        Bad("f({ a = 1 });");
        Bad("({ a = 1 } + 1);");
        Ok("({ a = 1 } = {});");
        Ok("({ a: { b = 1 } } = {});");
        Ok("({ a = 1 }) => {};");                       // arrow params reinterpret
        Ok("({ a: 1 });");
    }

    // ----- class field / static block early errors (§15.7.1) ---------------

    [TestMethod]
    public void Class_field_named_constructor_is_error()
    {
        Bad("class C { constructor; }");
        Bad("class C { 'constructor'; }");
        Bad("class C { static constructor; }");
        Ok("class C { constructor() {} }");            // a constructor METHOD is fine
        Ok("class C { foo; }");
        Ok("class C { static foo; }");
    }

    [TestMethod]
    public void Static_block_await_and_return_are_errors()
    {
        Bad("class C { static { return; } }");
        Bad("class C { static { await 0; } }");
        Bad("class C { static { (await => 0); } }");
        Bad("class C { static { class await {} } }");
        Bad("class C { static { ({ await }); } }");
        Ok("class C { static { let x = 1; } }");
        Ok("class C { static { (async () => { await 1; })(); } }");  // nested async fn resets
    }

    // ----- catch binding conflicts (§14.15.1) ------------------------------

    [TestMethod]
    public void Catch_binding_conflicts_are_errors()
    {
        Bad("try {} catch ([x, x]) {}");
        Bad("try {} catch (x) { let x; }");
        Bad("try {} catch (e) { function e() {} }");
        Ok("try {} catch (e) {}");
        Ok("try {} catch ([a, b]) {}");
        Ok("try {} catch (x) { var x; }");             // var redeclare is allowed
    }

    // ----- yield delegate / argument no-LineTerminator (§15.5) -------------

    [TestMethod]
    public void Yield_star_after_newline_is_error()
    {
        Bad("function* g() { yield\n* 1; }");
        Ok("function* g() { yield * 1; }");
        Ok("function* g() { yield\n1; }");             // ASI: bare yield then 1
    }

    // ----- shorthand reserved-word in strict mode (§13.2.5.1) --------------

    [TestMethod]
    public void Shorthand_strict_reserved_word_is_error()
    {
        Bad("'use strict'; ({ let });");
        Bad("'use strict'; ({ public });");
        Bad("'use strict'; ({ static });");
        Ok("({ let });");                              // sloppy — legal
        Ok("'use strict'; ({ foo });");
    }
}
