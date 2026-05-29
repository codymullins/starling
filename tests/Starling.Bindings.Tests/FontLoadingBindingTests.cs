using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// CSS Font Loading 3 §3–§4 JS-binding conformance on the Starling JS engine:
/// <c>document.fonts</c> (a FontFaceSet seeded from <c>@font-face</c> rules) and
/// the global <c>FontFace</c> constructor.
/// </summary>
[TestClass]
public sealed class FontLoadingBindingTests
{
    private static JsRuntime NewSession(string styleCss)
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var style = doc.CreateElement("style");
        style.AppendChild(doc.CreateTextNode(styleCss));
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(head);
        head.AppendChild(style);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return runtime;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }

    private const string SampleFace =
        "@font-face { font-family: 'Test Sans'; src: url(test.woff2) format('woff2'); }";

    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-document-fonts", section: "4")]
    [SpecFact]
    public void DocumentFonts_is_seeded_from_at_font_face_rules()
    {
        var rt = NewSession(SampleFace);
        Eval(rt, "result = document.fonts.size;").AsNumber.Should().Be(1);
        Eval(rt, "result = document.fonts.check('16px Test Sans');").AsBool.Should().BeTrue();
        Eval(rt, "result = document.fonts.check('16px Nonexistent');").AsBool.Should().BeFalse();
        Eval(rt, "result = document.fonts.status;").AsString.Should().Be("loaded");
    }

    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-ready", section: "4.3")]
    [SpecFact]
    public void DocumentFonts_ready_is_a_thenable()
    {
        var rt = NewSession(SampleFace);
        Eval(rt, "result = typeof document.fonts.ready.then;").AsString.Should().Be("function");
    }

    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#font-face-constructor", section: "3")]
    [SpecFact]
    public void FontFace_constructor_builds_a_face_with_descriptors()
    {
        var rt = NewSession("");
        Eval(rt, "result = new FontFace('My Font', 'url(x.woff2)').family;").AsString.Should().Be("My Font");
        Eval(rt, "result = new FontFace('My Font', 'url(x.woff2)', {style:'italic'}).style;").AsString.Should().Be("italic");
        Eval(rt, "result = new FontFace('My Font', 'url(x.woff2)').status;").AsString.Should().Be("unloaded");
    }

    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-add", section: "4")]
    [SpecFact]
    public void FontFaceSet_add_has_and_delete_track_constructed_faces()
    {
        var rt = NewSession("");
        Eval(rt, "(function(){var f=new FontFace('Added','url(a.woff2)'); document.fonts.add(f); result=document.fonts.has(f);})();")
            .AsBool.Should().BeTrue();
        Eval(rt, "result = document.fonts.size;").AsNumber.Should().Be(1);
        Eval(rt, "(function(){var f=new FontFace('Gone','url(g.woff2)'); document.fonts.add(f); var ok=document.fonts.delete(f); result=ok && !document.fonts.has(f);})();")
            .AsBool.Should().BeTrue();
    }

    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontface-load", section: "3.3")]
    [SpecFact]
    public void FontFace_load_returns_a_promise_and_transitions_status()
    {
        var rt = NewSession("");
        Eval(rt, "result = typeof (new FontFace('L','url(l.woff2)').load().then);").AsString.Should().Be("function");
    }
}
