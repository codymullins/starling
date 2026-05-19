using FluentAssertions;
using Starling.Css.Tokenizer;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/")]

[TestClass]
public sealed class CssTokenizerTests
{
    [TestMethod]
    public void Tokenizes_common_style_rule_tokens()
    {
        var tokens = CssTokenizer.Tokenize("""
            .card { margin: 1.5rem; color: #036; opacity: 50%; }
            """);

        tokens.Select(t => t.Type).Should().ContainInOrder(
            CssTokenType.Delim,
            CssTokenType.Ident,
            CssTokenType.LeftBrace,
            CssTokenType.Ident,
            CssTokenType.Colon,
            CssTokenType.Dimension,
            CssTokenType.Semicolon,
            CssTokenType.Ident,
            CssTokenType.Colon,
            CssTokenType.Hash,
            CssTokenType.Semicolon,
            CssTokenType.Ident,
            CssTokenType.Colon,
            CssTokenType.Percentage,
            CssTokenType.Semicolon,
            CssTokenType.RightBrace,
            CssTokenType.Eof);

        tokens.Should().Contain(t => t.Type == CssTokenType.Dimension && t.Number == 1.5 && t.Unit == "rem");
        tokens.Should().Contain(t => t.Type == CssTokenType.Percentage && t.Number == 50);
        tokens.Should().Contain(t => t.Type == CssTokenType.Hash && t.Value == "036");
    }

    [TestMethod]
    public void Skips_comments_and_tokenizes_at_keywords_strings_and_urls()
    {
        var tokens = CssTokenizer.Tokenize("""
            /* ignored */ @import url(https://example.test/a.css);
            a::before { content: "hello"; }
            """);

        tokens.Select(t => t.Type).Should().ContainInOrder(
            CssTokenType.AtKeyword,
            CssTokenType.Url,
            CssTokenType.Semicolon,
            CssTokenType.Ident,
            CssTokenType.Colon,
            CssTokenType.Colon,
            CssTokenType.Ident,
            CssTokenType.LeftBrace,
            CssTokenType.Ident,
            CssTokenType.Colon,
            CssTokenType.String,
            CssTokenType.Semicolon,
            CssTokenType.RightBrace,
            CssTokenType.Eof);

        tokens.Should().Contain(t => t.Type == CssTokenType.AtKeyword && t.Value == "import");
        tokens.Should().Contain(t => t.Type == CssTokenType.Url && t.Value == "https://example.test/a.css");
        tokens.Should().Contain(t => t.Type == CssTokenType.String && t.Value == "hello");
    }

    [TestMethod]
    public void Tokenizes_signed_and_scientific_numbers()
    {
        var tokens = CssTokenizer.Tokenize("a { x: -1.5e2; y: +.5; z: 0 }");

        tokens.Where(t => t.Type is CssTokenType.Number or CssTokenType.Dimension)
            .Select(t => t.Number)
            .Should().ContainInOrder(-150d, 0.5d, 0d);
    }

    [TestMethod]
    public void Treats_dimension_unit_as_identifier_after_number()
    {
        var tokens = CssTokenizer.Tokenize("0px 0em 0Q 0deg 0fr");

        var dimensions = tokens.Where(t => t.Type == CssTokenType.Dimension).ToList();
        dimensions.Should().HaveCount(5);
        dimensions.Select(d => d.Unit).Should().ContainInOrder("px", "em", "Q", "deg", "fr");
    }

    [TestMethod]
    public void Url_consumes_trailing_whitespace_before_close_paren()
    {
        var tokens = CssTokenizer.Tokenize("url(   https://example.test/a.css   )");

        tokens[0].Type.Should().Be(CssTokenType.Url);
        tokens[0].Value.Should().Be("https://example.test/a.css");
    }

    [TestMethod]
    public void Url_with_quoted_argument_falls_through_to_function_string()
    {
        // Per spec, a url() with a quoted string is parsed as a function call so
        // the parser can attach the resulting <string-token> as the function's arg.
        var tokens = CssTokenizer.Tokenize("url(\"https://example.test/a.css\")");

        tokens.Select(t => t.Type).Should().ContainInOrder(
            CssTokenType.Function,
            CssTokenType.String,
            CssTokenType.RightParen,
            CssTokenType.Eof);
        tokens[0].Value.Should().Be("url");
        tokens[1].Value.Should().Be("https://example.test/a.css");
    }

    [TestMethod]
    public void Url_with_embedded_whitespace_becomes_bad_url()
    {
        var tokens = CssTokenizer.Tokenize("url(a b)");

        tokens[0].Type.Should().Be(CssTokenType.BadUrl);
    }

    [TestMethod]
    public void String_unterminated_by_newline_is_bad_string()
    {
        var tokens = CssTokenizer.Tokenize("\"open\nclose\"");

        tokens[0].Type.Should().Be(CssTokenType.BadString);
        tokens[0].Value.Should().Be("open");
    }

    [TestMethod]
    public void Hex_escape_in_ident_decodes_to_codepoint()
    {
        // \41 = 'A', trailing space consumed per spec.
        var tokens = CssTokenizer.Tokenize("\\41 BC");

        tokens[0].Type.Should().Be(CssTokenType.Ident);
        tokens[0].Value.Should().Be("ABC");
    }

    [TestMethod]
    public void Backslash_newline_in_string_is_line_continuation()
    {
        var tokens = CssTokenizer.Tokenize("\"a\\\nb\"");

        tokens[0].Type.Should().Be(CssTokenType.String);
        tokens[0].Value.Should().Be("ab");
    }

    [TestMethod]
    public void Cdo_and_cdc_are_tokenized_at_top_level()
    {
        var tokens = CssTokenizer.Tokenize("<!-- p { color: red; } -->");

        tokens[0].Type.Should().Be(CssTokenType.Cdo);
        tokens.Select(t => t.Type).Should().Contain(CssTokenType.Cdc);
    }

    [TestMethod]
    public void Lone_hash_without_name_is_a_delim()
    {
        var tokens = CssTokenizer.Tokenize("# ");

        tokens[0].Type.Should().Be(CssTokenType.Delim);
        tokens[0].Delimiter.Should().Be('#');
    }

    [TestMethod]
    public void Hash_with_digit_start_still_tokenizes_as_hash()
    {
        // Tokenizer always produces Hash if the next char is a name char;
        // the type-flag distinction (id vs unrestricted) is for callers.
        var tokens = CssTokenizer.Tokenize("#123abc");

        tokens[0].Type.Should().Be(CssTokenType.Hash);
        tokens[0].Value.Should().Be("123abc");
    }

    [TestMethod]
    public void Function_token_swallows_name_and_open_paren()
    {
        var tokens = CssTokenizer.Tokenize("calc(1px + 2px)");

        tokens[0].Type.Should().Be(CssTokenType.Function);
        tokens[0].Value.Should().Be("calc");
    }

    [TestMethod]
    public void Eof_is_emitted_exactly_once_at_end()
    {
        var tokens = CssTokenizer.Tokenize(string.Empty);

        tokens.Should().ContainSingle();
        tokens[0].Type.Should().Be(CssTokenType.Eof);
    }
}
