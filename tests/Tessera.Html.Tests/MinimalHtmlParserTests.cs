using FluentAssertions;
using Tessera.Dom;
using Xunit;

namespace Tessera.Html.Tests;

public class MinimalHtmlParserTests
{
    [Fact]
    public void Parses_a_simple_doctype_html_head_body_tree()
    {
        const string Html = """
            <!doctype html>
            <html>
              <head><title>Hi</title></head>
              <body><p>Hello, world.</p></body>
            </html>
            """;

        var doc = HtmlParser.Parse(Html);
        doc.DocumentElement!.TagName.Should().Be("html");
        doc.Body!.TagName.Should().Be("body");
        doc.Body.TextContent.Trim().Should().Be("Hello, world.");
    }

    [Fact]
    public void Handles_void_elements_without_closing_tags()
    {
        var doc = HtmlParser.Parse("<body><br><p>a</p></body>");
        var body = doc.Body!;
        // br is void: should be a leaf, p should be a sibling, not a child.
        var children = new List<Node>(body.ChildNodes);
        children.OfType<Element>().Select(e => e.TagName).Should().ContainInOrder("br", "p");
        body.ChildNodes.OfType<Element>().First(e => e.TagName == "br")
            .FirstChild.Should().BeNull();
    }

    [Fact]
    public void Parses_attributes_with_quotes_and_unquoted_values()
    {
        var doc = HtmlParser.Parse("""<body><a href="https://x.test" id=main>x</a></body>""");
        var a = doc.Body!.Descendants().OfType<Element>().First(e => e.TagName == "a");
        a.GetAttribute("href").Should().Be("https://x.test");
        a.GetAttribute("id").Should().Be("main");
    }

    [Fact]
    public void Skips_comments_and_doctype()
    {
        var doc = HtmlParser.Parse("<!doctype html><!--ignore me--><body>kept</body>");
        doc.Body!.TextContent.Should().Be("kept");
        doc.Descendants().OfType<Comment>().Should().HaveCount(1);
    }
}
