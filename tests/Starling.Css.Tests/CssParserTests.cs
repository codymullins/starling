using FluentAssertions;
using Starling.Css.Parser;
using Starling.Css.Tokenizer;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-syntax-3", "https://www.w3.org/TR/css-syntax-3/")]

[TestClass]
public sealed class CssParserTests
{
    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
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

    [TestMethod]
    public void Tolerates_whitespace_between_property_name_and_colon()
    {
        var sheet = CssParser.ParseStyleSheet("p { width   :  10px ; }");

        var decl = sheet.Rules.OfType<StyleRule>().Single().Declarations.Should().ContainSingle().Subject;
        decl.Name.Should().Be("width");
        decl.Value.OfType<CssTokenValue>().Should()
            .ContainSingle(v => v.Token.Type == CssTokenType.Dimension && v.Token.Unit == "px");
    }

    [TestMethod]
    public void Recovers_from_malformed_declaration_and_continues()
    {
        var sheet = CssParser.ParseStyleSheet("p { ???; color: red; }");

        var rule = sheet.Rules.OfType<StyleRule>().Should().ContainSingle().Subject;
        rule.Declarations.Should().ContainSingle();
        rule.Declarations[0].Name.Should().Be("color");
    }

    [TestMethod]
    public void At_rule_with_only_prelude_and_semicolon_has_no_block()
    {
        var sheet = CssParser.ParseStyleSheet("@import url(a.css);");

        var atRule = sheet.Rules.Should().ContainSingle().Which.Should().BeOfType<AtRule>().Subject;
        atRule.Name.Should().Be("import");
        atRule.Rules.Should().BeEmpty();
        atRule.Declarations.Should().BeEmpty();
        atRule.Prelude.OfType<CssTokenValue>()
            .Should().ContainSingle(v => v.Token.Type == CssTokenType.Url);
    }

    [TestMethod]
    public void Cdo_and_cdc_are_skipped_at_top_level()
    {
        var sheet = CssParser.ParseStyleSheet("<!-- p { color: red; } -->");

        sheet.Rules.OfType<StyleRule>().Should().ContainSingle()
            .Which.Declarations.Should().ContainSingle(d => d.Name == "color");
    }

    [TestMethod]
    public void Parses_multiple_top_level_rules()
    {
        var sheet = CssParser.ParseStyleSheet("""
            a { color: red; }
            b { color: green; }
            c { color: blue; }
            """);

        sheet.Rules.Should().HaveCount(3);
        sheet.Rules.OfType<StyleRule>().Select(r => r.Declarations[0].Value.OfType<CssTokenValue>().Single().Token.Value)
            .Should().ContainInOrder("red", "green", "blue");
    }

    [TestMethod]
    public void Function_component_value_is_captured_as_CssFunction()
    {
        var sheet = CssParser.ParseStyleSheet("p { width: calc(100% - 16px); }");

        var func = sheet.Rules.OfType<StyleRule>().Single()
            .Declarations.Single().Value.OfType<CssFunction>().Should().ContainSingle().Subject;
        func.Name.Should().Be("calc");
        func.Values.OfType<CssTokenValue>().Select(v => v.Token.Type)
            .Should().ContainInOrder(CssTokenType.Percentage, CssTokenType.Delim, CssTokenType.Dimension);
    }

    [TestMethod]
    public void Custom_property_declarations_are_captured_with_dashed_name()
    {
        // --x should be tokenized as Ident("--x") and parsed as a declaration like any other.
        var sheet = CssParser.ParseStyleSheet("p { --brand: #036; color: var(--brand); }");

        var rule = sheet.Rules.OfType<StyleRule>().Single();
        rule.Declarations.Select(d => d.Name).Should().ContainInOrder("--brand", "color");
        rule.Declarations.Last().Value.OfType<CssFunction>().Should().ContainSingle(f => f.Name == "var");
    }

    [TestMethod]
    public void Unterminated_block_does_not_throw()
    {
        var act = () => CssParser.ParseStyleSheet("p { color: red");

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Important_flag_set_only_when_immediately_followed_by_important_ident()
    {
        var sheet = CssParser.ParseStyleSheet("p { a: 1 !nope; b: 2 !important }");

        var rule = sheet.Rules.OfType<StyleRule>().Single();
        rule.Declarations.Single(d => d.Name == "a").Important.Should().BeFalse();
        rule.Declarations.Single(d => d.Name == "b").Important.Should().BeTrue();
    }

    [TestMethod]
    public void Style_sheet_source_is_preserved()
    {
        const string Source = "p { color: red; }";
        var sheet = CssParser.ParseStyleSheet(Source);

        sheet.Source.Should().Be(Source);
    }
}
