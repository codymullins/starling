using FluentAssertions;
using Xunit;

namespace Tessera.Dom.Tests;

public class DomTreeTests
{
    [Fact]
    public void AppendChild_sets_parent_and_owner_document()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        html.ParentNode.Should().BeSameAs(doc);
        html.OwnerDocument.Should().BeSameAs(doc);
    }

    [Fact]
    public void Siblings_link_up_in_insertion_order()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        doc.AppendChild(root);
        var a = doc.CreateElement("a");
        var b = doc.CreateElement("b");
        var c = doc.CreateElement("c");
        root.AppendChild(a);
        root.AppendChild(b);
        root.AppendChild(c);

        root.FirstChild.Should().BeSameAs(a);
        root.LastChild.Should().BeSameAs(c);
        a.NextSibling.Should().BeSameAs(b);
        b.NextSibling.Should().BeSameAs(c);
        c.PreviousSibling.Should().BeSameAs(b);
    }

    [Fact]
    public void TextContent_concatenates_descendant_text()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        p.AppendChild(doc.CreateTextNode("Hello, "));
        var em = doc.CreateElement("em");
        p.AppendChild(em);
        em.AppendChild(doc.CreateTextNode("world"));
        p.AppendChild(doc.CreateTextNode("."));

        p.TextContent.Should().Be("Hello, world.");
    }

    [Fact]
    public void RemoveFromParent_unlinks_and_bumps_mutation_version()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var v0 = doc.MutationVersion;
        p.RemoveFromParent();
        p.ParentNode.Should().BeNull();
        doc.FirstChild.Should().BeNull();
        doc.MutationVersion.Should().BeGreaterThan(v0);
    }

    [Fact]
    public void Body_resolves_first_body_descendant()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var head = doc.CreateElement("head");
        html.AppendChild(head);
        var body = doc.CreateElement("body");
        html.AppendChild(body);

        doc.Body.Should().BeSameAs(body);
    }
}
