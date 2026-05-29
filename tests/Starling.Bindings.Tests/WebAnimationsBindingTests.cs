using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// Web Animations 1 §4 JS object-model conformance on the Starling JS engine:
/// <c>element.animate()</c> returns an <c>Animation</c> with the expected
/// control surface and an associated <c>KeyframeEffect</c>. (No animation host
/// is installed here, so playback controls are inert — the interface shape and
/// keyframe/timing readback are the conformance surface.)
/// </summary>
[TestClass]
public sealed class WebAnimationsBindingTests
{
    private static (JsRuntime, Element) NewSession()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var div = doc.CreateElement("div");
        div.SetAttribute("id", "d");
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

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animatable-animate", section: "4")]
    [SpecFact]
    public void Animate_returns_an_Animation_with_a_control_surface()
    {
        var (rt, _) = NewSession();
        const string make = "var a = document.getElementById('d').animate([{opacity:0},{opacity:1}], 500);";
        Eval(rt, make + "result = typeof a.play;").AsString.Should().Be("function");
        Eval(rt, make + "result = typeof a.pause;").AsString.Should().Be("function");
        Eval(rt, make + "result = typeof a.cancel;").AsString.Should().Be("function");
        Eval(rt, make + "result = typeof a.finish;").AsString.Should().Be("function");
        Eval(rt, make + "result = typeof a.currentTime;").AsString.Should().Be("number");
        Eval(rt, make + "result = typeof a.playState;").AsString.Should().Be("string");
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-finished", section: "4")]
    [SpecFact]
    public void Animation_finished_is_a_thenable()
    {
        var (rt, _) = NewSession();
        Eval(rt, "result = typeof document.getElementById('d').animate([{opacity:0},{opacity:1}], 500).finished.then;")
            .AsString.Should().Be("function");
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-keyframeeffect-getkeyframes", section: "4")]
    [SpecFact]
    public void KeyframeEffect_exposes_keyframes_and_timing()
    {
        var (rt, _) = NewSession();
        const string make = "var e = document.getElementById('d').animate([{opacity:0},{opacity:1}], {duration:500, iterations:2, easing:'ease-in'}).effect;";
        Eval(rt, make + "result = e.getKeyframes().length;").AsNumber.Should().Be(2);
        Eval(rt, make + "result = e.getKeyframes()[0].offset;").AsNumber.Should().Be(0);
        Eval(rt, make + "result = e.getKeyframes()[1].offset;").AsNumber.Should().Be(1);
        Eval(rt, make + "result = e.getTiming().duration;").AsNumber.Should().Be(500);
        Eval(rt, make + "result = e.getTiming().iterations;").AsNumber.Should().Be(2);
        Eval(rt, make + "result = e.getTiming().easing;").AsString.Should().Be("ease-in");
        Eval(rt, make + "result = e.getComputedTiming().activeDuration;").AsNumber.Should().Be(1000);
    }
}
