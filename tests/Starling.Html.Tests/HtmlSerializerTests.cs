using AwesomeAssertions;
using Starling.Dom;
using Starling.Html.TreeBuilder;
namespace Starling.Html.Tests;

/// <summary>
/// Tests for the §13.3 fragment serializer and the §13.4 fragment parser entry
/// point on <see cref="HtmlTreeBuilder"/>.
/// </summary>
[TestClass]
public sealed class HtmlSerializerTests
{
    [TestMethod]
    public void ParseFragment_builds_real_element_children()
    {
        var doc = new Document();
        var context = doc.CreateElement("div");
        var fragment = HtmlTreeBuilder.ParseFragment("<p>hi <b>there</b></p>", context, doc);

        fragment.Should().BeOfType<DocumentFragment>();
        var p = fragment.FirstChild as Element;
        p!.LocalName.Should().Be("p");
        p.DescendantElements().Single(e => e.LocalName == "b").TextContent.Should().Be("there");
    }

    [TestMethod]
    public void SerializeChildren_round_trips_nested_markup()
    {
        var doc = new Document();
        var context = doc.CreateElement("div");
        var fragment = HtmlTreeBuilder.ParseFragment("<p>a<span>b</span></p>", context, doc);
        context.AppendChild(fragment);

        HtmlSerializer.SerializeChildren(context).Should().Be("<p>a<span>b</span></p>");
    }

    [TestMethod]
    public void SerializeNode_emits_the_element_itself()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("id", "box");
        el.AppendChild(doc.CreateElement("span"));

        HtmlSerializer.SerializeNode(el).Should().Be("<div id=\"box\"><span></span></div>");
    }

    [TestMethod]
    public void SerializeChildren_omits_closing_tag_for_void_elements()
    {
        var doc = new Document();
        var host = doc.CreateElement("div");
        host.AppendChild(doc.CreateElement("br"));
        var img = doc.CreateElement("img");
        img.SetAttribute("src", "x.png");
        host.AppendChild(img);

        HtmlSerializer.SerializeChildren(host).Should().Be("<br><img src=\"x.png\">");
    }

    [TestMethod]
    public void SerializeChildren_escapes_text_special_characters()
    {
        var doc = new Document();
        var host = doc.CreateElement("div");
        host.AppendChild(doc.CreateText("1 < 2 & 3 > 0"));

        HtmlSerializer.SerializeChildren(host).Should().Be("1 &lt; 2 &amp; 3 &gt; 0");
    }

    [TestMethod]
    public void SerializeChildren_escapes_attribute_quotes_and_ampersands()
    {
        var doc = new Document();
        var host = doc.CreateElement("div");
        var a = doc.CreateElement("a");
        a.SetAttribute("title", "a \"b\" & c");
        host.AppendChild(a);

        HtmlSerializer.SerializeChildren(host).Should().Be("<a title=\"a &quot;b&quot; &amp; c\"></a>");
    }

    [TestMethod]
    public void SerializeChildren_emits_raw_text_for_style_and_script()
    {
        var doc = new Document();
        var host = doc.CreateElement("div");
        var style = doc.CreateElement("style");
        style.AppendChild(doc.CreateText("a > b { color: red; }"));
        host.AppendChild(style);

        // §13.3: raw-text element contents are not escaped.
        HtmlSerializer.SerializeChildren(host).Should().Be("<style>a > b { color: red; }</style>");
    }
}
