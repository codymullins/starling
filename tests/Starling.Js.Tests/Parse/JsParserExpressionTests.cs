using FluentAssertions;
using Tessera.Js.Ast;
using Tessera.Js.Parse;
using Xunit;

namespace Tessera.Js.Tests.Parse;

public class JsParserExpressionTests
{
    // ----- Primary expressions --------------------------------------------

    [Fact]
    public void Numeric_literal()
        => Parse("42").Should().BeOfType<NumericLiteral>()
            .Which.Value.Should().Be(42.0);

    [Fact]
    public void String_literal()
        => Parse("\"hi\"").Should().BeOfType<StringLiteral>()
            .Which.Value.Should().Be("hi");

    [Fact]
    public void Boolean_literal()
        => Parse("true").Should().BeOfType<BooleanLiteral>()
            .Which.Value.Should().BeTrue();

    [Fact]
    public void Null_literal() => Parse("null").Should().BeOfType<NullLiteral>();

    [Fact]
    public void This_expression() => Parse("this").Should().BeOfType<ThisExpression>();

    [Fact]
    public void Identifier_expression()
        => Parse("foo").Should().BeOfType<Identifier>()
            .Which.Name.Should().Be("foo");

    // ----- Binary precedence ----------------------------------------------

    [Fact]
    public void Multiplication_binds_tighter_than_addition()
    {
        // 1 + 2 * 3 → BinaryExpr(+, 1, BinaryExpr(*, 2, 3))
        var bin = Parse("1 + 2 * 3").Should().BeOfType<BinaryExpression>().Subject;
        bin.Op.Should().Be("+");
        bin.Right.Should().BeOfType<BinaryExpression>()
            .Which.Op.Should().Be("*");
    }

    [Fact]
    public void Exponentiation_is_right_associative()
    {
        // 2 ** 3 ** 4 → BinaryExpr(**, 2, BinaryExpr(**, 3, 4))
        var bin = Parse("2 ** 3 ** 4").Should().BeOfType<BinaryExpression>().Subject;
        bin.Op.Should().Be("**");
        bin.Left.Should().BeOfType<NumericLiteral>().Which.Value.Should().Be(2);
        bin.Right.Should().BeOfType<BinaryExpression>()
            .Which.Op.Should().Be("**");
    }

    [Fact]
    public void Parens_override_precedence()
    {
        // (1 + 2) * 3 → BinaryExpr(*, BinaryExpr(+, 1, 2), 3)
        var bin = Parse("(1 + 2) * 3").Should().BeOfType<BinaryExpression>().Subject;
        bin.Op.Should().Be("*");
        bin.Left.Should().BeOfType<BinaryExpression>()
            .Which.Op.Should().Be("+");
    }

    [Fact]
    public void Comparison_then_logical_and()
    {
        // a < b && c > d → Logical(&&, Binary(<, a, b), Binary(>, c, d))
        var log = Parse("a < b && c > d").Should().BeOfType<LogicalExpression>().Subject;
        log.Op.Should().Be("&&");
        log.Left.Should().BeOfType<BinaryExpression>().Which.Op.Should().Be("<");
        log.Right.Should().BeOfType<BinaryExpression>().Which.Op.Should().Be(">");
    }

    [Fact]
    public void Nullish_and_logical_compose()
    {
        // a ?? b || c — ?? has lower precedence than ||, so:
        // a ?? (b || c)
        var log = Parse("a ?? b || c").Should().BeOfType<LogicalExpression>().Subject;
        log.Op.Should().Be("??");
        log.Right.Should().BeOfType<LogicalExpression>().Which.Op.Should().Be("||");
    }

    // ----- Unary and update -----------------------------------------------

    [Fact]
    public void Unary_minus_then_arithmetic()
    {
        // -a * b → Binary(*, Unary(-, a), b)
        var bin = Parse("-a * b").Should().BeOfType<BinaryExpression>().Subject;
        bin.Left.Should().BeOfType<UnaryExpression>().Which.Op.Should().Be("-");
        bin.Op.Should().Be("*");
    }

    [Fact]
    public void Typeof_and_void()
    {
        var u = Parse("typeof x").Should().BeOfType<UnaryExpression>().Subject;
        u.Op.Should().Be("typeof");
        Parse("void 0").Should().BeOfType<UnaryExpression>().Which.Op.Should().Be("void");
    }

    [Fact]
    public void Postfix_increment()
    {
        var u = Parse("a++").Should().BeOfType<UpdateExpression>().Subject;
        u.Op.Should().Be("++");
        u.Prefix.Should().BeFalse();
    }

    [Fact]
    public void Prefix_decrement()
    {
        var u = Parse("--a").Should().BeOfType<UpdateExpression>().Subject;
        u.Op.Should().Be("--");
        u.Prefix.Should().BeTrue();
    }

    // ----- Conditional and assignment -------------------------------------

    [Fact]
    public void Conditional_expression()
    {
        var c = Parse("a ? b : c").Should().BeOfType<ConditionalExpression>().Subject;
        ((Identifier)c.Test).Name.Should().Be("a");
        ((Identifier)c.Consequent).Name.Should().Be("b");
        ((Identifier)c.Alternate).Name.Should().Be("c");
    }

