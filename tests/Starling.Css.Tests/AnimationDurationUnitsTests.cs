using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Tests;

/// <summary>
/// CSS Animations 1 §3.1: <c>animation-duration</c> accepts <c>&lt;time&gt;</c> in
/// either seconds or milliseconds; the two units must scale consistently. Guards
/// a bug where one unit was interpreted at the wrong scale.
/// </summary>
[TestClass]
public sealed class AnimationDurationUnitsTests
{
    private static StyleEngine MakeEngine(string css)
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(new CssParser(css).ParseStyleSheet(StyleOrigin.Author));
        return engine;
    }

    private static Element MakeDiv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var div = doc.CreateElement("div");
        html.AppendChild(div);
        return div;
    }

    private static double OpacityAtQuarter(string animation, double durationMs)
    {
        var engine = MakeEngine(
            "@keyframes fade { 0% { opacity: 0 } 100% { opacity: 1 } } " +
            $"div {{ {animation}; opacity: 0.5 }}");
        var el = MakeDiv();
        engine.ComputeWithAnimations(el, 0); // prime + register
        var t = durationMs / 4d;             // 25% through
        engine.AnimationEngine.Tick(t);
        return ((CssNumber)engine.ComputeWithAnimations(el, t).Get(PropertyId.Opacity)).Value;
    }

    [TestMethod]
    [DataRow("animation: fade 1s linear", 1000.0)]
    [DataRow("animation: fade 1000ms linear", 1000.0)]
    [DataRow("animation: fade 8s linear", 8000.0)]
    [DataRow("animation: fade 8000ms linear", 8000.0)]
    [DataRow("animation: fade 250ms linear", 250.0)]
    public void Duration_units_scale_consistently(string animation, double durationMs)
        => OpacityAtQuarter(animation, durationMs).Should()
            .BeApproximately(0.25, 1e-3, $"'{animation}' should be 25% done at {durationMs / 4}ms");
}
