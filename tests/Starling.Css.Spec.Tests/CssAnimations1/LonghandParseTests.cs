using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssAnimations1;

/// <summary>
/// §3 conformance — animation longhand + shorthand parse and initial values.
/// Spec: <see href="https://www.w3.org/TR/css-animations-1/#animations"/>
/// </summary>
[TestClass]
[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/", section: "3")]
public sealed class LonghandParseTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ── §3.1  animation-name ──────────────────────────────────────────────

    // §3.1 — animation-name: none parses as keyword "none".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-name", section: "3.1")]
    [SpecFact]
    public void Animation_name_none_parses()
    {
        ValueOf("animation-name: none", PropertyId.AnimationName)
            .Should().Be(new CssKeyword("none"));
    }

    // §3.1 — animation-name: slide parses as keyword "slide".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-name", section: "3.1")]
    [SpecFact]
    public void Animation_name_ident_parses()
    {
        ValueOf("animation-name: slide", PropertyId.AnimationName)
            .Should().Be(new CssKeyword("slide"));
    }

    // §3.1 — initial value of animation-name is "none".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-name", section: "3.1")]
    [SpecFact]
    public void Animation_name_initial_value_is_none()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationName)
            .Should().Be(new CssKeyword("none"));
    }

    // §3.1 — animation-name is not inherited.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-name", section: "3.1")]
    [SpecFact]
    public void Animation_name_is_not_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.AnimationName).Should().BeFalse();
    }

    // ── §3.2  animation-duration ──────────────────────────────────────────

    // §3.2 — animation-duration: 2s parses as time value.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-duration", section: "3.2")]
    [SpecFact]
    public void Animation_duration_seconds_parses()
    {
        var v = ValueOf("animation-duration: 2s", PropertyId.AnimationDuration);
        v.Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(2, 1e-9);
    }

    // §3.2 — animation-duration: 500ms parses to 0.5 seconds.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-duration", section: "3.2")]
    [SpecFact]
    public void Animation_duration_milliseconds_parses()
    {
        var v = ValueOf("animation-duration: 500ms", PropertyId.AnimationDuration);
        v.Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(0.5, 1e-9);
    }

    // §3.2 — initial value of animation-duration is 0s.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-duration", section: "3.2")]
    [SpecFact]
    public void Animation_duration_initial_value_is_zero()
    {
        var init = PropertyRegistry.InitialValue(PropertyId.AnimationDuration);
        // Stored as CssDimension(0, "s") per PropertyRegistry.
        init.Should().BeOfType<CssDimension>().Which.Value.Should().Be(0);
    }

    // ── §3.3  animation-timing-function ───────────────────────────────────

    // §3.3 — animation-timing-function: ease parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_ease_parses()
    {
        ValueOf("animation-timing-function: ease", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("ease"));
    }

    // §3.3 — animation-timing-function: linear parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_linear_parses()
    {
        ValueOf("animation-timing-function: linear", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("linear"));
    }

    // §3.3 — animation-timing-function: ease-in parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_ease_in_parses()
    {
        ValueOf("animation-timing-function: ease-in", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("ease-in"));
    }

    // §3.3 — animation-timing-function: ease-out parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_ease_out_parses()
    {
        ValueOf("animation-timing-function: ease-out", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("ease-out"));
    }

    // §3.3 — animation-timing-function: ease-in-out parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_ease_in_out_parses()
    {
        ValueOf("animation-timing-function: ease-in-out", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("ease-in-out"));
    }

    // §3.3 — animation-timing-function: step-start parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_step_start_parses()
    {
        ValueOf("animation-timing-function: step-start", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("step-start"));
    }

    // §3.3 — animation-timing-function: step-end parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_step_end_parses()
    {
        ValueOf("animation-timing-function: step-end", PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("step-end"));
    }

    // §3.3 — animation-timing-function: cubic-bezier(0.4, 0, 0.2, 1) parses as function.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_cubic_bezier_parses_as_function()
    {
        var v = ValueOf("animation-timing-function: cubic-bezier(0.4, 0, 0.2, 1)", PropertyId.AnimationTimingFunction);
        v.Should().BeOfType<CssFunctionValue>().Which.Name.Should().Be("cubic-bezier");
    }

    // §3.3 — initial value of animation-timing-function is "ease".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-timing-function", section: "3.3")]
    [SpecFact]
    public void Animation_timing_function_initial_is_ease()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationTimingFunction)
            .Should().Be(new CssKeyword("ease"));
    }

    // ── §3.4  animation-delay ─────────────────────────────────────────────

    // §3.4 — animation-delay: 0.5s parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-delay", section: "3.4")]
    [SpecFact]
    public void Animation_delay_seconds_parses()
    {
        var v = ValueOf("animation-delay: 0.5s", PropertyId.AnimationDelay);
        v.Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(0.5, 1e-9);
    }

    // §3.4 — animation-delay: -200ms parses (negative delays are valid).
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-delay", section: "3.4")]
    [SpecFact]
    public void Animation_delay_negative_parses()
    {
        var v = ValueOf("animation-delay: -200ms", PropertyId.AnimationDelay);
        v.Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(-0.2, 1e-9);
    }

    // §3.4 — initial value of animation-delay is 0s.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-delay", section: "3.4")]
    [SpecFact]
    public void Animation_delay_initial_is_zero()
    {
        var init = PropertyRegistry.InitialValue(PropertyId.AnimationDelay);
        init.Should().BeOfType<CssDimension>().Which.Value.Should().Be(0);
    }

    // ── §3.5  animation-iteration-count ───────────────────────────────────

    // §3.5 — animation-iteration-count: 3 parses as number.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-iteration-count", section: "3.5")]
    [SpecFact]
    public void Animation_iteration_count_integer_parses()
    {
        var v = ValueOf("animation-iteration-count: 3", PropertyId.AnimationIterationCount);
        v.Should().BeOfType<CssNumber>().Which.Value.Should().Be(3);
    }

    // §3.5 — animation-iteration-count: 2.5 parses as fractional number.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-iteration-count", section: "3.5")]
    [SpecFact]
    public void Animation_iteration_count_fractional_parses()
    {
        var v = ValueOf("animation-iteration-count: 2.5", PropertyId.AnimationIterationCount);
        v.Should().BeOfType<CssNumber>().Which.Value.Should().BeApproximately(2.5, 1e-9);
    }

    // §3.5 — animation-iteration-count: infinite parses as keyword.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-iteration-count", section: "3.5")]
    [SpecFact]
    public void Animation_iteration_count_infinite_parses_as_keyword()
    {
        ValueOf("animation-iteration-count: infinite", PropertyId.AnimationIterationCount)
            .Should().Be(new CssKeyword("infinite"));
    }

    // §3.5 — initial value of animation-iteration-count is 1.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-iteration-count", section: "3.5")]
    [SpecFact]
    public void Animation_iteration_count_initial_is_one()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationIterationCount)
            .Should().Be(new CssNumber(1));
    }

    // ── §3.6  animation-direction ─────────────────────────────────────────

    // §3.6 — animation-direction: normal parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-direction", section: "3.6")]
    [SpecFact]
    public void Animation_direction_normal_parses()
    {
        ValueOf("animation-direction: normal", PropertyId.AnimationDirection)
            .Should().Be(new CssKeyword("normal"));
    }

    // §3.6 — animation-direction: reverse parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-direction", section: "3.6")]
    [SpecFact]
    public void Animation_direction_reverse_parses()
    {
        ValueOf("animation-direction: reverse", PropertyId.AnimationDirection)
            .Should().Be(new CssKeyword("reverse"));
    }

    // §3.6 — animation-direction: alternate parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-direction", section: "3.6")]
    [SpecFact]
    public void Animation_direction_alternate_parses()
    {
        ValueOf("animation-direction: alternate", PropertyId.AnimationDirection)
            .Should().Be(new CssKeyword("alternate"));
    }

    // §3.6 — animation-direction: alternate-reverse parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-direction", section: "3.6")]
    [SpecFact]
    public void Animation_direction_alternate_reverse_parses()
    {
        ValueOf("animation-direction: alternate-reverse", PropertyId.AnimationDirection)
            .Should().Be(new CssKeyword("alternate-reverse"));
    }

    // §3.6 — initial value of animation-direction is "normal".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-direction", section: "3.6")]
    [SpecFact]
    public void Animation_direction_initial_is_normal()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationDirection)
            .Should().Be(new CssKeyword("normal"));
    }

    // ── §3.7  animation-fill-mode ─────────────────────────────────────────

    // §3.7 — animation-fill-mode: none parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-fill-mode", section: "3.7")]
    [SpecFact]
    public void Animation_fill_mode_none_parses()
    {
        ValueOf("animation-fill-mode: none", PropertyId.AnimationFillMode)
            .Should().Be(new CssKeyword("none"));
    }

    // §3.7 — animation-fill-mode: forwards parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-fill-mode", section: "3.7")]
    [SpecFact]
    public void Animation_fill_mode_forwards_parses()
    {
        ValueOf("animation-fill-mode: forwards", PropertyId.AnimationFillMode)
            .Should().Be(new CssKeyword("forwards"));
    }

    // §3.7 — animation-fill-mode: backwards parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-fill-mode", section: "3.7")]
    [SpecFact]
    public void Animation_fill_mode_backwards_parses()
    {
        ValueOf("animation-fill-mode: backwards", PropertyId.AnimationFillMode)
            .Should().Be(new CssKeyword("backwards"));
    }

    // §3.7 — animation-fill-mode: both parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-fill-mode", section: "3.7")]
    [SpecFact]
    public void Animation_fill_mode_both_parses()
    {
        ValueOf("animation-fill-mode: both", PropertyId.AnimationFillMode)
            .Should().Be(new CssKeyword("both"));
    }

    // §3.7 — initial value of animation-fill-mode is "none".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-fill-mode", section: "3.7")]
    [SpecFact]
    public void Animation_fill_mode_initial_is_none()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationFillMode)
            .Should().Be(new CssKeyword("none"));
    }

    // ── §3.8  animation-play-state ────────────────────────────────────────

    // §3.8 — animation-play-state: running parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-play-state", section: "3.8")]
    [SpecFact]
    public void Animation_play_state_running_parses()
    {
        ValueOf("animation-play-state: running", PropertyId.AnimationPlayState)
            .Should().Be(new CssKeyword("running"));
    }

    // §3.8 — animation-play-state: paused parses.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-play-state", section: "3.8")]
    [SpecFact]
    public void Animation_play_state_paused_parses()
    {
        ValueOf("animation-play-state: paused", PropertyId.AnimationPlayState)
            .Should().Be(new CssKeyword("paused"));
    }

    // §3.8 — initial value of animation-play-state is "running".
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation-play-state", section: "3.8")]
    [SpecFact]
    public void Animation_play_state_initial_is_running()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationPlayState)
            .Should().Be(new CssKeyword("running"));
    }

    // ── §3  animation shorthand ───────────────────────────────────────────

    // §3 — shorthand with only name + duration sets correct longhands.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_name_and_duration_expands()
    {
        var decls = Expand("animation: slide 2s");
        decls.Single(d => d.Id == PropertyId.AnimationName).Value
            .Should().Be(new CssKeyword("slide"));
        decls.Single(d => d.Id == PropertyId.AnimationDuration).Value
            .Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(2, 1e-9);
    }

    // §3 — shorthand: name, duration, timing, delay.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_with_timing_and_delay_expands()
    {
        var decls = Expand("animation: slide 2s ease-in 0.5s");
        decls.Single(d => d.Id == PropertyId.AnimationName).Value.Should().Be(new CssKeyword("slide"));
        decls.Single(d => d.Id == PropertyId.AnimationTimingFunction).Value.Should().Be(new CssKeyword("ease-in"));
        var delay = decls.Single(d => d.Id == PropertyId.AnimationDelay).Value;
        delay.Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(0.5, 1e-9);
    }

    // §3 — shorthand: full specification with all longhands.
    // animation: slide 2s ease-in 0.5s infinite alternate both
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_all_longhands_expand()
    {
        var decls = Expand("animation: slide 2s ease-in 0.5s infinite alternate both");

        decls.Single(d => d.Id == PropertyId.AnimationName).Value
            .Should().Be(new CssKeyword("slide"));

        decls.Single(d => d.Id == PropertyId.AnimationDuration).Value
            .Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(2, 1e-9);

        decls.Single(d => d.Id == PropertyId.AnimationTimingFunction).Value
            .Should().Be(new CssKeyword("ease-in"));

        decls.Single(d => d.Id == PropertyId.AnimationDelay).Value
            .Should().BeOfType<CssTime>().Which.InSeconds.Should().BeApproximately(0.5, 1e-9);

        decls.Single(d => d.Id == PropertyId.AnimationIterationCount).Value
            .Should().Be(new CssKeyword("infinite"));

        decls.Single(d => d.Id == PropertyId.AnimationDirection).Value
            .Should().Be(new CssKeyword("alternate"));

        decls.Single(d => d.Id == PropertyId.AnimationFillMode).Value
            .Should().Be(new CssKeyword("both"));
    }

    // §3 — shorthand with only "none" fills fill-mode (not name) per impl priority.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_none_fills_fill_mode_first()
    {
        // Per ExpandAnimation: the first "none" goes to fill-mode.
        var decls = Expand("animation: none 1s");
        decls.Single(d => d.Id == PropertyId.AnimationFillMode).Value
            .Should().Be(new CssKeyword("none"));
    }

    // §3 — shorthand with play-state paused.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_paused_play_state_expands()
    {
        var decls = Expand("animation: slide 1s paused");
        decls.Single(d => d.Id == PropertyId.AnimationPlayState).Value
            .Should().Be(new CssKeyword("paused"));
    }

    // §3 — shorthand with direction reverse.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_reverse_direction_expands()
    {
        var decls = Expand("animation: slide 1s reverse");
        decls.Single(d => d.Id == PropertyId.AnimationDirection).Value
            .Should().Be(new CssKeyword("reverse"));
    }

    // §3 — shorthand with iteration count number 3.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_iteration_count_number_expands()
    {
        var decls = Expand("animation: slide 1s 3");
        decls.Single(d => d.Id == PropertyId.AnimationIterationCount).Value
            .Should().BeOfType<CssNumber>().Which.Value.Should().Be(3);
    }

    // §3 — shorthand omitted values default to initial.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_omitted_values_default_to_initial()
    {
        var decls = Expand("animation: slide 1s");
        decls.Single(d => d.Id == PropertyId.AnimationDirection).Value
            .Should().Be(new CssKeyword("normal"));
        decls.Single(d => d.Id == PropertyId.AnimationPlayState).Value
            .Should().Be(new CssKeyword("running"));
        decls.Single(d => d.Id == PropertyId.AnimationTimingFunction).Value
            .Should().Be(new CssKeyword("ease"));
        decls.Single(d => d.Id == PropertyId.AnimationIterationCount).Value
            .Should().Be(new CssNumber(1));
    }

    // §3 — comma-separated multi-layer animation shorthand.
    // The impl splits on commas; both layers are captured in a CssValueList.
    [Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/#animation", section: "3")]
    [SpecFact]
    public void Animation_shorthand_comma_separated_multi_layer_captures_both_names()
    {
        var decls = Expand("animation: fade 1s, slide 2s");
        var nameDecl = decls.Single(d => d.Id == PropertyId.AnimationName);
        // Two layers → CssValueList with two entries.
        var list = nameDecl.Value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssKeyword("fade"));
        list.Values[1].Should().Be(new CssKeyword("slide"));
    }
}
