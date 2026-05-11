using FluentAssertions;
using Tessera.Css.Parser;
using Tessera.Css.Tokenizer;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class CssParserTests
{
    [Fact]
    public void Parses_style_rule_declarations()
    {
        var sheet = CssParser.ParseStyleSheet("""
            body, .card { color: red; margin: 1rem 2px !important; }
            """);

        var rule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>().Subject;
        rule.Prelude.OfType<CssTokenValue>().Select(v => v.Token.Type)
            .Should().ContainInOrder(CssTokenType.Ident, CssTokenType.Comma, CssTokenType.Delim, CssTokenType.Ident);
        rule.Declarations.Should().HaveCount(2);
        rule.Declarations[0].Name.Should().Be("color");
        rule.Declarations[0].Value.OfType<CssTokenValue>().Should()
            .ContainSingle(v => v.Token.Type == CssTokenType.Ident && v.Token.Value == "red");
        rule.Declarations[1].Name.Should().Be("margin");
        rule.Declarations[1].Important.Should().BeTrue();
        rule.Declarations[1].Value.OfType<CssTokenValue>().Select(v => v.Token.Type)
            .Should().ContainInOrder(CssTokenType.Dimension, CssTokenType.Dimension);
    }

    [Fact]
    public void Parses_at_rules_with_nested_rules()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @media (max-width: 600px) { .card { display: block; } }
            """);

        var media = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        media.Name.Should().Be("media");
        media.Prelude.Should().ContainSingle().Which.Should().BeOfType<CssSimpleBlock>()
            .Which.StartToken.Should().Be(CssTokenType.LeftParen);
        media.Rules.Should().ContainSingle().Which.Should().BeOfType<StyleRule>()
            .Which.Declarations.Should().ContainSingle(d => d.Name == "display");
    }

    [Fact]
    public void Parses_font_face_as_declaration_block()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face { font-family: "Inter"; src: url(/inter.woff2); }
            """);

        var fontFace = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        fontFace.Name.Should().Be("font-face");
        fontFace.Rules.Should().BeEmpty();
        fontFace.Declarations.Select(d => d.Name).Should().ContainInOrder("font-family", "src");
        fontFace.Declarations[1].Value.Should().ContainSingle()
            .Which.Should().BeOfType<CssTokenValue>()
            .Which.Token.Type.Should().Be(CssTokenType.Url);
    }
}
