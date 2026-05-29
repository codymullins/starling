using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssAnimations1;

/// <summary>
/// Web Animations 1 — the engine's programmatic-animation surface that backs
/// JS <c>element.animate()</c>. These exercise <see cref="AnimationEngine"/>
/// directly (the same sampling path the painter composites), covering
/// registration that survives a cascade, playback control (pause / play /
/// cancel / finish / currentTime), and removal.
/// Spec: <see href="https://www.w3.org/TR/web-animations-1/"/>
/// </summary>
[TestClass]
[Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/", section: "4")]
public sealed class ScriptAnimationTests
{
    private static KeyframesRule FadeRule(string name = "waapi-0")
        => new(name, new[]
        {
            new Keyframe(0.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0)) }),
            new Keyframe(1.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(1)) }),
        });

    private static AnimationDeclaration Decl(double durationMs = 1000, double delayMs = 0, double iterations = 1)
        => new("waapi-0", durationMs, delayMs, TimingFunction.Linear, iterations,
               AnimationDirection.Normal, AnimationFillMode.Both, AnimationPlayState.Running);

    private static double Opacity(AnimationEngine e, Element el)
        => ((CssNumber)e.GetEffective(el, PropertyId.Opacity)!).Value;

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animatable-animate", section: "4")]
    [SpecFact]
    public void ScriptAnimation_samples_like_a_declarative_one()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        engine.Tick(0);
        Opacity(engine, el).Should().BeApproximately(0, 1e-6);
        engine.Tick(500);
        Opacity(engine, el).Should().BeApproximately(0.5, 1e-6);
        engine.Tick(1000);
        Opacity(engine, el).Should().BeApproximately(1, 1e-6);
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-cancel", section: "4")]
    [SpecFact]
    public void ScriptAnimation_survives_a_recascade_that_clears_declarative_animations()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        // A cascade pass with no animation-name clears _active — the script
        // animation must remain.
        engine.OnAnimationsCascaded(el, System.Array.Empty<AnimationDeclaration>());

        engine.Tick(500);
        Opacity(engine, el).Should().BeApproximately(0.5, 1e-6);
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-pause", section: "4")]
    [SpecFact]
    public void Pause_freezes_and_play_resumes()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        var inst = engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        engine.Tick(250);
        inst.ScriptPause();
        engine.Tick(800); // clock advances but the head is frozen at 0.25
        Opacity(engine, el).Should().BeApproximately(0.25, 1e-6);

        inst.ScriptPlay();
        engine.Tick(900); // resumes: 100ms more elapsed → 0.35
        Opacity(engine, el).Should().BeApproximately(0.35, 1e-6);
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-currenttime", section: "4")]
    [SpecFact]
    public void SetCurrentTime_moves_the_playback_head()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        var inst = engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        engine.Tick(100);
        inst.ScriptSetCurrentTime(750);
        engine.Tick(100); // currentTime stays 750 since start was shifted
        Opacity(engine, el).Should().BeApproximately(0.75, 1e-6);
        inst.ScriptCurrentTime().Should().BeApproximately(750, 1e-6);
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-cancel", section: "4")]
    [SpecFact]
    public void Cancel_removes_all_effects()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        var inst = engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        engine.Tick(500);
        Opacity(engine, el).Should().BeApproximately(0.5, 1e-6);

        inst.ScriptCancel();
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();

        // play() reverses cancellation.
        inst.ScriptPlay();
        engine.Tick(500);
        Opacity(engine, el).Should().BeApproximately(0.5, 1e-6);
    }

    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#dom-animation-finish", section: "4")]
    [SpecFact]
    public void Finish_jumps_to_the_end_and_remove_drops_the_animation()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        var inst = engine.AddScriptAnimation(el, Decl(durationMs: 1000), FadeRule());

        engine.Tick(100);
        inst.ScriptFinish();
        Opacity(engine, el).Should().BeApproximately(1, 1e-6);

        engine.RemoveScriptAnimation(inst);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }
}
