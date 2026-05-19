using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/")]

public sealed class FontFamilyValueParserTests
{
    [Fact]
    public void Preserves_case_of_ident_family_names()
    {
        var style = ResolveBody("body { font-family: Helvetica, Arial, sans-serif; }");
        var value = style.Get(PropertyId.FontFamily);
        value.Should().BeOfType<CssValueList>();
        var list = ((CssValueList)value!).Values;
        list.Should().HaveCount(3);
        ((CssKeyword)list[0]).Name.Should().Be("Helvetica");
        ((CssKeyword)list[1]).Name.Should().Be("Arial");
        // Generic keywords are case-normalised (lowercase) so the resolver can
        // pattern-match them directly.
        ((CssKeyword)list[2]).Name.Should().Be("sans-serif");
    }

    [Fact]
    public void Joins_multi_word_unquoted_family_name()
    {
        var style = ResolveBody("body { font-family: Open Sans, sans-serif; }");
        var list = ((CssValueList)style.Get(PropertyId.FontFamily)!).Values;
        ((CssKeyword)list[0]).Name.Should().Be("Open Sans");
    }

    [Fact]
    public void Preserves_quoted_family_name()
    {
        var style = ResolveBody("body { font-family: \"Helvetica Neue\"; }");
        ((CssString)style.Get(PropertyId.FontFamily)!).Value.Should().Be("Helvetica Neue");
    }

    private static ComputedStyle ResolveBody(string css)
    {
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css, StyleOrigin.Author));
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        return engine.Compute(body, context: null);
    }
}
