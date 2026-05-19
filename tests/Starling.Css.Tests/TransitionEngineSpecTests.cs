using FluentAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
namespace Starling.Css.Tests;

/// <summary>
/// Additional CSS Transitions Level 1 conformance tests.
/// Reference: https://www.w3.org/TR/css-transitions-1/ §3.
/// </summary>
[TestClass]
public sealed class TransitionEngineSpecTests
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

    // CSS Transitions 1 §3 — "If the combined duration is zero, no
    // transition is generated." A zero-duration change snaps to the new
    // value immediately without creating an active running transition.
    [TestMethod]
    public void Zero_duration_with_zero_delay_does_not_create_running_transition()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            duration: new CssTime(0, CssTimeUnit.Milliseconds)));
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(0, CssTimeUnit.Milliseconds)));

        engine.ActiveCount.Should().Be(0);
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().Be(1);
    }

    // CSS Transitions 1 §3 — transition-property: all should match every
    // animatable property (the default helper uses "all"; this verifies
    // multiple properties on the same element each start transitions).
    [TestMethod]
    public void Property_all_starts_transitions_for_every_changed_property()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");

        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Width,
            new CssLength(0, CssLengthUnit.Px), Props());

        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        engine.OnComputedValueChanged(el, PropertyId.Width,
            new CssLength(200, CssLengthUnit.Px), Props(
                duration: new CssTime(200, CssTimeUnit.Milliseconds)));

        engine.ActiveCount.Should().Be(2);
    }

    // CSS Transitions 1 §3 — completing a transition emits exactly one
    // completion event and clears the active state.
    [TestMethod]
    public void Completed_transition_reports_completion_once_and_clears_state()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds)));

        engine.Tick(100).Should().Be(1);
        engine.ActiveCount.Should().Be(0);

        // Subsequent ticks after completion must not re-report.
        engine.Tick(200).Should().Be(0);
    }

    // CSS Transitions 1 §3 — after a transition completes, the next
    // value change starts a fresh transition (from the held final value).
    [TestMethod]
    public void New_target_after_completion_starts_fresh_transition()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds)));
        engine.Tick(100); // complete

        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds)));

        engine.ActiveCount.Should().Be(1);
        engine.Tick(150); // 50ms into new transition
        ((CssNumber)engine.GetEffective(el, PropertyId.Opacity)!).Value
            .Should().BeApproximately(0.5, 1e-6);
    }
}
