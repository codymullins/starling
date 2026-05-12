using FluentAssertions;
using Tessera.Dom;
using Xunit;

namespace Tessera.Html.Tests;

/// <summary>
/// Spec-driven tree builder behavior. These exercise the insertion-mode
/// transitions and implicit element creation that the simplified
/// TokenizingHtmlParser couldn't model.
/// </summary>
public sealed class TreeBuilderTests
{
    [Fact]
    public void Implicit_html_head_body_are_created_for_bare_input()
    {
        var doc = HtmlParser.Parse("<p>hi</p>");

        doc.DocumentElement!.LocalName.Should().Be("html");
        doc.Head.Should().NotBeNull();
        doc.Body.Should().NotBeNull();
        doc.Body!.LocalName.Should().Be("body");
        var p = doc.Body.Descendants().OfType<Element>().First();
        p.LocalName.Should().Be("p");
        p.TextContent.Should().Be("hi");
    }

    [Fact]
    public void Doctype_html5_does_not_trigger_quirks()
    {
        var doc = HtmlParser.Parse("<!doctype html><body>x</body>");
        doc.Mode.Should().Be(QuirksMode.NoQuirks);
    }

    [Fact]
    public void Missing_doctype_triggers_quirks_mode()
    {
        var doc = HtmlParser.Parse("<html><body>x</body></html>");
        doc.Mode.Should().Be(QuirksMode.Quirks);
    }

    [Fact]
    public void Implicitly_closes_open_paragraph_when_block_starts()
    {
        var doc = HtmlParser.Parse("<p>one<p>two");
        var paragraphs = doc.Body!.Descendants().OfType<Element>()
            .Where(e => e.LocalName == "p").ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].TextContent.Should().Be("one");
        paragraphs[1].TextContent.Should().Be("two");
    }

    [Fact]
    public void Heading_inside_heading_closes_outer()
    {
        var doc = HtmlParser.Parse("<h1>a<h2>b</h2>");
        var headings = doc.Body!.Descendants().OfType<Element>()
            .Where(e => e.LocalName is "h1" or "h2").ToList();

        headings.Should().HaveCount(2);
        // h2 should be a sibling of h1 (h1 implicitly closed), not a descendant.
        headings[0].LocalName.Should().Be("h1");
        headings[0].TextContent.Should().Be("a");
        headings[1].LocalName.Should().Be("h2");
        headings[1].TextContent.Should().Be("b");
    }

    [Fact]
    public void List_items_implicitly_close_each_other()
    {
        var doc = HtmlParser.Parse("<ul><li>one<li>two<li>three</ul>");
        var ul = doc.Body!.Descendants().OfType<Element>().First(e => e.LocalName == "ul");
        var items = ul.ChildNodes.OfType<Element>().Where(e => e.LocalName == "li").ToList();
        items.Should().HaveCount(3);
        items.Select(li => li.TextContent.Trim()).Should().ContainInOrder("one", "two", "three");
    }

    [Fact]
    public void Title_text_lives_in_head_and_is_not_parsed_as_html()
    {
        var doc = HtmlParser.Parse("<title>1 < 2 & 3</title>");
        var title = doc.Head!.Descendants().OfType<Element>().First(e => e.LocalName == "title");
        title.TextContent.Should().Be("1 < 2 & 3");
    }

    [Fact]
    public void Style_content_is_raw_text_and_lives_in_head()
    {
        var doc = HtmlParser.Parse("<style>p { color: red; }</style><body>x</body>");
        var style = doc.Head!.Descendants().OfType<Element>().First(e => e.LocalName == "style");
        style.TextContent.Should().Contain("p { color: red; }");
        doc.Body!.TextContent.Should().Be("x");
    }

    [Fact]
    public void Body_attributes_merge_into_existing_body()
    {
        var doc = HtmlParser.Parse("<body class=outer><body class=inner data-x=y>x</body>");
        var body = doc.Body!;
        body.GetAttribute("class").Should().Be("outer"); // first wins; second only adds new attrs
        body.GetAttribute("data-x").Should().Be("y");
    }

    [Fact]
    public void Trailing_text_after_close_body_returns_to_body()
    {
        var doc = HtmlParser.Parse("<body>before</body>after");
        doc.Body!.TextContent.Should().Contain("before");
        doc.Body.TextContent.Should().Contain("after");
    }

    [Fact]
    public void Mismatched_end_tags_do_not_explode()
    {
        var act = () => HtmlParser.Parse("<div><span></div></span>");
        act.Should().NotThrow();
    }

    [Fact]
    public void Self_closing_marker_on_unknown_element_pops_immediately()
    {
        var doc = HtmlParser.Parse("<body><x-self/><p>after</p></body>");
        var children = doc.Body!.ChildNodes.OfType<Element>().Select(e => e.LocalName).ToList();
        children.Should().ContainInOrder("x-self", "p");
        // The <p> is a sibling of <x-self>, not a child.
        doc.Body.Descendants().OfType<Element>().First(e => e.LocalName == "x-self")
            .FirstChild.Should().BeNull();
    }

    [Fact]
    public void Stray_comment_at_top_level_attaches_to_document()
    {
        var doc = HtmlParser.Parse("<!--c1--><!doctype html><html><!--c2--><body>x</body></html><!--c3-->");
        doc.ChildNodes.OfType<Comment>().Select(c => c.Data).Should().Contain("c1");
        doc.Descendants().OfType<Comment>().Select(c => c.Data).Should().Contain(["c2", "c3"]);
    }

    [Fact]
    public void Text_before_html_open_is_treated_as_body_content()
    {
        var doc = HtmlParser.Parse("hello<p>world</p>");
        doc.Body.Should().NotBeNull();
        doc.Body!.TextContent.Should().Contain("hello");
        doc.Body.TextContent.Should().Contain("world");
    }
}
