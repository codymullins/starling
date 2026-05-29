using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// JS-binding conformance for the CSS object model exposed via
/// <c>window.CSS</c> / <c>CSSStyleValue</c> on the Starling JS engine: CSS
/// Typed OM 1 (numeric factories + <c>CSSStyleValue.parse</c>), CSS Properties
/// and Values API 1 (<c>CSS.registerProperty</c>), and <c>CSS.escape</c>.
/// </summary>
[TestClass]
public sealed class CssTypedOmBindingTests
{
    private static JsRuntime NewSession()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
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

    // ----- CSS Typed OM 1 -----

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#numeric-factory", section: "4.1")]
    [SpecFact]
    public void CSS_numeric_factories_build_unit_values()
    {
        var rt = NewSession();
        Eval(rt, "result = CSS.px(10).value;").AsNumber.Should().Be(10);
        Eval(rt, "result = CSS.px(10).unit;").AsString.Should().Be("px");
        Eval(rt, "result = CSS.percent(50).unit;").AsString.Should().Be("%");
        Eval(rt, "result = CSS.number(5).unit;").AsString.Should().Be("number");
        Eval(rt, "result = String(CSS.px(10));").AsString.Should().Be("10px");
        Eval(rt, "result = String(CSS.percent(50));").AsString.Should().Be("50%");
        Eval(rt, "result = String(CSS.number(5));").AsString.Should().Be("5");
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-cssstylevalue-parse", section: "3.2")]
    [SpecFact]
    public void CSSStyleValue_parse_returns_typed_values()
    {
        var rt = NewSession();
        Eval(rt, "result = CSSStyleValue.parse('width', '10px').value;").AsNumber.Should().Be(10);
        Eval(rt, "result = CSSStyleValue.parse('width', '10px').unit;").AsString.Should().Be("px");
        Eval(rt, "result = CSSStyleValue.parse('display', 'block').value;").AsString.Should().Be("block");
        Eval(rt, "result = String(CSSStyleValue.parse('width', '50%'));").AsString.Should().Be("50%");
    }

    // ----- CSS Properties and Values API 1 (CSS.registerProperty) -----

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#registering-custom-properties", section: "3")]
    [SpecFact]
    public void RegisterProperty_accepts_a_valid_descriptor()
    {
        var rt = NewSession();
        Eval(rt, "try{CSS.registerProperty({name:'--ok',syntax:'<length>',inherits:false,initialValue:'0px'});result='ok';}catch(x){result='threw';}")
            .AsString.Should().Be("ok");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#universal-syntax", section: "3")]
    [SpecFact]
    public void RegisterProperty_universal_syntax_needs_no_initial_value()
    {
        var rt = NewSession();
        Eval(rt, "try{CSS.registerProperty({name:'--any',syntax:'*',inherits:false});result='ok';}catch(x){result='threw';}")
            .AsString.Should().Be("ok");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#dom-css-registerproperty", section: "3")]
    [SpecFact]
    public void RegisterProperty_rejects_bad_name_missing_initial_and_duplicates()
    {
        var rt = NewSession();
        // A descriptor is "ok" only if registerProperty does not throw; invalid
        // ones must throw (we assert rejection, not the exact exception name).
        string Try(string js)
            => Eval(rt, "try{" + js + ";result='ok';}catch(x){result='threw';}").AsString;

        // name must be a dashed ident.
        Try("CSS.registerProperty({name:'color',syntax:'<color>',inherits:false,initialValue:'red'})").Should().Be("threw");
        // non-universal syntax requires an initial value.
        Try("CSS.registerProperty({name:'--noinit',syntax:'<length>',inherits:false})").Should().Be("threw");
        // duplicate registration of the same name throws.
        Try("CSS.registerProperty({name:'--dup',syntax:'*',inherits:false})").Should().Be("ok");
        Try("CSS.registerProperty({name:'--dup',syntax:'*',inherits:false})").Should().Be("threw");
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", section: "4.1")]
    [SpecFact]
    public void CSS_escape_serializes_identifiers()
    {
        var rt = NewSession();
        Eval(rt, "result = CSS.escape('a.b');").AsString.Should().Be("a\\.b");
        Eval(rt, "result = CSS.escape('1abc');").AsString.Should().StartWith("\\31");
    }
}
