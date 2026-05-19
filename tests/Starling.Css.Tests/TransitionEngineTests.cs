using FluentAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
namespace Starling.Css.Tests;

[TestClass]
public sealed class TransitionEngineTests
{
    private static Func<PropertyId, CssValue?> Props(
        CssValue? property = null,
        CssValue? duration = null,
        CssValue? delay = null,
        CssValue? timing = null)
    {
        return id => id switch
        {
            PropertyId.TransitionProperty => property ?? new CssKeyword("all"),
            PropertyId.TransitionDuration => duration ?? new CssTime(0.2, CssTimeUnit.Seconds),
            PropertyId.TransitionDelay => delay ?? new CssTime(0, CssTimeUnit.Seconds),
            PropertyId.TransitionTimingFunction => timing ?? new CssKeyword("linear"),
            _ => null,
        };
    }

    [TestMethod]
    public void First_value_does_not_fire_a_transition()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props());
        engine.ActiveCount.Should().Be(0);
        engine.GetEffective(el, PropertyId.Opacity).Should().Be(new CssNumber(1));
    }

    [TestMethod]
    public void Changed_value_starts_a_transition_and_reaches_target_after_duration()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        // Prime: initial value 0
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        // Change: target 1, duration 200ms
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        engine.ActiveCount.Should().Be(1);

        // At t=0 the effective value still matches the from-value.
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0, 1e-6);

        // Mid-way (100ms, linear): expect ~0.5
        engine.Tick(100);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);

        // After the duration: snap to target and complete.
        var completed = engine.Tick(200);
        completed.Should().Be(1);
        engine.ActiveCount.Should().Be(0);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(1, 1e-6);
    }

    [TestMethod]
    public void Delay_postpones_the_first_interpolated_sample()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds),
            delay: new CssTime(50, CssTimeUnit.Milliseconds)));

        engine.Tick(25); // inside the delay window
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0, 1e-6);

        engine.Tick(100); // elapsed - delay = 50ms of 100ms = 0.5 progress
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);

        engine.Tick(150);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(1, 1e-6);
        engine.ActiveCount.Should().Be(0);
    }

    [TestMethod]
    public void Property_not_in_transition_property_list_skips_animation()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            property: new CssKeyword("color")));
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            property: new CssKeyword("color")));
        engine.ActiveCount.Should().Be(0);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().Be(1);
    }

    [TestMethod]
    public void Transition_property_none_disables_animation_even_with_duration()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            property: new CssKeyword("none")));
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            property: new CssKeyword("none")));
        engine.ActiveCount.Should().Be(0);
    }

    [TestMethod]
    public void Interrupted_transition_continues_from_current_sample()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        engine.Tick(100); // halfway; sample ~ 0.5
        var midway = ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value;
        midway.Should().BeApproximately(0.5, 1e-6);

        // New target appears while in flight — From should be the current
        // sample, not the original 0.
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds)));

        // Immediately after the interrupt the effective value still matches
        // the interrupted sample.
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(midway, 1e-6);
    }

    [TestMethod]
    public void Forget_removes_all_state_for_an_element()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds)));
        engine.ActiveCount.Should().Be(1);
        engine.Forget(el);
        engine.ActiveCount.Should().Be(0);
        engine.GetEffective(el, PropertyId.Opacity).Should().BeNull();
    }
}
