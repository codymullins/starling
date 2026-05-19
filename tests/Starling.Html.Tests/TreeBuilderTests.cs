using FluentAssertions;
using Starling.Dom;
namespace Starling.Html.Tests;

/// <summary>
/// Spec-driven tree builder behavior. These exercise the insertion-mode
/// transitions and implicit element creation that the simplified
/// TokenizingHtmlParser couldn't model.
/// </summary>
[TestClass]
public sealed class TreeBuilderTests
{
    [TestMethod]
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

    [TestMethod]
    public void Doctype_html5_does_not_trigger_quirks()
    {
        var doc = HtmlParser.Parse("<!doctype html><body>x</body>");
        doc.Mode.Should().Be(QuirksMode.NoQuirks);
    }

    [TestMethod]
    public void Missing_doctype_triggers_quirks_mode()
    {
        var doc = HtmlParser.Parse("<html><body>x</body></html>");
        doc.Mode.Should().Be(QuirksMode.Quirks);
    }

    [TestMethod]
    public void Implicitly_closes_open_paragraph_when_block_starts()
    {
        var doc = HtmlParser.Parse("<p>one<p>two");
        var paragraphs = doc.Body!.Descendants().OfType<Element>()
            .Where(e => e.LocalName == "p").ToList();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].TextContent.Should().Be("one");
        paragraphs[1].TextContent.Should().Be("two");
    }

    [TestMethod]
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

    [TestMethod]
    public void List_items_implicitly_close_each_other()
    {
        var doc = HtmlParser.Parse("<ul><li>one<li>two<li>three</ul>");
        var ul = doc.Body!.Descendants().OfType<Element>().First(e => e.LocalName == "ul");
        var items = ul.ChildNodes.OfType<Element>().Where(e => e.LocalName == "li").ToList();
        items.Should().HaveCount(3);
        items.Select(li => li.TextContent.Trim()).Should().ContainInOrder("one", "two", "three");
    }

    [TestMethod]
    public void Title_text_lives_in_head_and_is_not_parsed_as_html()
    {
        var doc = HtmlParser.Parse("<title>1 < 2 & 3</title>");
        var title = doc.Head!.Descendants().OfType<Element>().First(e => e.LocalName == "title");
        title.TextContent.Should().Be("1 < 2 & 3");
    }

    [TestMethod]
    public void Style_content_is_raw_text_and_lives_in_head()
    {
        var doc = HtmlParser.Parse("<style>p { color: red; }</style><body>x</body>");
        var style = doc.Head!.Descendants().OfType<Element>().First(e => e.LocalName == "style");
        style.TextContent.Should().Contain("p { color: red; }");
        doc.Body!.TextContent.Should().Be("x");
    }

    [TestMethod]
    public void Body_attributes_merge_into_existing_body()
    {
        var doc = HtmlParser.Parse("<body class=outer><body class=inner data-x=y>x</body>");
        var body = doc.Body!;
        body.GetAttribute("class").Should().Be("outer"); // first wins; second only adds new attrs
        body.GetAttribute("data-x").Should().Be("y");
    }

    [TestMethod]
    public void Trailing_text_after_close_body_returns_to_body()
    {
        var doc = HtmlParser.Parse("<body>before</body>after");
        doc.Body!.TextContent.Should().Contain("before");
        doc.Body.TextContent.Should().Contain("after");
    }

    [TestMethod]
    public void Mismatched_end_tags_do_not_explode()
    {
        var act = () => HtmlParser.Parse("<div><span></div></span>");
        act.Should().NotThrow();
    }

    [TestMethod]
    public void Self_closing_marker_on_unknown_element_pops_immediately()
    {
        var doc = HtmlParser.Parse("<body><x-self/><p>after</p></body>");
        var children = doc.Body!.ChildNodes.OfType<Element>().Select(e => e.LocalName).ToList();
        children.Should().ContainInOrder("x-self", "p");
        // The <p> is a sibling of <x-self>, not a child.
        doc.Body.Descendants().OfType<Element>().First(e => e.LocalName == "x-self")
            .FirstChild.Should().BeNull();
    }

    [TestMethod]
    public void Stray_comment_at_top_level_attaches_to_document()
    {
        var doc = HtmlParser.Parse("<!--c1--><!doctype html><html><!--c2--><body>x</body></html><!--c3-->");
        doc.ChildNodes.OfType<Comment>().Select(c => c.Data).Should().Contain("c1");
        doc.Descendants().OfType<Comment>().Select(c => c.Data).Should().Contain(["c2", "c3"]);
    }

    [TestMethod]
    public void Text_before_html_open_is_treated_as_body_content()
    {
        var doc = HtmlParser.Parse("hello<p>world</p>");
        doc.Body.Should().NotBeNull();
        doc.Body!.TextContent.Should().Contain("hello");
        doc.Body.TextContent.Should().Contain("world");
    }

    [TestMethod]
    public void Script_content_is_raw_text_and_following_body_content_resumes()
    {
        var doc = HtmlParser.Parse("<script>if (a < b) { c(); }</script><body>after</body>");

        var script = doc.Head!.Descendants().OfType<Element>().First(e => e.LocalName == "script");
        script.TextContent.Should().Be("if (a < b) { c(); }");
        doc.Body!.TextContent.Should().Be("after");
    }

    [TestMethod]
    public void Head_content_after_head_is_reprocessed_into_head()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><head></head><style>.x{color:red}</style><body>x</body></html>");

        doc.Head!.Descendants().OfType<Element>().Should().Contain(e => e.LocalName == "style");
        doc.Body!.Descendants().OfType<Element>().Should().NotContain(e => e.LocalName == "style");
    }

    [TestMethod]
    public void Void_elements_do_not_swallow_following_text()
    {
        var doc = HtmlParser.Parse("<body>before<img src=x>after<br>tail</body>");

        doc.Body!.TextContent.Should().Be("beforeaftertail");
        doc.Body.ChildNodes.OfType<Element>().Select(e => e.LocalName)
            .Should().ContainInOrder("img", "br");
    }

    [TestMethod]
    public void Definition_items_implicitly_close_each_other()
    {
        var doc = HtmlParser.Parse("<dl><dt>term<dd>definition<dt>next</dl>");
        var dl = doc.Body!.Descendants().OfType<Element>().First(e => e.LocalName == "dl");

        dl.ChildNodes.OfType<Element>().Select(e => $"{e.LocalName}:{e.TextContent.Trim()}")
            .Should().ContainInOrder("dt:term", "dd:definition", "dt:next");
    }

    [TestMethod]
    public void Nested_button_start_tag_closes_previous_button()
    {
        var doc = HtmlParser.Parse("<body><button>one<button>two</button></body>");
        var buttons = doc.Body!.Descendants().OfType<Element>()
            .Where(e => e.LocalName == "button")
            .ToList();

        buttons.Should().HaveCount(2);
        buttons[0].TextContent.Should().Be("one");
        buttons[1].TextContent.Should().Be("two");
        buttons[1].ParentNode.Should().BeSameAs(doc.Body);
    }

    [TestMethod]
    public void Simple_table_structure_is_preserved_for_static_pages()
    {
        var doc = HtmlParser.Parse("<body><table><tbody><tr><td>A</td><td>B</td></tr></tbody></table></body>");
        var table = doc.Body!.Descendants().OfType<Element>().First(e => e.LocalName == "table");

        table.Descendants().OfType<Element>().Select(e => e.LocalName)
            .Should().ContainInOrder("tbody", "tr", "td", "td");
        table.TextContent.Should().Be("AB");
    }
}
