using AwesomeAssertions;
using Jint;
using Starling.Common.Diagnostics;
using Starling.Html;
using Starling.Loop;
using Starling.Spec;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// JS-binding conformance for the CSS object model exposed via
/// <c>window.CSS</c> / <c>CSSStyleValue</c>: CSS Typed OM 1 (numeric factories
/// + <c>CSSStyleValue.parse</c>) and CSS Properties and Values API 1
/// (<c>CSS.registerProperty</c>).
/// </summary>
[TestClass]
public sealed class CssOmBindingsTests
{
    private static Engine NewSession()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
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

    // ----- CSS Typed OM 1 -----

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#numeric-factory", section: "4.1")]
    [SpecFact]
    public void CSS_numeric_factories_build_unit_values()
    {
        var e = NewSession();
        e.Evaluate("CSS.px(10).value").AsNumber().Should().Be(10);
        e.Evaluate("CSS.px(10).unit").AsString().Should().Be("px");
        e.Evaluate("CSS.percent(50).unit").AsString().Should().Be("%");
        e.Evaluate("CSS.number(5).unit").AsString().Should().Be("number");
        e.Evaluate("String(CSS.px(10))").AsString().Should().Be("10px");
        e.Evaluate("String(CSS.percent(50))").AsString().Should().Be("50%");
        e.Evaluate("String(CSS.number(5))").AsString().Should().Be("5");
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-cssstylevalue-parse", section: "3.2")]
    [SpecFact]
    public void CSSStyleValue_parse_returns_typed_values()
    {
        var e = NewSession();
        e.Evaluate("CSSStyleValue.parse('width', '10px').value").AsNumber().Should().Be(10);
        e.Evaluate("CSSStyleValue.parse('width', '10px').unit").AsString().Should().Be("px");
        e.Evaluate("CSSStyleValue.parse('display', 'block').value").AsString().Should().Be("block");
        e.Evaluate("String(CSSStyleValue.parse('width', '50%'))").AsString().Should().Be("50%");
    }

    // ----- CSS Properties and Values API 1 (CSS.registerProperty) -----

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#registering-custom-properties", section: "3")]
    [SpecFact]
    public void RegisterProperty_accepts_a_valid_descriptor()
    {
        var e = NewSession();
        e.Evaluate("(function(){try{CSS.registerProperty({name:'--ok',syntax:'<length>',inherits:false,initialValue:'0px'});return 'ok';}catch(x){return x.name;}})()")
            .AsString().Should().Be("ok");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#universal-syntax", section: "3")]
    [SpecFact]
    public void RegisterProperty_universal_syntax_needs_no_initial_value()
    {
        var e = NewSession();
        e.Evaluate("(function(){try{CSS.registerProperty({name:'--any',syntax:'*',inherits:false});return 'ok';}catch(x){return x.name;}})()")
            .AsString().Should().Be("ok");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#dom-css-registerproperty", section: "3")]
    [SpecFact]
    public void RegisterProperty_rejects_bad_name_missing_initial_and_duplicates()
    {
        var e = NewSession();
        // A descriptor is "ok" only if registerProperty does not throw; invalid
        // ones must throw (we assert rejection, not the exact DOMException name).
        static string Try(Engine eng, string js)
            => eng.Evaluate("(function(){try{" + js + ";return 'ok';}catch(x){return 'threw';}})()").AsString();

        // name must be a dashed ident.
        Try(e, "CSS.registerProperty({name:'color',syntax:'<color>',inherits:false,initialValue:'red'})").Should().Be("threw");
        // non-universal syntax requires an initial value.
        Try(e, "CSS.registerProperty({name:'--noinit',syntax:'<length>',inherits:false})").Should().Be("threw");
        // duplicate registration of the same name throws.
        Try(e, "CSS.registerProperty({name:'--dup',syntax:'*',inherits:false})").Should().Be("ok");
        Try(e, "CSS.registerProperty({name:'--dup',syntax:'*',inherits:false})").Should().Be("threw");
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", section: "4.1")]
    [SpecFact]
    public void CSS_escape_serializes_identifiers()
    {
        var e = NewSession();
        e.Evaluate("CSS.escape('a.b')").AsString().Should().Be("a\\.b");
        e.Evaluate("CSS.escape('1abc')").AsString().Should().StartWith("\\31");
    }
}
