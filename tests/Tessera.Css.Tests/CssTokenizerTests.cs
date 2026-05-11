using FluentAssertions;
using Tessera.Css.Tokenizer;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class CssTokenizerTests
{
    [Fact]
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

    [Fact]
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
}
