using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Tests;

/// <summary>
/// CSS Transforms 1 §10: an animated <c>transform</c> must interpolate, not snap
/// at 50%. Regression for the bug where keyframe transform values (stored as
/// function values, not <see cref="CssTransform"/>) missed the interpolator's
/// type switch and fell back to the discrete rule.
/// </summary>
[TestClass]
public sealed class TransformAnimationTests
{
    private static StyleEngine MakeEngine(string css)
    {
        var e = new StyleEngine(includeUserAgentStyleSheet: false);
        e.AddStyleSheet(new CssParser(css).ParseStyleSheet(StyleOrigin.Author));
        return e;
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

    [TestMethod]
    public void Rotate_animation_interpolates_smoothly()
    {
        var e = MakeEngine(
            "@keyframes spin { from { transform: rotate(0deg) } to { transform: rotate(90deg) } } " +
            "div { animation: spin 1000ms linear }");
        var el = MakeDiv();
        e.ComputeWithAnimations(el, 0); // prime + register

        double AngleDegAt(double ms)
        {
            e.AnimationEngine.Tick(ms);
            var value = e.ComputeWithAnimations(el, ms).Get(PropertyId.Transform);
            var m = CssTransformParser.Parse(value).ToMatrix(0, 0);
            return Math.Atan2(m.B, m.A) * 180d / Math.PI;
        }

        // Linear 0deg -> 90deg over 1000ms: quarter points are 22.5 / 45 / 67.5.
        AngleDegAt(250).Should().BeApproximately(22.5, 0.5);
        AngleDegAt(500).Should().BeApproximately(45.0, 0.5);
        AngleDegAt(750).Should().BeApproximately(67.5, 0.5);
    }

    [TestMethod]
    public void None_to_translate_animation_interpolates_via_matrix()
    {
        var e = MakeEngine(
            "@keyframes slide { from { transform: none } to { transform: translateX(100px) } } " +
            "div { animation: slide 1000ms linear }");
        var el = MakeDiv();
        e.ComputeWithAnimations(el, 0);

        double TranslateXAt(double ms)
        {
            e.AnimationEngine.Tick(ms);
            var value = e.ComputeWithAnimations(el, ms).Get(PropertyId.Transform);
            return CssTransformParser.Parse(value).ToMatrix(0, 0).E;
        }

        TranslateXAt(250).Should().BeApproximately(25.0, 0.5);
        TranslateXAt(750).Should().BeApproximately(75.0, 0.5);
    }
}
