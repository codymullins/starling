using FluentAssertions;
using Tessera.Css.Animations;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]

public sealed class AnimationEngineTests
{
    private static KeyframesRule SimpleFade()
        => new("fade", new[]
        {
            new Keyframe(0.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0)) }),
            new Keyframe(1.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(1)) }),
        });

    private static AnimationDeclaration Decl(
        string name = "fade",
        double durationMs = 1000,
        double delayMs = 0,
        double iterationCount = 1,
        AnimationDirection direction = AnimationDirection.Normal,
        AnimationFillMode fillMode = AnimationFillMode.None,
        AnimationPlayState playState = AnimationPlayState.Running,
        TimingFunction? timing = null)
        => new(name, durationMs, delayMs, timing ?? TimingFunction.Linear,
            iterationCount, direction, fillMode, playState);

    [Fact]
    public void Linear_progression_samples_through_intermediate_values()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000) });

        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0, 1e-6);

        engine.Tick(250);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.25, 1e-6);

        engine.Tick(500);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);

        engine.Tick(750);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.75, 1e-6);
    }

    [Fact]
    public void Animation_without_fill_mode_returns_null_after_end()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100) });

        engine.Tick(50);
        engine.GetEffective(el, PropertyId.Opacity).Should().NotBeNull();

        engine.Tick(200);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }

    [Fact]
    public void Fill_mode_forwards_holds_final_value()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, fillMode: AnimationFillMode.Forwards) });

        engine.Tick(200);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public void Fill_mode_backwards_holds_initial_value_during_delay()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, delayMs: 50, fillMode: AnimationFillMode.Backwards) });

        engine.Tick(25); // still in delay window
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Without_fill_backwards_no_value_during_delay()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, delayMs: 50) });
        engine.Tick(25);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }

    [Fact]
    public void Alternate_direction_flips_each_iteration()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, iterationCount: 4, direction: AnimationDirection.Alternate) });

        // Iteration 0 (normal): at t=50 (mid), opacity ~ 0.5
        engine.Tick(50);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);

        // Just past start of iteration 1 (going from 1 toward 0): expect > 0.5
        engine.Tick(105);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeGreaterThan(0.5);

        // Mid iteration 1 (reverse): opacity ~ 0.5 (going 1→0)
        engine.Tick(150);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Reverse_direction_starts_at_end_value()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, direction: AnimationDirection.Reverse) });

        engine.Tick(0);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(1, 1e-6);
        engine.Tick(50);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void Paused_play_state_freezes_sample()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000) });
        engine.Tick(300);
        var midSample = ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value;
        midSample.Should().BeApproximately(0.3, 1e-6);

        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000, playState: AnimationPlayState.Paused) });
        engine.Tick(800);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(midSample, 1e-6);

        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000, playState: AnimationPlayState.Running) });
        engine.Tick(900);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.4, 1e-6);
    }

    [Fact]
    public void Iteration_count_zero_or_negative_produces_no_output()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, iterationCount: 0) });
        engine.Tick(50);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }

    [Fact]
    public void Unregistered_keyframes_name_produces_no_value()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(name: "missing", durationMs: 100) });
        engine.Tick(50);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }

    [Fact]
    public void Multi_layer_later_animation_overrides_earlier_for_same_property()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        engine.RegisterKeyframes(new KeyframesRule("solid", new[]
        {
            new Keyframe(0.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0.2)) }),
            new Keyframe(1.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0.2)) }),
        }));
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[]
        {
            Decl(name: "fade",  durationMs: 1000),
            Decl(name: "solid", durationMs: 1000),
        });
        engine.Tick(500);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.2, 1e-6); // solid wins as later layer
    }

    [Fact]
    public void Removed_animation_name_stops_sampling()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000) });
        engine.Tick(500);
        engine.GetEffective(el, PropertyId.Opacity).Should().NotBeNull();

        engine.OnAnimationsCascaded(el, Array.Empty<AnimationDeclaration>());
        engine.Tick(600);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
        engine.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Keyframe_without_property_falls_back_to_endpoints()
    {
        // The fade keyframe declares opacity at 0% and 100%; there is no
        // explicit 50% offset, so a sample at progress 0.5 must
        // interpolate between the two anchoring frames directly.
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 1000) });
        engine.Tick(500);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }
}