    [Fact]
    public void Assignment_is_right_associative()
    {
        // a = b = c → Assign(=, a, Assign(=, b, c))
        var a = Parse("a = b = c").Should().BeOfType<AssignmentExpression>().Subject;
        a.Op.Should().Be("=");
        a.Value.Should().BeOfType<AssignmentExpression>().Which.Op.Should().Be("=");
    }

    [Fact]
    public void Compound_assignment()
    {
        Parse("a += 1").Should().BeOfType<AssignmentExpression>().Which.Op.Should().Be("+=");
    }

    // ----- Member access and calls ----------------------------------------

    [Fact]
    public void Member_access_dot()
    {
        var m = Parse("a.b.c").Should().BeOfType<MemberExpression>().Subject;
        m.Computed.Should().BeFalse();
        ((Identifier)m.Property).Name.Should().Be("c");
        var inner = m.Object.Should().BeOfType<MemberExpression>().Subject;
        ((Identifier)inner.Property).Name.Should().Be("b");
    }

    [Fact]
    public void Member_access_computed()
    {
        var m = Parse("a[0]").Should().BeOfType<MemberExpression>().Subject;
        m.Computed.Should().BeTrue();
        m.Property.Should().BeOfType<NumericLiteral>();
    }

    [Fact]
    public void Call_expression_with_args()
    {
        var c = Parse("foo(1, 2)").Should().BeOfType<CallExpression>().Subject;
        c.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void Call_after_member_chain()
    {
        // a.b().c
        var m = Parse("a.b().c").Should().BeOfType<MemberExpression>().Subject;
        var call = m.Object.Should().BeOfType<CallExpression>().Subject;
        call.Callee.Should().BeOfType<MemberExpression>();
    }

    [Fact]
    public void Optional_chaining()
    {
        var m = Parse("a?.b").Should().BeOfType<MemberExpression>().Subject;
        m.Optional.Should().BeTrue();
        ((Identifier)m.Property).Name.Should().Be("b");
    }

    [Fact]
    public void New_expression()
    {
        var n = Parse("new Date()").Should().BeOfType<NewExpression>().Subject;
        ((Identifier)n.Callee).Name.Should().Be("Date");
        n.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void New_with_args_and_member()
    {
        var n = Parse("new X.Y(1)").Should().BeOfType<NewExpression>().Subject;
        n.Callee.Should().BeOfType<MemberExpression>();
        n.Arguments.Should().HaveCount(1);
    }

    // ----- Arrays + objects -----------------------------------------------

    [Fact]
    public void Array_literal()
    {
        var a = Parse("[1, 2, 3]").Should().BeOfType<ArrayExpression>().Subject;
        a.Elements.Should().HaveCount(3);
    }

    [Fact]
    public void Array_with_hole()
    {
        var a = Parse("[1, , 3]").Should().BeOfType<ArrayExpression>().Subject;
        a.Elements.Should().HaveCount(3);
        a.Elements[1].Should().BeNull();
    }

    [Fact]
    public void Array_spread()
    {
        var a = Parse("[...rest]").Should().BeOfType<ArrayExpression>().Subject;
        a.Elements.Should().ContainSingle().Which.Should().BeOfType<SpreadElement>();
    }

    [Fact]
    public void Object_literal_with_key_value_and_shorthand()
    {
        var o = Parse("{ a: 1, b }").Should().BeOfType<ObjectExpression>().Subject;
        o.Properties.Should().HaveCount(2);
        o.Properties[0].Shorthand.Should().BeFalse();
        o.Properties[1].Shorthand.Should().BeTrue();
    }

    [Fact]
    public void Object_literal_with_computed_key()
    {
        var o = Parse("{ [k]: v }").Should().BeOfType<ObjectExpression>().Subject;
        o.Properties[0].Computed.Should().BeTrue();
    }

    [Fact]
    public void Object_literal_with_reserved_word_as_key()
    {
        // { class: 1 } — reserved words are allowed as property names.
        var o = Parse("{ class: 1 }").Should().BeOfType<ObjectExpression>().Subject;
        o.Properties.Should().ContainSingle();
    }

    // ----- Sequence expression --------------------------------------------

    [Fact]
    public void Top_level_sequence_via_comma()
    {
        var s = Parse("a, b, c").Should().BeOfType<SequenceExpression>().Subject;
        s.Expressions.Should().HaveCount(3);
    }

    // ----- Errors ---------------------------------------------------------

    [Fact]
    public void Trailing_garbage_throws()
    {
        var act = () => Parse("1 + 2 foo");
        act.Should().Throw<JsParseException>();
    }

    [Fact]
    public void Unclosed_paren_throws()
    {
        var act = () => Parse("(1 + 2");
        act.Should().Throw<JsParseException>();
    }

    // ----- Helpers --------------------------------------------------------

    private static Expression Parse(string src) => new JsParser(src).ParseExpression();
}
