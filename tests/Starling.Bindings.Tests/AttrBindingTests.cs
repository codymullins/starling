using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// WPT-05 — JS binding tests for Attr / document.createAttribute(NS) / NamedNodeMap.
/// Covers DOM §4.9.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#interface-attr", "4.9")]
public sealed class AttrBindingTests
{
    // ---- document.createAttribute -------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_returns_object_with_correct_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('foo');
            result = a.name;
        """).AsString.Should().Be("foo");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_html_document_lowercases_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('FOO');
            result = a.name;
        """).AsString.Should().Be("foo");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_coerces_null_to_string()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute(null);
            result = a.name;
        """).AsString.Should().Be("null");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_throws_for_empty_string()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { document.createAttribute(''); } catch (e) { threw = true; }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattribute", "createAttribute")]
    public void CreateAttribute_special_chars_allowed()
    {
        // WHATWG DOM permits any non-empty string (unlike XML Name validation)
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('~!@#$%^&*');
            result = a.name;
        """).AsString.Should().Be("~!@#$%^&*");
    }

    // ---- document.createAttributeNS -----------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_sets_namespace_and_localName()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttributeNS('http://www.w3.org/1999/xlink', 'xl:href');
            result = a.namespaceURI + '|' + a.prefix + '|' + a.localName;
        """).AsString.Should().Be("http://www.w3.org/1999/xlink|xl|href");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createattributens", "createAttributeNS")]
    public void CreateAttributeNS_null_namespace_gives_null_namespaceURI()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttributeNS(null, 'local');
            result = a.namespaceURI === null;
        """).AsBool.Should().BeTrue();
    }

    // ---- Attr properties ----------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-value", "Attr.value")]
    public void Attr_value_readable_and_writable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('x');
            a.value = 'hello';
            result = a.value;
        """).AsString.Should().Be("hello");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-value", "Attr.value")]
    public void Attr_value_write_propagates_to_ownerElement()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'initial');
            var a = el.attributes.getNamedItem('id');
            a.value = 'updated';
            result = el.getAttribute('id');
        """).AsString.Should().Be("updated");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-specified", "Attr.specified")]
    public void Attr_specified_is_true()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('x');
            result = a.specified;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-ownerelement", "Attr.ownerElement")]
    public void Attr_ownerElement_null_when_detached()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('x');
            result = a.ownerElement === null;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-ownerelement", "Attr.ownerElement")]
    public void Attr_ownerElement_set_after_setAttributeNode()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var a = document.createAttribute('x');
            el.setAttributeNode(a);
            result = a.ownerElement === el;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-attr-name", "Attr.name")]
    public void Attr_inherits_from_Node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.createAttribute('x');
            result = a instanceof Node;
        """).AsBool.Should().BeTrue();
    }

    // ---- element.attributes (NamedNodeMap) -----------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.length")]
    public void Attributes_length_reflects_attr_count()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('a', '1');
            el.setAttribute('b', '2');
            result = el.attributes.length;
        """).AsNumber.Should().Be(2);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-item", "NamedNodeMap.item")]
    public void Attributes_item_returns_attr_by_index()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            var a = el.attributes.item(0);
            result = a.name + '=' + a.value;
        """).AsString.Should().Be("id=x");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-item", "NamedNodeMap.item")]
    public void Attributes_indexed_access_returns_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            var a = el.attributes[0];
            result = a.name;
        """).AsString.Should().Be("id");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem", "NamedNodeMap.getNamedItem")]
    public void Attributes_getNamedItem_finds_attr_by_name()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('class', 'foo');
            var a = el.attributes.getNamedItem('class');
            result = a.value;
        """).AsString.Should().Be("foo");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem", "NamedNodeMap.getNamedItem")]
    public void Attributes_getNamedItem_null_for_missing()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.attributes.getNamedItem('nope') === null;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-getnameditem", "NamedNodeMap.getNamedItem")]
    public void Attributes_named_property_access_finds_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'myId');
            var a = el.attributes['id'];
            result = a.value;
        """).AsString.Should().Be("myId");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem", "NamedNodeMap.setNamedItem")]
    public void Attributes_setNamedItem_inserts_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var a = document.createAttribute('data-x');
            a.value = 'hello';
            el.attributes.setNamedItem(a);
            result = el.getAttribute('data-x');
        """).AsString.Should().Be("hello");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-setnameditem", "NamedNodeMap.setNamedItem")]
    public void Attributes_setNamedItem_returns_old_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'old');
            var a = document.createAttribute('id');
            a.value = 'new';
            var old = el.attributes.setNamedItem(a);
            result = old.value;
        """).AsString.Should().Be("old");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem", "NamedNodeMap.removeNamedItem")]
    public void Attributes_removeNamedItem_removes_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            el.attributes.removeNamedItem('id');
            result = el.getAttribute('id') === null && el.attributes.length === 0;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-namednodemap-removenameditem", "NamedNodeMap.removeNamedItem")]
    public void Attributes_removeNamedItem_throws_NotFoundError_for_missing()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var errorName = '';
            try { el.attributes.removeNamedItem('nope'); } catch(e) { errorName = e.name; }
            result = errorName;
        """).AsString.Should().Be("NotFoundError");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.live")]
    public void Attributes_map_is_live()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var map = el.attributes;
            el.setAttribute('id', 'x');
            result = map.length;
        """).AsNumber.Should().Be(1);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-namednodemap", "NamedNodeMap.identity")]
    public void Attributes_map_identity_is_stable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.attributes === el.attributes;
        """).AsBool.Should().BeTrue();
    }

    // ---- getAttributeNode / setAttributeNode --------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-getattributenode", "getAttributeNode")]
    public void GetAttributeNode_returns_live_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'v');
            var a = el.getAttributeNode('id');
            result = a.value;
        """).AsString.Should().Be("v");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-getattributenode", "getAttributeNode")]
    public void GetAttributeNode_returns_null_for_missing()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.getAttributeNode('nope') === null;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-setattributenode", "setAttributeNode")]
    public void SetAttributeNode_sets_attr_on_element()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var a = document.createAttribute('id');
            a.value = 'mine';
            el.setAttributeNode(a);
            result = el.id;
        """).AsString.Should().Be("mine");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-removeattributenode", "removeAttributeNode")]
    public void RemoveAttributeNode_removes_attr()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            var a = el.getAttributeNode('id');
            el.removeAttributeNode(a);
            result = el.getAttribute('id') === null;
        """).AsBool.Should().BeTrue();
    }

    // ---- toggleAttribute / hasAttributes / getAttributeNames ----------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-toggleattribute", "toggleAttribute")]
    public void ToggleAttribute_adds_when_absent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('button');
            el.toggleAttribute('disabled');
            result = el.hasAttribute('disabled');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-toggleattribute", "toggleAttribute")]
    public void ToggleAttribute_removes_when_present()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('button');
            el.setAttribute('disabled', '');
            el.toggleAttribute('disabled');
            result = el.hasAttribute('disabled');
        """).AsBool.Should().BeFalse();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-toggleattribute", "toggleAttribute")]
    public void ToggleAttribute_force_true_adds_when_absent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('button');
            var r = el.toggleAttribute('disabled', true);
            result = r && el.hasAttribute('disabled');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-toggleattribute", "toggleAttribute")]
    public void ToggleAttribute_force_false_removes_when_present()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('button');
            el.setAttribute('disabled', '');
            var r = el.toggleAttribute('disabled', false);
            result = r === false && !el.hasAttribute('disabled');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-hasattributes", "hasAttributes")]
    public void HasAttributes_false_when_none()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.hasAttributes();
        """).AsBool.Should().BeFalse();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-hasattributes", "hasAttributes")]
    public void HasAttributes_true_when_attr_present()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            result = el.hasAttributes();
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-getattributenames", "getAttributeNames")]
    public void GetAttributeNames_returns_all_attribute_names()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('id', 'x');
            el.setAttribute('class', 'y');
            var names = el.getAttributeNames();
            result = names.length === 2 && names.includes('id') && names.includes('class');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-element-getattributenames", "getAttributeNames")]
    public void GetAttributeNames_empty_when_no_attrs()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.getAttributeNames().length === 0;
        """).AsBool.Should().BeTrue();
    }

    // ---- HierarchyRequestError for Attr as child ----------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-node-insert", "HierarchyRequestError")]
    public void AppendChild_attr_throws_HierarchyRequestError()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            var a = document.createAttribute('x');
            var threw = false;
            try { el.appendChild(a); } catch(e) { threw = e.name === 'HierarchyRequestError'; }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    // ---- Helpers ------------------------------------------------------------

    private static (JsRuntime, Document) BuildEnv(string? url = null)
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: url));
        return (runtime, doc);
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        return doc;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
