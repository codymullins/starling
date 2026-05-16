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
        doc.Head.Should().BeSameAs(head);
    }

    [Fact]
    public void InsertBefore_ReplaceChild_and_RemoveChild_maintain_links()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        doc.AppendChild(root);
        var a = doc.CreateElement("a");
        var c = doc.CreateElement("c");
        root.AppendChild(a);
        root.AppendChild(c);
        var b = doc.CreateElement("b");

        root.InsertBefore(b, c);

        root.ChildNodes.OfType<Element>().Select(e => e.TagName)
            .Should().ContainInOrder("a", "b", "c");
        a.NextSibling.Should().BeSameAs(b);
        b.PreviousSibling.Should().BeSameAs(a);
        b.NextSibling.Should().BeSameAs(c);

        var d = doc.CreateElement("d");
        root.ReplaceChild(d, b).Should().BeSameAs(b);
        b.ParentNode.Should().BeNull();
        root.ChildNodes.OfType<Element>().Select(e => e.TagName)
            .Should().ContainInOrder("a", "d", "c");

        root.RemoveChild(d).Should().BeSameAs(d);
        d.ParentNode.Should().BeNull();
        root.ChildNodes.OfType<Element>().Select(e => e.TagName)
            .Should().ContainInOrder("a", "c");
    }

    [Fact]
    public void InsertBefore_same_node_reference_is_noop()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(root);
        root.AppendChild(child);

        root.InsertBefore(child, child).Should().BeSameAs(child);

        root.ChildNodes.Should().ContainSingle().Which.Should().BeSameAs(child);
        child.ParentNode.Should().BeSameAs(root);
        child.PreviousSibling.Should().BeNull();
        child.NextSibling.Should().BeNull();
    }

    [Fact]
    public void InsertBefore_rejects_cycles_and_external_reference_children()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        var grandchild = doc.CreateElement("em");
        doc.AppendChild(root);
        root.AppendChild(child);
        child.AppendChild(grandchild);

        var actCycle = () => grandchild.AppendChild(root);
        actCycle.Should().Throw<InvalidOperationException>();

        var outsider = doc.CreateElement("p");
        var actReference = () => root.InsertBefore(doc.CreateElement("a"), outsider);
        actReference.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OwnerDocument_propagates_to_reparented_subtrees()
    {
        var doc1 = new Document();
        var doc2 = new Document();
        var root = doc1.CreateElement("div");
        var child = doc1.CreateElement("span");
        root.AppendChild(child);
        doc1.AppendChild(root);

        doc2.AppendChild(root);

        root.OwnerDocument.Should().BeSameAs(doc2);
        child.OwnerDocument.Should().BeSameAs(doc2);
        doc1.FirstChild.Should().BeNull();
    }

    [Fact]
    public void Element_attributes_are_case_insensitive_and_mutate_document_version()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var v0 = doc.MutationVersion;

        el.SetAttribute("ID", "main");
        el.GetAttribute("id").Should().Be("main");
        el.Id.Should().Be("main");
        el.HasAttribute("id").Should().BeTrue();
        el.Attributes.Should().ContainSingle(a => a.Name == "id" && a.Value == "main");
        doc.MutationVersion.Should().BeGreaterThan(v0);

        el.Id = "other";
        el.GetAttribute("ID").Should().Be("other");
        el.RemoveAttribute("id");
        el.HasAttribute("id").Should().BeFalse();
    }

    [Fact]
    public void Document_lookup_collections_are_live()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        doc.AppendChild(root);
        var byTag = doc.GetElementsByTagName("p");
        var byClass = doc.GetElementsByClassName("selected");

        byTag.Should().BeEmpty();
        byClass.Should().BeEmpty();

        var p = doc.CreateElement("p");
        p.Id = "intro";
        p.ClassList.Add("selected");
        root.AppendChild(p);

        doc.GetElementById("intro").Should().BeSameAs(p);
        byTag.Should().ContainSingle().Which.Should().BeSameAs(p);
        byClass.Should().ContainSingle().Which.Should().BeSameAs(p);

        p.ClassList.Remove("selected").Should().BeTrue();
        byClass.Should().BeEmpty();
    }

    [Fact]
    public void DocumentFragment_inserts_children_and_is_emptied()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        doc.AppendChild(root);
        var fragment = doc.CreateDocumentFragment();
        var a = doc.CreateElement("a");
        var b = doc.CreateElement("b");
        fragment.AppendChild(a);
        fragment.AppendChild(b);

        root.AppendChild(fragment);

        fragment.FirstChild.Should().BeNull();
        root.ChildNodes.OfType<Element>().Select(e => e.TagName)
            .Should().ContainInOrder("a", "b");
        a.ParentNode.Should().BeSameAs(root);
        b.ParentNode.Should().BeSameAs(root);
    }

    [Fact]
    public void TextContent_setter_replaces_children_with_text_node()
    {
        var doc = new Document();
        var root = doc.CreateElement("div");
        root.AppendChild(doc.CreateElement("span"));
        doc.AppendChild(root);

        root.TextContent = "hello";

        root.ChildNodes.Should().ContainSingle()
            .Which.Should().BeOfType<Text>()
            .Which.Data.Should().Be("hello");
        root.TextContent.Should().Be("hello");
    }

    [Fact]
    public void CharacterData_exposes_node_value_and_mutates_document_version()
    {
        var doc = new Document();
        var text = doc.CreateText("before");
        doc.AppendChild(text);
        var v0 = doc.MutationVersion;

        text.NodeName.Should().Be("#text");
        text.NodeValue.Should().Be("before");
        text.NodeValue = "after";

        text.Data.Should().Be("after");
        text.TextContent.Should().Be("after");
        doc.MutationVersion.Should().BeGreaterThan(v0);
    }

    [Fact]
    public void Document_creates_comments_doctypes_fragments_cdata_and_processing_instructions()
    {
        var doc = new Document();
        var type = doc.CreateDocumentType("html", "", "about:legacy-compat");
        var comment = doc.CreateComment("hello");
        var cdata = doc.CreateCDataSection("raw");
        var pi = doc.CreateProcessingInstruction("xml-stylesheet", "href='x'");

        doc.AppendChild(type);
        doc.AppendChild(comment);
        doc.AppendChild(cdata);
        doc.AppendChild(pi);

        doc.DocType.Should().BeSameAs(type);
        type.NodeName.Should().Be("html");
        type.SystemId.Should().Be("about:legacy-compat");
        comment.NodeName.Should().Be("#comment");
        comment.NodeValue.Should().Be("hello");
        cdata.NodeName.Should().Be("#cdata-section");
        pi.NodeName.Should().Be("xml-stylesheet");
    }
}
