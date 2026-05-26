using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Dom.Tests;

/// <summary>
/// WPT-05 — DOM §4.9 Attr as a Node + Document.createAttribute(NS) + NamedNodeMap.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#interface-attr", "4.9")]
public sealed class AttrNodeTests
{
    // ---- Document.createAttribute -------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_returns_AttrNode_with_correct_name()
    {
        var doc = new Document();
        var attr = doc.CreateAttribute("foo");
        attr.Name.Should().Be("foo");
        attr.LocalName.Should().Be("foo");
        attr.Prefix.Should().BeNull();
        attr.Namespace.Should().BeNull();
        attr.Value.Should().Be("");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_colons_are_part_of_localName_not_prefix()
    {
        // createAttribute (non-NS path): colon is NOT a namespace separator
        var doc = new Document();
        var attr = doc.CreateAttribute("a:b");
        attr.Name.Should().Be("a:b");
        attr.LocalName.Should().Be("a:b");
        attr.Prefix.Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_throws_for_empty_name()
    {
        var doc = new Document();
        Action act = () => doc.CreateAttribute("");
        act.Should().Throw<ArgumentException>();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_sets_ownerDocument()
    {
        var doc = new Document();
        var attr = doc.CreateAttribute("x");
        attr.OwnerDocument.Should().BeSameAs(doc);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_ownerElement_is_null_when_detached()
    {
        var doc = new Document();
        var attr = doc.CreateAttribute("x");
        attr.OwnerElement.Should().BeNull();
    }

    // ---- Document.createAttributeNS -----------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_sets_namespace_localName_prefix()
    {
        var doc = new Document();
        var attr = doc.CreateAttributeNS("http://example.com/ns", "ns:local");
        attr.Name.Should().Be("ns:local");
        attr.LocalName.Should().Be("local");
        attr.Prefix.Should().Be("ns");
        attr.Namespace.Should().Be("http://example.com/ns");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_null_namespace_gives_null_namespace()
    {
        var doc = new Document();
        var attr = doc.CreateAttributeNS(null, "local");
        attr.LocalName.Should().Be("local");
        attr.Namespace.Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_empty_string_namespace_treated_as_null()
    {
        var doc = new Document();
        var attr = doc.CreateAttributeNS("", "local");
        attr.Namespace.Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_throws_for_empty_qualifiedName()
    {
        var doc = new Document();
        Action act = () => doc.CreateAttributeNS("http://example.com/ns", "");
        act.Should().Throw<ArgumentException>();
    }

    // ---- Attr.value + AttrNode.Clone ----------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-value", "Attr.value")]
    public void AttrNode_value_can_be_set()
    {
        var attr = new AttrNode("data-x");
        attr.Value = "hello";
        attr.Value.Should().Be("hello");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-value", "Attr.value")]
    public void AttrNode_value_set_propagates_to_ownerElement()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("data-x", "initial");

        // Grab the live AttrNode from the NamedNodeMap
        var attr = el.Attributes.GetNamedItem("data-x");
        attr.Should().NotBeNull();
        attr!.OwnerElement.Should().BeSameAs(el);

        // Writing the Attr.value should update the element's attribute storage
        attr.Value = "updated";
        el.GetAttribute("data-x").Should().Be("updated");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-value", "Attr.value")]
    public void AttrNode_clone_is_independent_of_source()
    {
        var attr = new AttrNode("id", "original");
        var clone = attr.Clone();
        clone.Name.Should().Be("id");
        clone.Value.Should().Be("original");
        clone.OwnerElement.Should().BeNull();

        // Mutating original doesn't affect clone
        attr.Value = "changed";
        clone.Value.Should().Be("original");
    }

    // ---- NamedNodeMap live behaviour ----------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.length")]
    public void NamedNodeMap_length_reflects_attribute_count()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.Attributes.Count.Should().Be(0);

        el.SetAttribute("id", "x");
        el.Attributes.Count.Should().Be(1);

        el.SetAttribute("class", "y");
        el.Attributes.Count.Should().Be(2);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-item", "NamedNodeMap.item")]
    public void NamedNodeMap_item_returns_attr_by_index()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("id", "x");
        var item = el.Attributes[0];
        item.Name.Should().Be("id");
        item.Value.Should().Be("x");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-item", "NamedNodeMap.item")]
    public void NamedNodeMap_item_out_of_range_throws()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        Action act = () => _ = el.Attributes[0];
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem", "NamedNodeMap.getNamedItem")]
    public void NamedNodeMap_getNamedItem_finds_attr_by_name()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("class", "foo");
        var attr = el.Attributes.GetNamedItem("class");
        attr.Should().NotBeNull();
        attr!.Value.Should().Be("foo");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem", "NamedNodeMap.getNamedItem")]
    public void NamedNodeMap_getNamedItem_returns_null_for_missing()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.Attributes.GetNamedItem("missing").Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem", "NamedNodeMap.setNamedItem")]
    public void NamedNodeMap_setNamedItem_inserts_and_returns_old()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("id", "old");

        var newAttr = doc.CreateAttribute("id");
        newAttr.Value = "new";

        var old = el.Attributes.SetNamedItem(newAttr);
        old.Should().NotBeNull();
        old!.Value.Should().Be("old");
        el.GetAttribute("id").Should().Be("new");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem", "NamedNodeMap.setNamedItem")]
    public void NamedNodeMap_setNamedItem_sets_ownerElement()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        var attr = doc.CreateAttribute("data-x");
        attr.OwnerElement.Should().BeNull();

        el.Attributes.SetNamedItem(attr);
        attr.OwnerElement.Should().BeSameAs(el);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem", "NamedNodeMap.removeNamedItem")]
    public void NamedNodeMap_removeNamedItem_removes_and_clears_ownerElement()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("id", "x");

        var attr = el.Attributes.GetNamedItem("id");
        attr.Should().NotBeNull();

        var removed = el.Attributes.RemoveNamedItem("id");
        removed.Should().NotBeNull();
        removed!.Name.Should().Be("id");
        removed.OwnerElement.Should().BeNull();
        el.GetAttribute("id").Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem", "NamedNodeMap.removeNamedItem")]
    public void NamedNodeMap_removeNamedItem_returns_null_for_missing()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        // DOM layer returns null; the JS binding layer wraps this as NotFoundError.
        var removed = el.Attributes.RemoveNamedItem("nope");
        removed.Should().BeNull();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.live")]
    public void NamedNodeMap_reflects_setAttribute_changes_live()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        var map = el.Attributes;  // capture reference

        el.SetAttribute("id", "a");
        map.Count.Should().Be(1);
        map.GetNamedItem("id")!.Value.Should().Be("a");

        el.SetAttribute("id", "b");
        map.GetNamedItem("id")!.Value.Should().Be("b");

        el.RemoveAttribute("id");
        map.Count.Should().Be(0);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.identity")]
    public void NamedNodeMap_setAttribute_preserves_AttrNode_identity()
    {
        // Calling setAttribute on an already-present attribute mutates the existing
        // AttrNode in-place rather than replacing it, so existing references stay valid.
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("id", "first");

        var ref1 = el.Attributes.GetNamedItem("id");
        el.SetAttribute("id", "second");
        var ref2 = el.Attributes.GetNamedItem("id");

        ref1.Should().BeSameAs(ref2);
        ref1!.Value.Should().Be("second");
    }

    // ---- NamedNodeMap.getNamedItemNS / removeNamedItemNS --------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditemns", "NamedNodeMap.getNamedItemNS")]
    public void NamedNodeMap_getNamedItemNS_finds_ns_attr()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttributeNS("http://example.com/ns", "ns:local", "val");

        var attr = el.Attributes.GetNamedItemNS("http://example.com/ns", "local");
        attr.Should().NotBeNull();
        attr!.Value.Should().Be("val");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditemns", "NamedNodeMap.removeNamedItemNS")]
    public void NamedNodeMap_removeNamedItemNS_removes_ns_attr()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttributeNS("http://example.com/ns", "ns:local", "val");

        var removed = el.Attributes.RemoveNamedItemNS("http://example.com/ns", "local");
        removed.Should().NotBeNull();
        removed!.LocalName.Should().Be("local");
        el.Attributes.GetNamedItemNS("http://example.com/ns", "local").Should().BeNull();
    }

    // ---- Attr.specified -----------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-specified", "Attr.specified")]
    public void AttrNode_specified_is_always_true()
    {
        var attr = new AttrNode("x");
        attr.Specified.Should().BeTrue();
    }

    // ---- Attr.NodeName / NodeValue / TextContent ----------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-attr-name", "Attr.nodeName")]
    public void AttrNode_NodeName_equals_Name()
    {
        var attr = new AttrNode("data-foo");
        attr.NodeName.Should().Be("data-foo");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-node-nodevalue", "Attr.nodeValue")]
    public void AttrNode_NodeValue_round_trips_value()
    {
        var attr = new AttrNode("x");
        attr.NodeValue = "test";
        attr.NodeValue.Should().Be("test");
        attr.Value.Should().Be("test");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-node-textcontent", "Attr.textContent")]
    public void AttrNode_TextContent_round_trips_value()
    {
        var attr = new AttrNode("x");
        attr.TextContent = "content";
        attr.TextContent.Should().Be("content");
        attr.Value.Should().Be("content");
    }
}
