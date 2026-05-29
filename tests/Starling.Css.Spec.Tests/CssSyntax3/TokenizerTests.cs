using AwesomeAssertions;
using Starling.Css.Tokenizer;

namespace Starling.Css.Spec.Tests.CssSyntax3;

/// <summary>
/// Tokenizer conformance for
/// <see href="https://www.w3.org/TR/css-syntax-3/#tokenizing-and-parsing">CSS Syntax Level 3 §4</see>.
/// </summary>
[TestClass]
[Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/", section: "4")]
public sealed class TokenizerTests
{
    private static IReadOnlyList<CssToken> Tok(string source) => CssTokenizer.Tokenize(source);

    // ─── §4.3.1 consume-a-token: EOF ──────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Eof_emitted_for_empty_input()
    {
        var tokens = Tok("");
        tokens.Should().ContainSingle();
        tokens[0].Type.Should().Be(CssTokenType.Eof);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Eof_is_the_last_token_always()
    {
        var tokens = Tok("a");
        tokens[^1].Type.Should().Be(CssTokenType.Eof);
    }

    // ─── §4.3.1 whitespace ────────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#whitespace-token-diagram", section: "4.3.1")]
    [SpecFact]
    public void Whitespace_token_collapses_consecutive_whitespace()
    {
        // spec: one or more whitespace chars become a single whitespace token
        var tokens = Tok("   \t\n  ");
        tokens[0].Type.Should().Be(CssTokenType.Whitespace);
        tokens[1].Type.Should().Be(CssTokenType.Eof);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#whitespace-token-diagram", section: "4.3.1")]
    [SpecFact]
    public void Whitespace_between_tokens_is_a_single_token()
    {
        var tokens = Tok("a   b");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[1].Type.Should().Be(CssTokenType.Whitespace);
        tokens[2].Type.Should().Be(CssTokenType.Ident);
    }

    // ─── §4.3.11 comments are not tokens ──────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-comment", section: "4.3.11")]
    [SpecFact]
    public void Comment_is_consumed_and_not_emitted_as_a_token()
    {
        var tokens = Tok("/* ignored */");
        tokens.Should().ContainSingle();
        tokens[0].Type.Should().Be(CssTokenType.Eof);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-comment", section: "4.3.11")]
    [SpecFact]
    public void Comment_between_tokens_is_invisible_to_consumer()
    {
        var tokens = Tok("a/* comment */b");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("a");
        tokens[1].Type.Should().Be(CssTokenType.Ident);
        tokens[1].Value.Should().Be("b");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-comment", section: "4.3.11")]
    [SpecFact]
    public void Unterminated_comment_consumes_to_end_of_input()
    {
        // spec: unterminated comment is a parse error; treated as consuming all remaining
        // Input: "a/* no end" — ident 'a' immediately before the comment (no whitespace)
        var tokens = Tok("a/* no end");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        // The comment and everything after it are silently consumed
        tokens[1].Type.Should().Be(CssTokenType.Eof);
    }

    // ─── §4.3.3 ident-token ───────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_simple_ascii_letters()
    {
        var tokens = Tok("color");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("color");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_starting_with_underscore()
    {
        var tokens = Tok("_foo");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("_foo");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_starting_with_hyphen_followed_by_name_start()
    {
        // §4.3.3: ident can start with - if followed by ident-start code point
        var tokens = Tok("-foo");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("-foo");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_double_hyphen_is_custom_property_name()
    {
        // §4.3.3: ident can start with -- (used for custom properties)
        var tokens = Tok("--brand");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("--brand");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_with_digits_in_non_start_position()
    {
        var tokens = Tok("h1");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("h1");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#ident-token-diagram", section: "4.3.3")]
    [SpecFact]
    public void Ident_with_non_ascii_codepoint()
    {
        // §4.3.3: code points >= 0x80 are name-start code points
        var tokens = Tok("café");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("café");
    }

    // ─── §4.3.4 function-token ────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#function-token-diagram", section: "4.3.4")]
    [SpecFact]
    public void Function_token_captures_name_and_consumes_open_paren()
    {
        var tokens = Tok("calc(");
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("calc");
        // The open paren is consumed — not emitted separately
        tokens[1].Type.Should().Be(CssTokenType.Eof);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#function-token-diagram", section: "4.3.4")]
    [SpecFact]
    public void Function_token_case_preserved()
    {
        var tokens = Tok("RGB(");
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("RGB");
    }

    // ─── §4.3.5 at-keyword-token ──────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#at-keyword-token-diagram", section: "4.3.5")]
    [SpecFact]
    public void AtKeyword_consumes_at_sign_and_ident()
    {
        var tokens = Tok("@media");
        tokens[0].Type.Should().Be(CssTokenType.AtKeyword);
        tokens[0].Value.Should().Be("media");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#at-keyword-token-diagram", section: "4.3.5")]
    [SpecFact]
    public void At_sign_not_followed_by_ident_is_delim()
    {
        // §4.3.1: if @ is not followed by an ident, emit Delim('@')
        var tokens = Tok("@ ");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('@');
    }

    // ─── §4.3.6 hash-token ────────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#hash-token-diagram", section: "4.3.6")]
    [SpecFact]
    public void Hash_with_ident_start_char_produces_hash_token()
    {
        var tokens = Tok("#main");
        tokens[0].Type.Should().Be(CssTokenType.Hash);
        tokens[0].Value.Should().Be("main");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#hash-token-diagram", section: "4.3.6")]
    [SpecFact]
    public void Hash_with_digit_first_char_is_unrestricted_hash_token()
    {
        // §4.3.6: hash is produced even when value doesn't start with ident-start;
        // the "id type" flag is set only when first char IS ident-start. The tokenizer
        // still produces Hash (not Delim) when first char is a digit/hyphen name char.
        var tokens = Tok("#123abc");
        tokens[0].Type.Should().Be(CssTokenType.Hash);
        tokens[0].Value.Should().Be("123abc");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#hash-token-diagram", section: "4.3.6")]
    [SpecFact]
    public void Hash_not_followed_by_name_char_is_delim()
    {
        // §4.3.1: # not followed by a name code point is Delim('#')
        var tokens = Tok("# ");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('#');
    }

    // ─── §4.3.7 string-token ──────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#string-token-diagram", section: "4.3.7")]
    [SpecFact]
    public void String_double_quoted()
    {
        var tokens = Tok("\"hello world\"");
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("hello world");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#string-token-diagram", section: "4.3.7")]
    [SpecFact]
    public void String_single_quoted()
    {
        var tokens = Tok("'hello'");
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("hello");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#string-token-diagram", section: "4.3.7")]
    [SpecFact]
    public void String_empty()
    {
        var tokens = Tok("\"\"");
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#string-token-diagram", section: "4.3.7")]
    [SpecFact]
    public void String_with_backslash_line_continuation_yields_joined_string()
    {
        // §4.3.7: \<newline> in a string is a line continuation (char removed)
        var tokens = Tok("\"a\\\nb\"");
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("ab");
    }

    // ─── §4.3.8 bad-string-token ──────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#bad-string-token-diagram", section: "4.3.8")]
    [SpecFact]
    public void BadString_when_newline_interrupts_string()
    {
        // §4.3.7: an unescaped newline inside a string yields bad-string
        var tokens = Tok("\"open\nclose\"");
        tokens[0].Type.Should().Be(CssTokenType.BadString);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#bad-string-token-diagram", section: "4.3.8")]
    [SpecFact]
    public void BadString_partial_value_preserved_before_newline()
    {
        var tokens = Tok("\"hello\nworld\"");
        tokens[0].Type.Should().Be(CssTokenType.BadString);
        tokens[0].Value.Should().Be("hello");
    }

    // ─── §4.3.9 url-token ─────────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_unquoted_bare_value()
    {
        var tokens = Tok("url(https://example.test/a.css)");
        tokens[0].Type.Should().Be(CssTokenType.Url);
        tokens[0].Value.Should().Be("https://example.test/a.css");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_with_surrounding_whitespace_trims_whitespace()
    {
        var tokens = Tok("url(   https://example.test/a.css   )");
        tokens[0].Type.Should().Be(CssTokenType.Url);
        tokens[0].Value.Should().Be("https://example.test/a.css");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_with_double_quoted_argument_falls_through_to_function_token()
    {
        // §4.3.9: if url( is immediately followed by a quote, emit Function("url")
        // and let the parser handle the quoted string
        var tokens = Tok("url(\"https://example.test/a.css\")");
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("url");
        tokens[1].Type.Should().Be(CssTokenType.String);
        tokens[1].Value.Should().Be("https://example.test/a.css");
        tokens[2].Type.Should().Be(CssTokenType.RightParen);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_with_single_quoted_argument_falls_through_to_function_token()
    {
        var tokens = Tok("url('a.css')");
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("url");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_with_whitespace_before_quote_falls_through_to_function_token()
    {
        // §4.3.9: whitespace is consumed, then if next is a quote → Function token
        var tokens = Tok("url(   \"a.css\")");
        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("url");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#url-token-diagram", section: "4.3.9")]
    [SpecFact]
    public void Url_uppercase_also_produces_url_token()
    {
        var tokens = Tok("URL(https://example.test/)");
        tokens[0].Type.Should().Be(CssTokenType.Url);
        tokens[0].Value.Should().Be("https://example.test/");
    }

    // ─── §4.3.10 bad-url-token ────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#bad-url-token-diagram", section: "4.3.10")]
    [SpecFact]
    public void BadUrl_when_embedded_whitespace_splits_value()
    {
        var tokens = Tok("url(a b)");
        tokens[0].Type.Should().Be(CssTokenType.BadUrl);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#bad-url-token-diagram", section: "4.3.10")]
    [SpecFact]
    public void BadUrl_when_unescaped_open_paren_inside_url()
    {
        // §4.3.10: ( inside unquoted url is non-printable-like bad char
        var tokens = Tok("url(bad(url)");
        tokens[0].Type.Should().Be(CssTokenType.BadUrl);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#bad-url-token-diagram", section: "4.3.10")]
    [SpecFact]
    public void BadUrl_when_quote_char_appears_inside_unquoted_url()
    {
        // §4.3.10: a quote inside unquoted url is a bad-url
        var tokens = Tok("url(ba\"d)");
        tokens[0].Type.Should().Be(CssTokenType.BadUrl);
    }

    // ─── §4.3.2 delim-token ───────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#delim-token-diagram", section: "4.3.2")]
    [SpecFact]
    public void Delim_period()
    {
        var tokens = Tok(".");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('.');
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#delim-token-diagram", section: "4.3.2")]
    [SpecFact]
    public void Delim_ampersand()
    {
        var tokens = Tok("&");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('&');
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#delim-token-diagram", section: "4.3.2")]
    [SpecFact]
    public void Delim_exclamation_mark()
    {
        var tokens = Tok("!");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('!');
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#delim-token-diagram", section: "4.3.2")]
    [SpecFact]
    public void Delim_plus_not_followed_by_digit_is_delim()
    {
        var tokens = Tok("+ ");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('+');
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#delim-token-diagram", section: "4.3.2")]
    [SpecFact]
    public void Delim_hyphen_alone_is_delim()
    {
        // - not followed by ident-start, -, or digit is a Delim
        var tokens = Tok("- ");
        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('-');
    }

    // ─── §4.3.12 number-token ─────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_integer()
    {
        var tokens = Tok("42");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(42d);
        tokens[0].IsInteger.Should().BeTrue();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_float_with_decimal_point()
    {
        var tokens = Tok("3.14");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().BeApproximately(3.14, 1e-9);
        tokens[0].IsInteger.Should().BeFalse();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_decimal_without_leading_zero()
    {
        var tokens = Tok(".5");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().BeApproximately(0.5, 1e-9);
        tokens[0].IsInteger.Should().BeFalse();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_positive_signed_integer()
    {
        var tokens = Tok("+42");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(42d);
        tokens[0].HasSign.Should().BeTrue();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_negative_signed_integer()
    {
        var tokens = Tok("-7");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(-7d);
        tokens[0].HasSign.Should().BeTrue();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_scientific_notation_positive_exponent()
    {
        var tokens = Tok("1e2");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(100d);
        tokens[0].IsInteger.Should().BeFalse();
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_scientific_notation_with_sign()
    {
        var tokens = Tok("-1.5e2");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(-150d);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#number-token-diagram", section: "4.3.12")]
    [SpecFact]
    public void Number_scientific_notation_negative_exponent()
    {
        var tokens = Tok("1e-2");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().BeApproximately(0.01, 1e-10);
        tokens[0].IsInteger.Should().BeFalse();
    }

    // ─── §4.3.13 percentage-token ─────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#percentage-token-diagram", section: "4.3.13")]
    [SpecFact]
    public void Percentage_integer()
    {
        var tokens = Tok("50%");
        tokens[0].Type.Should().Be(CssTokenType.Percentage);
        tokens[0].Number.Should().Be(50d);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#percentage-token-diagram", section: "4.3.13")]
    [SpecFact]
    public void Percentage_float()
    {
        var tokens = Tok("12.5%");
        tokens[0].Type.Should().Be(CssTokenType.Percentage);
        tokens[0].Number.Should().BeApproximately(12.5, 1e-9);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#percentage-token-diagram", section: "4.3.13")]
    [SpecFact]
    public void Percentage_zero()
    {
        var tokens = Tok("0%");
        tokens[0].Type.Should().Be(CssTokenType.Percentage);
        tokens[0].Number.Should().Be(0d);
    }

    // ─── §4.3.14 dimension-token ──────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_px()
    {
        var tokens = Tok("16px");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().Be(16d);
        tokens[0].Unit.Should().Be("px");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_rem()
    {
        var tokens = Tok("1.5rem");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().BeApproximately(1.5, 1e-9);
        tokens[0].Unit.Should().Be("rem");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_deg_angle_unit()
    {
        var tokens = Tok("90deg");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().Be(90d);
        tokens[0].Unit.Should().Be("deg");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_fr_grid_unit()
    {
        var tokens = Tok("1fr");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Unit.Should().Be("fr");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_ms_time_unit()
    {
        var tokens = Tok("200ms");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().Be(200d);
        tokens[0].Unit.Should().Be("ms");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_zero_without_unit_is_number_not_dimension()
    {
        var tokens = Tok("0");
        tokens[0].Type.Should().Be(CssTokenType.Number);
        tokens[0].Number.Should().Be(0d);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#dimension-token-diagram", section: "4.3.14")]
    [SpecFact]
    public void Dimension_negative_value()
    {
        var tokens = Tok("-10px");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().Be(-10d);
        tokens[0].Unit.Should().Be("px");
    }

    // ─── §4.3.15 CDO / CDC ────────────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#CDO-token-diagram", section: "4.3.15")]
    [SpecFact]
    public void Cdo_four_char_sequence()
    {
        var tokens = Tok("<!--");
        tokens[0].Type.Should().Be(CssTokenType.Cdo);
        tokens[1].Type.Should().Be(CssTokenType.Eof);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#CDC-token-diagram", section: "4.3.15")]
    [SpecFact]
    public void Cdc_three_char_sequence()
    {
        var tokens = Tok("-->");
        tokens[0].Type.Should().Be(CssTokenType.Cdc);
        tokens[1].Type.Should().Be(CssTokenType.Eof);
    }

    // ─── §4.3.1 bracket and punctuation tokens ────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Colon_token()
    {
        var tokens = Tok(":");
        tokens[0].Type.Should().Be(CssTokenType.Colon);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Semicolon_token()
    {
        var tokens = Tok(";");
        tokens[0].Type.Should().Be(CssTokenType.Semicolon);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Comma_token()
    {
        var tokens = Tok(",");
        tokens[0].Type.Should().Be(CssTokenType.Comma);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Left_square_bracket_token()
    {
        var tokens = Tok("[");
        tokens[0].Type.Should().Be(CssTokenType.LeftSquare);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Right_square_bracket_token()
    {
        var tokens = Tok("]");
        tokens[0].Type.Should().Be(CssTokenType.RightSquare);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Left_paren_token()
    {
        var tokens = Tok("(");
        tokens[0].Type.Should().Be(CssTokenType.LeftParen);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Right_paren_token()
    {
        var tokens = Tok(")");
        tokens[0].Type.Should().Be(CssTokenType.RightParen);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Left_brace_token()
    {
        var tokens = Tok("{");
        tokens[0].Type.Should().Be(CssTokenType.LeftBrace);
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-token", section: "4.3.1")]
    [SpecFact]
    public void Right_brace_token()
    {
        var tokens = Tok("}");
        tokens[0].Type.Should().Be(CssTokenType.RightBrace);
    }

    // ─── §4.3.7 escape sequences ──────────────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_hex_with_trailing_space_consumed()
    {
        // §4.3.7: \41 ' ' (space is consumed) → 'A'
        var tokens = Tok("\\41 BC");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("ABC");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_literal_char_in_ident()
    {
        // \. is a literal dot in an ident
        var tokens = Tok("foo\\.bar");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("foo.bar");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_hex_in_string()
    {
        // \22 = U+0022 = double quote; inside single-quoted string
        var tokens = Tok("'a\\22 b'");
        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("a\"b");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_null_codepoint_replaced_with_replacement_char()
    {
        // §4.3.7: escape value 0 → U+FFFD replacement character
        var tokens = Tok("\\0 x");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("�x");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_surrogate_codepoint_replaced_with_replacement_char()
    {
        // §4.3.7: surrogate code points → U+FFFD
        var tokens = Tok("\\D800 ");
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("�");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#consume-escaped-code-point", section: "4.3.7")]
    [SpecFact]
    public void Escape_6_hex_digits_max()
    {
        // §4.3.7: at most 6 hex digits consumed after backslash.
        // \0000041 — 7 hex digits after \; only first 6 (000004) are consumed → U+0004 (control, → replacement).
        // Then '1' is a bare name char appended to the ident.
        // But the impl replaces U+0004 with U+FFFD per the non-printable rule.
        // We verify the 7th digit is NOT swallowed into the escape.
        var tokens = Tok("\\0000041x"); // \000004 = U+0004 → replacement char '1' 'x' as name chars
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        // The trailing "1x" characters (digits 7+ and 'x') are still part of the ident name
        tokens[0].Value.Should().EndWith("1x");
    }

    // ─── §4.3.3 tricky ident-start cases ─────────────────────────────────────

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#would-start-an-identifier", section: "4.3.3")]
    [SpecFact]
    public void Backslash_escape_starts_an_ident()
    {
        // §4.3.3: \<valid-escape> is an ident-start
        var tokens = Tok("\\61 bc"); // \61 = 'a', bc follows as name chars
        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("abc");
    }

    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#would-start-an-identifier", section: "4.3.3")]
    [SpecFact]
    public void Hyphen_followed_by_digit_is_NOT_ident_start_but_number_start()
    {
        // -3 would start a number, not an ident
        var tokens = Tok("-3px");
        tokens[0].Type.Should().Be(CssTokenType.Dimension);
        tokens[0].Number.Should().Be(-3d);
        tokens[0].Unit.Should().Be("px");
    }

    // ─── §4.3.6 Hash type-flag (id vs unrestricted) ───────────────────────────

    /// <summary>
    /// CSS Syntax 3 §4.3.6 specifies that a hash token carries a "type flag" that
    /// is "id" when the token's value would be a valid CSS identifier, and
    /// "unrestricted" otherwise. The current <see cref="CssToken"/> record has no
    /// field for this flag; consumers cannot distinguish <c>#abc</c> (id type) from
    /// <c>#1abc</c> (unrestricted type) without re-examining the value. The CSS
    /// Selectors parser and serializer both rely on this distinction.
    /// </summary>
    [Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/#hash-token-diagram", section: "4.3.6")]
    [SpecFact]
    public void Hash_id_type_flag_set_for_ident_start_value()
    {
        // §4.3.6: '#' followed by a name-start code point → type flag = "id";
        // '#' followed by a digit → type flag = "unrestricted".
        var idHash = Tok("#main")[0];
        var unrestrictedHash = Tok("#1abc")[0];

        idHash.Type.Should().Be(CssTokenType.Hash);
        unrestrictedHash.Type.Should().Be(CssTokenType.Hash);

        idHash.HashIsId.Should().BeTrue();
        unrestrictedHash.HashIsId.Should().BeFalse();
    }
}
