using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Dom.Tests;

/// <summary>
/// wp:M3-22 — Document.CreateHtmlDocument DOM-layer tests.
/// Covers DOM §4.5.1 createHTMLDocument skeleton structure.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
public sealed class DomImplementationTests
{
    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_has_html_documentElement()
    {
        var doc = Document.CreateHtmlDocument("test");
        doc.DocumentElement.Should().NotBeNull();
        doc.DocumentElement!.LocalName.Should().Be("html");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_has_head_and_body()
    {
        var doc = Document.CreateHtmlDocument("test");
        doc.Head.Should().NotBeNull();
        doc.Body.Should().NotBeNull();
        doc.Head!.LocalName.Should().Be("head");
        doc.Body!.LocalName.Should().Be("body");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_with_title_creates_title_element()
    {
        var doc = Document.CreateHtmlDocument("My Title");
        var titleEl = doc.Head!.FirstChild as Element;
        titleEl.Should().NotBeNull();
        titleEl!.LocalName.Should().Be("title");
        titleEl.TextContent.Should().Be("My Title");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_empty_string_title_creates_empty_title_element()
    {
        var doc = Document.CreateHtmlDocument("");
        // Even empty string arg should produce a title element (spec: only omission skips it)
        var titleEl = doc.Head!.FirstChild as Element;
        titleEl.Should().NotBeNull();
        titleEl!.LocalName.Should().Be("title");
        titleEl.TextContent.Should().Be("");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_null_title_has_no_title_element()
    {
        var doc = Document.CreateHtmlDocument(null);
        // Null means omitted — head should be empty
        doc.Head!.FirstChild.Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_has_doctype()
    {
        var doc = Document.CreateHtmlDocument("t");
        doc.DocType.Should().NotBeNull();
        doc.DocType!.Name.Should().Be("html");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_body_ownerDocument_is_new_doc()
    {
        var doc = Document.CreateHtmlDocument("t");
        doc.Body!.OwnerDocument.Should().BeSameAs(doc);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHtmlDocument_createElement_returns_element_in_new_doc()
    {
        var doc = Document.CreateHtmlDocument("");
        var el = doc.CreateElement("p");
        el.OwnerDocument.Should().BeSameAs(doc);
        el.LocalName.Should().Be("p");
    }
}
