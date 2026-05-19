using FluentAssertions;
using Tessera.Css.Animations;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]

public sealed class PerKeyframeTimingTests
{
    private static KeyframesRule ParseKeyframes(string css)
    {
        var sheet = new CssParser(css).ParseStyleSheet(StyleOrigin.Author);
        var rule = sheet.Rules.OfType<AtRule>().Single();
        KeyframesParser.TryParse(rule, out var k).Should().BeTrue();
        return k!;
    }

    [Fact]
    public void Per_keyframe_steps_overrides_animation_timing_function()
    {
        var rule = ParseKeyframes("""
            @keyframes k {
              0%   { opacity: 0; animation-timing-function: steps(2, jump-end) }
              100% { opacity: 1 }
            }
        """);

        rule.Frames.Should().HaveCount(2);
        rule.Frames[0].SegmentTimingFunction.Should().BeOfType<StepsTimingFunction>();
        rule.Frames[1].SegmentTimingFunction.Should().BeNull();

        var engine = new AnimationEngine();
        engine.RegisterKeyframes(rule);
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[]
        {
            new AnimationDeclaration("k", 1000, 0, TimingFunction.Linear, 1,
                AnimationDirection.Normal, AnimationFillMode.None, AnimationPlayState.Running),
        });

        // steps(2, jump-end): t∈[0, 0.5) → 0, t∈[0.5, 1) → 0.5.
        engine.Tick(400);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0, 1e-6);

        engine.Tick(600);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void End_keyframe_timing_function_is_ignored()
    {
        // §7.1: a timing function on the last keyframe has nothing to time
        // (no segment starts at it). It still parses, but doesn't affect
        // sampling.
        var rule = ParseKeyframes("""
            @keyframes k {
              0%   { opacity: 0 }
              100% { opacity: 1; animation-timing-function: steps(4) }
            }
        """);

        var engine = new AnimationEngine();
        engine.RegisterKeyframes(rule);
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[]
        {
            new AnimationDeclaration("k", 1000, 0, TimingFunction.Linear, 1,
                AnimationDirection.Normal, AnimationFillMode.None, AnimationPlayState.Running),
        });

        engine.Tick(500);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }
}
