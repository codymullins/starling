using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// wp:M3-22 — JS binding tests for document.implementation / DOMImplementation.
/// Covers DOM §4.5 / §4.5.1 createHTMLDocument exposed via JS.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
public sealed class DomImplementationBindingTests
{
    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void Implementation_is_accessible_on_document()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof document.implementation === 'object' && document.implementation !== null;")
            .AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void Implementation_is_stable_identity()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation === document.implementation;")
            .AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_returns_object()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof document.implementation.createHTMLDocument('') === 'object';")
            .AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_documentElement_tagName_is_HTML()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('t').documentElement.tagName;")
            .AsString.Should().Be("HTML");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_head_is_accessible()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('t').head.tagName;")
            .AsString.Should().Be("HEAD");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_body_is_accessible()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('t').body.tagName;")
            .AsString.Should().Be("BODY");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_title_reflects_arg()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('MyPage').title;")
            .AsString.Should().Be("MyPage");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_empty_string_title_yields_empty_title()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('').title;")
            .AsString.Should().Be("");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_createElement_works()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.createHTMLDocument('').createElement('div').tagName;")
            .AsString.Should().Be("DIV");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_body_fragment_parse_works()
    {
        var (runtime, _) = BuildEnv();
        // Critical jQuery path: createHTMLDocument(""), set body content, read back
        // NOTE: body's content is set via the standard DOM mutation path; child tag is P
        Eval(runtime, """
            var doc = document.implementation.createHTMLDocument("");
            var p = doc.createElement("p");
            p.textContent = "hello";
            doc.body.appendChild(p);
            result = doc.body.firstChild.tagName;
        """).AsString.Should().Be("P");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_body_innerHTML_parse_works()
    {
        var (runtime, _) = BuildEnv();
        // innerHTML write into the new document's body exercises the ParseFragment path
        // with the new document as owner — the critical integration for jQuery parseHTML
        Eval(runtime, """
            var doc = document.implementation.createHTMLDocument("");
            doc.body["innerHTML"] = "<p>hello</p>";
            result = doc.body.firstChild.tagName;
        """).AsString.Should().Be("P");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-createhtmldocument", "4.5.1")]
    public void CreateHTMLDocument_base_append_jQuery_pattern()
    {
        // Simulate the exact jQuery parseHTML / buildFragment pattern:
        // var doc = document.implementation.createHTMLDocument("");
        // var base = doc.createElement("base");
        // doc.head.appendChild(base);
        // doc.body.innerHTML = "<div>x</div>"; → doc.body.firstChild.tagName === "DIV"
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var doc = document.implementation.createHTMLDocument("");
            var base = doc.createElement("base");
            base.href = "https://example.com/";
            doc.head.appendChild(base);
            doc.body["innerHTML"] = "<div>x</div>";
            result = doc.body.firstChild.tagName;
        """).AsString.Should().Be("DIV");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-domimplementation-hasfeat", "4.5")]
    public void HasFeature_returns_true()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.implementation.hasFeature('HTML', '');")
            .AsBool.Should().BeTrue();
    }

    // ---- Helpers -------------------------------------------------------

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(head);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
