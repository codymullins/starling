using AwesomeAssertions;
using Jint;
using Starling.Common.Diagnostics;
using Starling.Html;
using Starling.Loop;
using Starling.Spec;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// JS-binding conformance for CSSOM View geometry: <c>getBoundingClientRect</c>
/// returns a spec-shaped <c>DOMRect</c>, <c>getClientRects</c> returns a list,
/// and the box-metric accessors (<c>clientWidth</c>, <c>offsetWidth</c>,
/// <c>scrollWidth/Top</c>, …) are exposed. With no layout host these read the
/// spec-permitted zeros of a never-laid-out document, but the interface shape is
/// the conformance surface tested here.
/// </summary>
[TestClass]
public sealed class CssomViewBindingsTests
{
    private static Engine NewSession()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body><div id='d'>x</div></body></html>");
        var baseUrl = Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new Engine();
        var ctx = new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: baseUrl,
            http: new Starling.Net.StarlingHttpClient(),
            diag: NoopDiagnostics.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return engine;
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-getboundingclientrect", section: "6")]
    [SpecFact]
    public void GetBoundingClientRect_returns_a_DOMRect_shape()
    {
        var e = NewSession();
        // All eight DOMRect members are present and numeric.
        e.Evaluate("(function(){var r=document.getElementById('d').getBoundingClientRect();" +
            "return ['x','y','width','height','top','right','bottom','left']" +
            ".every(function(k){return typeof r[k]==='number';});})()")
            .AsBoolean().Should().BeTrue();
        // toJSON() round-trips the members.
        e.Evaluate("typeof document.getElementById('d').getBoundingClientRect().toJSON().width")
            .AsString().Should().Be("number");
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-getclientrects", section: "6")]
    [SpecFact]
    public void GetClientRects_returns_a_list()
    {
        var e = NewSession();
        e.Evaluate("typeof document.getElementById('d').getClientRects().length")
            .AsString().Should().Be("number");
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-element-clientwidth", section: "7")]
    [SpecFact]
    public void Box_metric_accessors_are_exposed_and_numeric()
    {
        var e = NewSession();
        e.Evaluate("(function(){var d=document.getElementById('d');" +
            "return ['clientWidth','clientHeight','offsetWidth','offsetHeight'," +
            "'scrollWidth','scrollHeight','scrollTop','scrollLeft']" +
            ".every(function(k){return typeof d[k]==='number';});})()")
            .AsBoolean().Should().BeTrue();
    }

    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-window-matchmedia", section: "4.2")]
    [SpecFact]
    public void MatchMedia_returns_a_MediaQueryList()
    {
        var e = NewSession();
        e.Evaluate("typeof matchMedia('(min-width: 100px)').matches").AsString().Should().Be("boolean");
        e.Evaluate("matchMedia('(min-width: 100px)').media").AsString().Should().Be("(min-width: 100px)");
    }
}
