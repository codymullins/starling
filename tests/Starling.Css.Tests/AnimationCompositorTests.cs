using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/")]

public sealed class AnimationCompositorTests
{
    private static StyleEngine MakeEngine(string css)
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = new CssParser(css).ParseStyleSheet(StyleOrigin.Author);
        engine.AddStyleSheet(sheet);
        return engine;
    }

    private static Element MakeDiv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var body = doc.CreateElement("body");
        html.AppendChild(body);
        var div = doc.CreateElement("div");
        body.AppendChild(div);
        return div;
    }

    [Fact]
    public void Transition_interpolates_opacity_change_over_time()
    {
        var engine = MakeEngine("""
            div { transition: opacity 1s linear; opacity: 1; }
            div.faded { opacity: 0; }
        """);
        var el = MakeDiv();

        // Prime the snapshot at opacity:1.
        var s1 = engine.ComputeWithAnimations(el, nowMs: 0);
        ((CssNumber)s1.Get(PropertyId.Opacity)).Value.Should().Be(1);

        // Toggle class so the cascaded value flips to 0; this should
        // start a transition starting from 1.
        el.SetAttribute("class", "faded");
        var s2 = engine.ComputeWithAnimations(el, nowMs: 0);
        // At t=0 the transition has only just started — value is still 1.
        ((CssNumber)s2.Get(PropertyId.Opacity)).Value.Should().BeApproximately(1, 1e-6);

        // Advance to the midpoint.
        engine.TransitionEngine.Tick(500);
        var s3 = engine.ComputeWithAnimations(el, nowMs: 500);
        ((CssNumber)s3.Get(PropertyId.Opacity)).Value.Should().BeApproximately(0.5, 1e-6);

        // At the end the transition has dropped out and the static cascade
        // value (0) wins.
        engine.TransitionEngine.Tick(1000);
        var s4 = engine.ComputeWithAnimations(el, nowMs: 1000);
        ((CssNumber)s4.Get(PropertyId.Opacity)).Value.Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Animation_overrides_static_cascade_opacity()
    {
        var engine = MakeEngine("""
            @keyframes fade { 0% { opacity: 0 } 100% { opacity: 1 } }
            div { animation: fade 1s linear; opacity: 0.5 }
        """);
        var el = MakeDiv();

        // First compute primes the animation engine.
        engine.ComputeWithAnimations(el, nowMs: 0);

        // Sample at 500ms — keyframe interpolation gives 0.5 which happens
        // to match the static cascade, so advance to 250 instead to prove
        // the animation is what's driving the value (not the cascade).
        engine.AnimationEngine.Tick(250);
        var sampled = engine.ComputeWithAnimations(el, nowMs: 250);
        ((CssNumber)sampled.Get(PropertyId.Opacity)).Value
            .Should().BeApproximately(0.25, 1e-6);
    }

    [Fact]
    public void Transition_wins_over_animation_on_same_property()
    {
        // CSS Animations 1 §3.2: transitions are layered above animations.
        var engine = MakeEngine("""
            @keyframes fade { 0% { opacity: 0 } 100% { opacity: 1 } }
            div { transition: opacity 1s linear; animation: fade 1s linear; opacity: 1 }
            div.x { opacity: 0 }
        """);
        var el = MakeDiv();

        // Prime + start animation.
        engine.ComputeWithAnimations(el, nowMs: 0);

        // Toggle a class so opacity:1 -> opacity:0 — starts a transition.
        el.SetAttribute("class", "x");
        engine.ComputeWithAnimations(el, nowMs: 0);

        // At t=500: animation alone would be 0.5; transition from 1->0
        // is also 0.5. Make the animation be at a different value (0.75)
        // by jumping animation clock past transition clock, then verify
        // the transition value (0.5 — transition advanced to 500/1000)
        // wins.
        engine.AnimationEngine.Tick(750);
        engine.TransitionEngine.Tick(500);
        var sampled = engine.ComputeWithAnimations(el, nowMs: 500);
        // Transition: from 1, to 0, over 1000ms — at 500ms → 0.5.
        // Animation: 750/1000 progress → opacity 0.75. Transition wins.
        ((CssNumber)sampled.Get(PropertyId.Opacity)).Value
            .Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Forget_clears_per_element_state()
    {
        var engine = MakeEngine("""
            @keyframes fade { 0% { opacity: 0 } 100% { opacity: 1 } }
            div { animation: fade 1s linear; opacity: 0 }
        """);
        var el = MakeDiv();
        engine.ComputeWithAnimations(el, nowMs: 0);
        engine.AnimationEngine.ActiveCount.Should().BeGreaterThan(0);

        engine.Compositor.Forget(el);
        engine.AnimationEngine.Forget(el);
        engine.TransitionEngine.Forget(el);

        engine.AnimationEngine.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void No_animation_no_transition_passes_through_static_values()
    {
        var engine = MakeEngine("div { color: red }");
        var el = MakeDiv();

        var statics = engine.Compute(el);
        var composed = engine.ComputeWithAnimations(el, nowMs: 0);
        composed.Get(PropertyId.Color).Should().BeEquivalentTo(statics.Get(PropertyId.Color));
    }
}
