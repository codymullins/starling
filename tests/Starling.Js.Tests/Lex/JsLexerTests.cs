using AwesomeAssertions;
using Starling.Js.Lex;
using Starling.Spec;
namespace Starling.Js.Tests.Lex;

[TestClass]
public class JsLexerTests
{
    // ----- Whitespace / EOF -----------------------------------------------

    [TestMethod]
    public void Empty_source_yields_only_eof()
        => Kinds("").Should().Equal(JsTokenKind.EndOfFile);

    [TestMethod]
    public void Pure_whitespace_skipped()
        => Kinds("   \t  \n  ").Should().Equal(JsTokenKind.EndOfFile);

    // ----- Identifier / keyword -------------------------------------------

    [TestMethod]
    public void Bare_identifier()
    {
        var t = First("foo");
        t.Kind.Should().Be(JsTokenKind.Identifier);
        t.Lexeme.Should().Be("foo");
    }

    [TestMethod]
    public void Identifier_with_dollar_and_underscore_chars()
    {
        First("_$x123").Lexeme.Should().Be("_$x123");
    }

    [TestMethod]
    public void Keywords_get_their_dedicated_kinds()
    {
        Kinds("var let const if else return function class").Should().Equal(
            JsTokenKind.Var,
            JsTokenKind.Identifier,    // 'let' is contextual → Identifier
            JsTokenKind.Const,
            JsTokenKind.If,
            JsTokenKind.Else,
            JsTokenKind.Return,
            JsTokenKind.Function,
            JsTokenKind.Class,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Boolean_and_null_literals_are_their_own_kinds()
    {
        var toks = Tokens("true false null");
        toks[0].Kind.Should().Be(JsTokenKind.BooleanLiteral);
        toks[0].Value.Should().Be(true);
        toks[1].Kind.Should().Be(JsTokenKind.BooleanLiteral);
        toks[1].Value.Should().Be(false);
        toks[2].Kind.Should().Be(JsTokenKind.NullLiteral);
    }

    // ----- Numeric literal ------------------------------------------------

    [TestMethod]
    public void Integer_literal_decoded_as_double()
    {
        var t = First("42");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(42.0);
    }

    [TestMethod]
    public void Float_literal_decoded()
    {
        First("3.14").Value.Should().Be(3.14);
    }

    [TestMethod]
    public void Exponent_literal()
    {
        First("1.5e10").Value.Should().Be(1.5e10);
    }

    [TestMethod]
    public void Negative_exponent()
    {
        First("2e-3").Value.Should().Be(0.002);
    }

    [TestMethod]
    public void Hex_literal()
    {
        First("0xFF").Value.Should().Be(255.0);
    }

    [TestMethod]
    public void Binary_literal()
    {
        First("0b1010").Value.Should().Be(10.0);
    }

    [TestMethod]
    public void Octal_literal()
    {
        First("0o17").Value.Should().Be(15.0);
    }

    [TestMethod]
    public void BigInt_literal()
    {
        var t = First("12345n");
        t.Kind.Should().Be(JsTokenKind.BigIntLiteral);
        t.Value.Should().Be("12345");
    }

    [TestMethod]
    public void BigInt_hex_literal()
    {
        var t = First("0xFFn");
        t.Kind.Should().Be(JsTokenKind.BigIntLiteral);
        t.Value.Should().Be("FF");
    }

    // ----- String literal -------------------------------------------------

    [TestMethod]
    public void Double_quoted_string()
    {
        var t = First("\"hello\"");
        t.Kind.Should().Be(JsTokenKind.StringLiteral);
        t.Value.Should().Be("hello");
    }

    [TestMethod]
    public void Single_quoted_string()
    {
        First("'world'").Value.Should().Be("world");
    }

    [TestMethod]
    public void String_escapes_n_t_quote_backslash()
    {
        First("\"a\\n\\t\\\"\\\\b\"").Value.Should().Be("a\n\t\"\\b");
    }

    [TestMethod]
    public void Hex_escape_x41_is_A()
    {
        First("\"\\x41\"").Value.Should().Be("A");
    }

    [TestMethod]
    public void Unicode_escape_u0041_is_A()
    {
        First("\"\\u0041\"").Value.Should().Be("A");
    }

    [TestMethod]
    public void Unicode_escape_braces_for_supplementary_planes()
    {
        // U+1F600 — grinning face emoji
        First("\"\\u{1F600}\"").Value.Should().Be("😀");
    }

    [TestMethod]
    public void Unterminated_string_reports_error()
    {
        var sink = new RecordingSink();
        var lex = new JsLexer("\"oops", sink);
        lex.Next();
        sink.Errors.Should().NotBeEmpty()
            .And.Contain(e => e.code == JsLexError.UnterminatedString);
    }

    // ----- Comments -------------------------------------------------------

    [TestMethod]
    public void Line_comment_skipped()
        => Kinds("// a comment\n42").Should().Equal(
            JsTokenKind.NumericLiteral, JsTokenKind.EndOfFile);

    [TestMethod]
    public void Block_comment_skipped()
        => Kinds("/* one\nline */ 42").Should().Equal(
            JsTokenKind.NumericLiteral, JsTokenKind.EndOfFile);

    [TestMethod]
    public void Unterminated_block_comment_reports_error()
    {
        var sink = new RecordingSink();
        new JsLexer("/* nope", sink).Drain();
        sink.Errors.Should().ContainSingle()
            .Which.code.Should().Be(JsLexError.UnterminatedComment);
    }

    // ----- Punctuators ----------------------------------------------------

    [TestMethod]
    public void Single_char_punctuators()
    {
        Kinds("(){}[];,.+-*%/").Should().Equal(
            JsTokenKind.LParen, JsTokenKind.RParen,
            JsTokenKind.LBrace, JsTokenKind.RBrace,
            JsTokenKind.LBracket, JsTokenKind.RBracket,
            JsTokenKind.Semicolon, JsTokenKind.Comma, JsTokenKind.Dot,
            JsTokenKind.Plus, JsTokenKind.Minus, JsTokenKind.Star,
            JsTokenKind.Percent, JsTokenKind.Slash,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Compound_assignment_operators()
    {
        Kinds("+= -= *= **= /= %= &&= ||= ??=").Should().Equal(
            JsTokenKind.PlusEq, JsTokenKind.MinusEq,
            JsTokenKind.StarEq, JsTokenKind.StarStarEq,
            JsTokenKind.SlashEq, JsTokenKind.PercentEq,
            JsTokenKind.AmpAmpEq, JsTokenKind.PipePipeEq, JsTokenKind.QuestionQuestionEq,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Three_way_equality_operators()
    {
        Kinds("== != === !==").Should().Equal(
            JsTokenKind.EqEq, JsTokenKind.BangEq,
            JsTokenKind.EqEqEq, JsTokenKind.BangEqEq,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Bit_shift_operators_including_zerofill()
    {
        Kinds("<< >> >>> <<= >>= >>>=").Should().Equal(
            JsTokenKind.LtLt, JsTokenKind.GtGt, JsTokenKind.GtGtGt,
            JsTokenKind.LtLtEq, JsTokenKind.GtGtEq, JsTokenKind.GtGtGtEq,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Optional_chaining_and_nullish_coalescing()
    {
        Kinds("a?.b ?? c").Should().Equal(
            JsTokenKind.Identifier, JsTokenKind.QuestionDot, JsTokenKind.Identifier,
            JsTokenKind.QuestionQuestion, JsTokenKind.Identifier,
            JsTokenKind.EndOfFile);
    }

    [TestMethod]
    public void Arrow_and_spread()
    {
        Kinds("(a) => [...b]").Should().Equal(
            JsTokenKind.LParen, JsTokenKind.Identifier, JsTokenKind.RParen,
            JsTokenKind.Arrow,
            JsTokenKind.LBracket, JsTokenKind.Ellipsis, JsTokenKind.Identifier,
            JsTokenKind.RBracket,
            JsTokenKind.EndOfFile);
    }

    // ----- Position tracking ----------------------------------------------

    [TestMethod]
    public void Token_positions_track_lines_and_columns()
    {
        var t = Tokens("x\n  y");
        // 'x' at line 1 col 1; 'y' at line 2 col 3.
        t[0].Start.Should().Be(new JsPosition(1, 1, 0));
        t[1].Start.Should().Be(new JsPosition(2, 3, 4));
    }

    [TestMethod]
    public void Newlines_between_tokens_mark_PrecededByLineTerminator()
    {
        var t = Tokens("a\nb");
        t[0].PrecededByLineTerminator.Should().BeFalse();
        t[1].PrecededByLineTerminator.Should().BeTrue();
    }

    // ----- Round-trip on a real-ish snippet -------------------------------

    [TestMethod]
    public void Realistic_snippet_tokenizes_cleanly()
    {
        var src = "function add(a, b) { return a + b; }";
        var kinds = Kinds(src);
        kinds.Should().Equal(
            JsTokenKind.Function, JsTokenKind.Identifier,
            JsTokenKind.LParen, JsTokenKind.Identifier, JsTokenKind.Comma, JsTokenKind.Identifier,
            JsTokenKind.RParen, JsTokenKind.LBrace,
            JsTokenKind.Return, JsTokenKind.Identifier, JsTokenKind.Plus,
            JsTokenKind.Identifier, JsTokenKind.Semicolon,
            JsTokenKind.RBrace,
            JsTokenKind.EndOfFile);
    }

    // ----- Peek -----------------------------------------------------------

    [TestMethod]
    public void Peek_does_not_consume()
    {
        var l = new JsLexer("a b");
        l.Peek().Lexeme.Should().Be("a");
        l.Peek().Lexeme.Should().Be("a"); // still
        l.Next().Lexeme.Should().Be("a");
        l.Next().Lexeme.Should().Be("b");
    }

    // ----- Leading-dot numeric literals (§12.9.3 DecimalLiteral) -----------
    // ECMAScript allows ". DecimalDigits ExponentPart?" — e.g. .5, .25e3, .0
    // These tests fail before the fix and are promoted to [SpecFact] after.

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_simple_fraction()
    {
        var t = First(".5");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(0.5);
        t.Lexeme.Should().Be(".5");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_multi_digit_fraction()
    {
        var t = First(".25");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(0.25);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_with_exponent()
    {
        var t = First(".25e3");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(250.0);
        t.Lexeme.Should().Be(".25e3");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_with_negative_exponent()
    {
        var t = First(".5e-1");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(0.05);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_zero()
    {
        var t = First(".0");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(0.0);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_in_expression_context()
    {
        // (.5).toFixed(1) tokenizes as ( .5 ) . identifier ( 1 ) — the .5 is a number
        var kinds = Kinds("(.5).toFixed(1)");
        kinds.Should().Equal(
            JsTokenKind.LParen,
            JsTokenKind.NumericLiteral,
            JsTokenKind.RParen,
            JsTokenKind.Dot,
            JsTokenKind.Identifier,
            JsTokenKind.LParen,
            JsTokenKind.NumericLiteral,
            JsTokenKind.RParen,
            JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Leading_dot_in_var_initializer()
    {
        // var x = .5; must parse as var / ident / = / .5-numeric / ;
        var kinds = Kinds("var x = .5;");
        kinds.Should().Equal(
            JsTokenKind.Var,
            JsTokenKind.Identifier,
            JsTokenKind.Eq,
            JsTokenKind.NumericLiteral,
            JsTokenKind.Semicolon,
            JsTokenKind.EndOfFile);
        // Confirm the value
        var toks = Tokens("var x = .5;");
        toks[3].Value.Should().Be(0.5);
    }

    // ----- No-regress: dot-NOT-followed-by-digit must remain a Dot punctuator

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-MemberExpression", "13.3 Left-Hand-Side Expressions")]
    [SpecFact]
    public void Member_access_dot_not_followed_by_digit_is_Dot_punctuator()
    {
        // a.b — dot followed by identifier → Dot
        Kinds("a.b").Should().Equal(
            JsTokenKind.Identifier, JsTokenKind.Dot, JsTokenKind.Identifier,
            JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-MemberExpression", "13.3 Left-Hand-Side Expressions")]
    [SpecFact]
    public void Chained_member_access()
    {
        Kinds("a.b.c").Should().Equal(
            JsTokenKind.Identifier, JsTokenKind.Dot, JsTokenKind.Identifier,
            JsTokenKind.Dot, JsTokenKind.Identifier,
            JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-SpreadElement", "13.2.5 Array Initializer")]
    [SpecFact]
    public void Ellipsis_spread_not_affected_by_leading_dot_fix()
    {
        // [...a] — three dots → Ellipsis, not a number
        Kinds("[...a]").Should().Equal(
            JsTokenKind.LBracket, JsTokenKind.Ellipsis, JsTokenKind.Identifier,
            JsTokenKind.RBracket, JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Normal_float_1_dot_5_still_works()
    {
        var t = First("1.5");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(1.5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Normal_float_0_dot_5_still_works()
    {
        var t = First("0.5");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(0.5);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Trailing_dot_still_works()
    {
        // "3." is valid ES — a decimal literal with no fractional digits
        var t = First("3.");
        t.Kind.Should().Be(JsTokenKind.NumericLiteral);
        t.Value.Should().Be(3.0);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Dot_at_end_of_source_is_Dot_punctuator()
    {
        // "." alone (no digit follows) → Dot punctuator
        Kinds(".").Should().Equal(JsTokenKind.Dot, JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-DecimalLiteral", "12.9.3 Numeric Literals")]
    [SpecFact]
    public void Dot_followed_by_non_digit_is_Dot_punctuator()
    {
        // ".x" → Dot + Identifier (e.g. ".length" in method chaining would be Dot + ident)
        Kinds(".x").Should().Equal(JsTokenKind.Dot, JsTokenKind.Identifier, JsTokenKind.EndOfFile);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-MemberExpression", "13.3 Left-Hand-Side Expressions")]
    [SpecFact]
    public void Double_dot_member_call_1_dot_dot_toString()
    {
        // 1..toString() — integer 1 followed by "." fractional dot, then ".toString()"
        var kinds = Kinds("1..toString()");
        kinds.Should().Equal(
            JsTokenKind.NumericLiteral,
            JsTokenKind.Dot,
            JsTokenKind.Identifier,
            JsTokenKind.LParen,
            JsTokenKind.RParen,
            JsTokenKind.EndOfFile);
        // The numeric token should be "1."
        Tokens("1..toString()")[0].Value.Should().Be(1.0);
    }

    // ----- Helpers --------------------------------------------------------

    private static List<JsToken> Tokens(string s) => new JsLexer(s).Drain();

    private static List<JsTokenKind> Kinds(string s)
        => Tokens(s).Select(t => t.Kind).ToList();

    private static JsToken First(string s)
    {
        var l = new JsLexer(s);
        return l.Next();
    }

    private sealed class RecordingSink : IJsLexErrorSink
    {
        public List<(JsLexError code, JsPosition pos, string msg)> Errors { get; } = [];

        public void Report(JsLexError code, JsPosition position, string message)
            => Errors.Add((code, position, message));
    }
}
