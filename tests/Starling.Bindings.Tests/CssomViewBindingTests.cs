using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// JS-binding conformance for CSSOM View geometry on the Starling JS engine:
/// <c>getBoundingClientRect</c> returns a spec-shaped <c>DOMRect</c> (eight
/// numeric members + <c>toJSON</c>), <c>getClientRects</c> returns a list, the
/// box-metric accessors are exposed and numeric, and <c>matchMedia</c> returns
/// a MediaQueryList. With no layout host these read the spec-permitted zeros of
/// a never-laid-out document; the interface shape is the conformance surface.
/// </summary>
[TestClass]
public sealed class CssomViewBindingTests
{
    private static JsRuntime NewSession()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var div = doc.CreateElement("div");
        div.SetAttribute("id", "d");
        div.AppendChild(doc.CreateTextNode("x"));
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);
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

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect", section: "6")]
    [SpecFact]
    public void GetBoundingClientRect_returns_a_DOMRect_shape()
    {
        var rt = NewSession();
        Eval(rt, "result = (function(){var r=document.getElementById('d').getBoundingClientRect();" +
            "return ['x','y','width','height','top','right','bottom','left']" +
            ".every(function(k){return typeof r[k]==='number';});})();")
            .AsBool.Should().BeTrue();
        Eval(rt, "result = typeof document.getElementById('d').getBoundingClientRect().toJSON().width;")
            .AsString.Should().Be("number");
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-getclientrects", section: "6")]
    [SpecFact]
    public void GetClientRects_returns_a_list()
    {
        var rt = NewSession();
        Eval(rt, "result = typeof document.getElementById('d').getClientRects().length;")
            .AsString.Should().Be("number");
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-clientwidth", section: "7")]
    [SpecFact]
    public void Box_metric_accessors_are_exposed_and_numeric()
    {
        var rt = NewSession();
        Eval(rt, "result = (function(){var d=document.getElementById('d');" +
            "return ['clientWidth','clientHeight','offsetWidth','offsetHeight'," +
            "'scrollWidth','scrollHeight','scrollTop','scrollLeft']" +
            ".every(function(k){return typeof d[k]==='number';});})();")
            .AsBool.Should().BeTrue();
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-window-matchmedia", section: "4.2")]
    [SpecFact]
    public void MatchMedia_returns_a_MediaQueryList()
    {
        var rt = NewSession();
        Eval(rt, "result = typeof matchMedia('(min-width: 100px)').matches;").AsString.Should().Be("boolean");
        Eval(rt, "result = matchMedia('(min-width: 100px)').media;").AsString.Should().Be("(min-width: 100px)");
    }
}
