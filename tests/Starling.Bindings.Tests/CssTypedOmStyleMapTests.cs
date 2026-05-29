using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// CSS Typed OM 1 §6 StylePropertyMap conformance on the Starling JS engine:
/// <c>element.attributeStyleMap</c> (mutable, over the inline style attribute)
/// and <c>element.computedStyleMap()</c> (read-only). Values round-trip as
/// CSSStyleValue objects.
/// </summary>
[TestClass]
public sealed class CssTypedOmStyleMapTests
{
    private static (JsRuntime, Element) NewSession()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var div = doc.CreateElement("div");
        div.SetAttribute("id", "d");
        div.SetAttribute("style", "width: 10px; color: red");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(div);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, div);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-element-attributestylemap", section: "6.3")]
    [SpecFact]
    public void AttributeStyleMap_reads_inline_declarations_as_typed_values()
    {
        var (rt, _) = NewSession();
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.get('width').value;").AsNumber.Should().Be(10);
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.get('width').unit;").AsString.Should().Be("px");
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.has('color');").AsBool.Should().BeTrue();
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.has('height');").AsBool.Should().BeFalse();
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.size;").AsNumber.Should().Be(2);
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-stylepropertymap-set", section: "6.3")]
    [SpecFact]
    public void AttributeStyleMap_set_and_delete_mutate_the_style_attribute()
    {
        var (rt, div) = NewSession();
        // set accepts a CSSStyleValue object…
        Eval(rt, "document.getElementById('d').attributeStyleMap.set('height', CSS.px(20));");
        div.GetAttribute("style").Should().Contain("height: 20px");
        // …and a plain string.
        Eval(rt, "document.getElementById('d').attributeStyleMap.set('display', 'block');");
        div.GetAttribute("style").Should().Contain("display: block");
        // delete removes a property.
        Eval(rt, "document.getElementById('d').attributeStyleMap.delete('color');");
        div.GetAttribute("style").Should().NotContain("color");
        // round-trips back through get.
        Eval(rt, "result = document.getElementById('d').attributeStyleMap.get('height').value;").AsNumber.Should().Be(20);
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-stylepropertymapreadonly-foreach", section: "6.2")]
    [SpecFact]
    public void AttributeStyleMap_forEach_visits_each_declaration()
    {
        var (rt, _) = NewSession();
        Eval(rt, "result = (function(){var n=0; document.getElementById('d').attributeStyleMap.forEach(function(){n++;}); return n;})();")
            .AsNumber.Should().Be(2);
    }

    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/#dom-element-computedstylemap", section: "6.2")]
    [SpecFact]
    public void ComputedStyleMap_is_exposed_and_read_only_shaped()
    {
        var (rt, _) = NewSession();
        // No layout host in this unit context, so computed values resolve empty;
        // the read-only map interface shape is the conformance surface here.
        Eval(rt, "result = typeof document.getElementById('d').computedStyleMap().get;").AsString.Should().Be("function");
        Eval(rt, "result = typeof document.getElementById('d').computedStyleMap().has;").AsString.Should().Be("function");
        Eval(rt, "result = typeof document.getElementById('d').computedStyleMap().size;").AsString.Should().Be("number");
    }
}
