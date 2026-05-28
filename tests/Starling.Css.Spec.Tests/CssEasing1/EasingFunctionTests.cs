using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssEasing1;

/// <summary>
/// Conformance suite for
/// <see href="https://www.w3.org/TR/css-easing-1/">CSS Easing Functions Level 1</see>.
/// Tracking work package: <c>wp:spec-css-easing-1</c>.
/// </summary>
[TestClass]
[Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/")]
public sealed class EasingFunctionTests
{
    // Tolerance used throughout for floating-point comparisons.
    private const double Eps = 1e-4;

    // -------------------------------------------------------------------------
    // §2 — The cubic-bezier() easing function
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier(P1x, P1y, P2x, P2y)</c> with control
    /// points <c>(0,0)</c>, <c>(P1x,P1y)</c>, <c>(P2x,P2y)</c>, <c>(1,1)</c>
    /// must pass through the origin at t=0.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_t0_yields_0()
    {
        var fn = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        fn.Evaluate(0.0).Should().BeApproximately(0.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier</c> must pass through (1,1) at t=1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_t1_yields_1()
    {
        var fn = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c> (the <c>ease</c>
    /// curve) evaluated at t=0.5 should be strictly between 0 and 1 and close
    /// to ~0.73 (browser reference value).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_ease_midpoint_is_in_range()
    {
        var fn = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        var y = fn.Evaluate(0.5);
        y.Should().BeGreaterThan(0.0);
        y.Should().BeLessThan(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c> must be
    /// monotonically non-decreasing on [0, 1] for a valid (monotonic) curve.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_ease_is_monotonic()
    {
        var fn = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        double prev = 0.0;
        for (var i = 1; i <= 100; i++)
        {
            var y = fn.Evaluate(i / 100.0);
            y.Should().BeGreaterThanOrEqualTo(prev - Eps);
            prev = y;
        }
    }

    /// <summary>
    /// CSS Easing 1 §2: the identity cubic-bezier <c>(0, 0, 1, 1)</c> must
    /// evaluate to <c>t</c> (linear) at several sample points.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_identity_matches_linear()
    {
        var fn = new CubicBezierTimingFunction(0.0, 0.0, 1.0, 1.0);
        fn.Evaluate(0.0).Should().BeApproximately(0.0, Eps);
        fn.Evaluate(0.25).Should().BeApproximately(0.25, Eps);
        fn.Evaluate(0.5).Should().BeApproximately(0.5, Eps);
        fn.Evaluate(0.75).Should().BeApproximately(0.75, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §2: output values from <c>cubic-bezier</c> must lie in
    /// [0, 1] for input t in [0, 1] when the curve control points keep Y in
    /// [0, 1].
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void CubicBezier_output_bounded_for_standard_curves()
    {
        TimingFunction[] curves =
        [
            new CubicBezierTimingFunction(0.42, 0.0, 1.0, 1.0),   // ease-in
            new CubicBezierTimingFunction(0.0, 0.0, 0.58, 1.0),    // ease-out
            new CubicBezierTimingFunction(0.42, 0.0, 0.58, 1.0),   // ease-in-out
        ];
        foreach (var fn in curves)
        {
            for (var i = 0; i <= 20; i++)
            {
                var t = i / 20.0;
                var y = fn.Evaluate(t);
                y.Should().BeGreaterThanOrEqualTo(0.0 - Eps);
                y.Should().BeLessThanOrEqualTo(1.0 + Eps);
            }
        }
    }

    // -------------------------------------------------------------------------
    // §2 — Keyword aliases map to their canonical cubic-bezier definitions
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §2: the <c>ease</c> keyword must alias
    /// <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease", "§2")]
    [SpecFact]
    public void Keyword_ease_aliases_cubic_bezier()
    {
        TimingFunction.Ease.Should().Be(new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: the <c>ease-in</c> keyword must alias
    /// <c>cubic-bezier(0.42, 0, 1, 1)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in", "§2")]
    [SpecFact]
    public void Keyword_ease_in_aliases_cubic_bezier()
    {
        TimingFunction.EaseIn.Should().Be(new CubicBezierTimingFunction(0.42, 0.0, 1.0, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: the <c>ease-out</c> keyword must alias
    /// <c>cubic-bezier(0, 0, 0.58, 1)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-out", "§2")]
    [SpecFact]
    public void Keyword_ease_out_aliases_cubic_bezier()
    {
        TimingFunction.EaseOut.Should().Be(new CubicBezierTimingFunction(0.0, 0.0, 0.58, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: the <c>ease-in-out</c> keyword must alias
    /// <c>cubic-bezier(0.42, 0, 0.58, 1)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in-out", "§2")]
    [SpecFact]
    public void Keyword_ease_in_out_aliases_cubic_bezier()
    {
        TimingFunction.EaseInOut.Should().Be(new CubicBezierTimingFunction(0.42, 0.0, 0.58, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>ease-in</c> starts slow — output at t=0.5 is
    /// below 0.5 because the curve accelerates toward the end.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in", "§2")]
    [SpecFact]
    public void EaseIn_starts_slow()
    {
        TimingFunction.EaseIn.Evaluate(0.5).Should().BeLessThan(0.5);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>ease-out</c> starts fast — output at t=0.5 is
    /// above 0.5 because the curve decelerates toward the end.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-out", "§2")]
    [SpecFact]
    public void EaseOut_starts_fast()
    {
        TimingFunction.EaseOut.Evaluate(0.5).Should().BeGreaterThan(0.5);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>ease-in-out</c> is symmetric about (0.5, 0.5), so
    /// Evaluate(0.5) ≈ 0.5 and Evaluate(0.25) + Evaluate(0.75) ≈ 1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in-out", "§2")]
    [SpecFact]
    public void EaseInOut_is_symmetric()
    {
        var fn = TimingFunction.EaseInOut;
        fn.Evaluate(0.5).Should().BeApproximately(0.5, 0.01);
        var lo = fn.Evaluate(0.25);
        var hi = fn.Evaluate(0.75);
        (lo + hi).Should().BeApproximately(1.0, 0.01);
    }

    // -------------------------------------------------------------------------
    // §2 — FromCss parses cubic-bezier() and keyword aliases
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §2: <c>FromCss</c> with keyword <c>ease</c> returns
    /// <see cref="TimingFunction.Ease"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease", "§2")]
    [SpecFact]
    public void FromCss_keyword_ease_parses()
    {
        TimingFunction.FromCss(new CssKeyword("ease")).Should().Be(TimingFunction.Ease);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>FromCss</c> with keyword <c>ease-in</c> returns
    /// <see cref="TimingFunction.EaseIn"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in", "§2")]
    [SpecFact]
    public void FromCss_keyword_ease_in_parses()
    {
        TimingFunction.FromCss(new CssKeyword("ease-in")).Should().Be(TimingFunction.EaseIn);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>FromCss</c> with keyword <c>ease-out</c> returns
    /// <see cref="TimingFunction.EaseOut"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-out", "§2")]
    [SpecFact]
    public void FromCss_keyword_ease_out_parses()
    {
        TimingFunction.FromCss(new CssKeyword("ease-out")).Should().Be(TimingFunction.EaseOut);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>FromCss</c> with keyword <c>ease-in-out</c> returns
    /// <see cref="TimingFunction.EaseInOut"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-ease-in-out", "§2")]
    [SpecFact]
    public void FromCss_keyword_ease_in_out_parses()
    {
        TimingFunction.FromCss(new CssKeyword("ease-in-out")).Should().Be(TimingFunction.EaseInOut);
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>FromCss</c> parses <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>
    /// and produces a <see cref="CubicBezierTimingFunction"/> with the correct parameters.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void FromCss_cubic_bezier_function_parses()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("cubic-bezier",
        [
            new CssNumber(0.25), new CssNumber(0.1), new CssNumber(0.25), new CssNumber(1.0),
        ]));
        fn.Should().Be(new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier</c> with x-values outside [0, 1] should
    /// clamp x1/x2 to keep the X monotonic (implementation-defined clamp behaviour
    /// per spec note; the impl clamps rather than rejects).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void FromCss_cubic_bezier_clamps_x_out_of_range()
    {
        // x1 = -0.5 should be clamped to 0.0; x2 = 1.5 should be clamped to 1.0.
        var fn = TimingFunction.FromCss(new CssFunctionValue("cubic-bezier",
        [
            new CssNumber(-0.5), new CssNumber(0.0), new CssNumber(1.5), new CssNumber(1.0),
        ]));
        fn.Should().BeOfType<CubicBezierTimingFunction>()
          .Which.Should().Be(new CubicBezierTimingFunction(0.0, 0.0, 1.0, 1.0));
    }

    /// <summary>
    /// CSS Easing 1 §2: <c>cubic-bezier</c> with too few arguments falls back to
    /// <c>ease</c> (no crash; graceful fallback).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#cubic-bezier-easing-functions", "§2")]
    [SpecFact]
    public void FromCss_cubic_bezier_wrong_arity_falls_back_to_ease()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("cubic-bezier",
        [
            new CssNumber(0.25), new CssNumber(0.1),
        ]));
        fn.Should().Be(TimingFunction.Ease);
    }

    // -------------------------------------------------------------------------
    // §3 — The linear easing function (keyword)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §3 (keyword linear): the <c>linear</c> keyword must produce
    /// an identity mapping — <c>Evaluate(t) == t</c> for all t in [0, 1].
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-linear", "§3")]
    [SpecFact]
    public void Keyword_linear_is_identity()
    {
        var fn = TimingFunction.Linear;
        fn.Evaluate(0.0).Should().Be(0.0);
        fn.Evaluate(0.25).Should().Be(0.25);
        fn.Evaluate(0.5).Should().Be(0.5);
        fn.Evaluate(0.75).Should().Be(0.75);
        fn.Evaluate(1.0).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §3: <c>FromCss</c> with keyword <c>linear</c> returns
    /// the singleton <see cref="TimingFunction.Linear"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-easing-function-linear", "§3")]
    [SpecFact]
    public void FromCss_keyword_linear_parses()
    {
        TimingFunction.FromCss(new CssKeyword("linear")).Should().Be(TimingFunction.Linear);
    }

    // -------------------------------------------------------------------------
    // §4 — The linear() easing function (multi-stop)
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §4: <c>linear(0, 1)</c> — the simplest two-stop form —
    /// must be parsed and behave as an identity (same as the keyword
    /// <c>linear</c>).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#linear-easing-function", "§4")]
    [SpecFact]    public void LinearFunction_two_stop_is_identity()
    {
        // linear(0, 1) → two stops: output 0 at t=0, output 1 at t=1.
        var fn = TimingFunction.FromCss(new CssFunctionValue("linear",
        [
            new CssNumber(0.0), new CssNumber(1.0),
        ]));
        // Must NOT fall back to ease.
        fn.Should().NotBe(TimingFunction.Ease);
        fn.Evaluate(0.0).Should().BeApproximately(0.0, Eps);
        fn.Evaluate(0.5).Should().BeApproximately(0.5, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §4: <c>linear(0, 0.25 50%, 1)</c> — a three-stop form with
    /// an explicit midpoint — must interpolate piecewise.
    /// At t=0.25 (halfway through the first segment [0→50%]) output ≈ 0.125.
    /// At t=0.75 (halfway through the second segment [50%→100%]) output ≈ 0.625.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#linear-easing-function", "§4")]
    [SpecFact]    public void LinearFunction_multi_stop_with_position()
    {
        // linear(0, 0.25 50%, 1)
        // Segment 1: [0%→50%] maps output [0→0.25]
        // Segment 2: [50%→100%] maps output [0.25→1]
        var fn = TimingFunction.FromCss(new CssFunctionValue("linear",
        [
            new CssNumber(0.0),
            new CssValueList([new CssNumber(0.25), new CssPercentage(50)]),
            new CssNumber(1.0),
        ]));
        fn.Should().NotBe(TimingFunction.Ease);
        fn.Evaluate(0.25).Should().BeApproximately(0.125, Eps);
        fn.Evaluate(0.75).Should().BeApproximately(0.625, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §4: <c>linear(0, 0.5, 1)</c> — evenly spaced stops —
    /// must distribute input positions uniformly (0%, 50%, 100%).
    /// So at t=0.25 output ≈ 0.25 and at t=0.75 output ≈ 0.75.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#linear-easing-function", "§4")]
    [SpecFact]    public void LinearFunction_three_evenly_spaced_stops()
    {
        // linear(0, 0.5, 1): positions auto-distributed as 0%, 50%, 100%.
        var fn = TimingFunction.FromCss(new CssFunctionValue("linear",
        [
            new CssNumber(0.0),
            new CssNumber(0.5),
            new CssNumber(1.0),
        ]));
        fn.Should().NotBe(TimingFunction.Ease);
        fn.Evaluate(0.0).Should().BeApproximately(0.0, Eps);
        fn.Evaluate(0.25).Should().BeApproximately(0.25, Eps);
        fn.Evaluate(0.5).Should().BeApproximately(0.5, Eps);
        fn.Evaluate(0.75).Should().BeApproximately(0.75, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §4: <c>linear()</c> with explicit input positions on all
    /// stops must honour those positions exactly.
    /// <c>linear(0 0%, 1 100%)</c> is the canonical two-stop identity form.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#linear-easing-function", "§4")]
    [SpecFact]    public void LinearFunction_explicit_positions_respected()
    {
        // linear(0 0%, 1 100%)
        var fn = TimingFunction.FromCss(new CssFunctionValue("linear",
        [
            new CssValueList([new CssNumber(0.0), new CssPercentage(0)]),
            new CssValueList([new CssNumber(1.0), new CssPercentage(100)]),
        ]));
        fn.Should().NotBe(TimingFunction.Ease);
        fn.Evaluate(0.0).Should().BeApproximately(0.0, Eps);
        fn.Evaluate(0.5).Should().BeApproximately(0.5, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    // -------------------------------------------------------------------------
    // §5 — The steps() easing function
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-end)</c> — the default position —
    /// at t=0 output must be 0 (the jump happens at the end of each interval).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_end_t0_yields_0()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpEnd);
        fn.Evaluate(0.0).Should().Be(0.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-end)</c> at t=1 must output 1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_end_t1_yields_1()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpEnd);
        fn.Evaluate(1.0).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-end)</c> sample points — each step
    /// covers 0.25 of the input range; the output advances at the END of
    /// each interval.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_end_sample_points()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpEnd);
        // In the first quarter: 0 ≤ t < 0.25 → output 0.
        fn.Evaluate(0.1).Should().Be(0.0);
        // Exactly at 0.25: step 1 just completed → output 0.25.
        fn.Evaluate(0.25).Should().Be(0.25);
        // In the second quarter: 0.25 ≤ t < 0.5 → output 0.25.
        fn.Evaluate(0.4).Should().Be(0.25);
        // Exactly at 0.5: step 2 just completed → output 0.5.
        fn.Evaluate(0.5).Should().Be(0.5);
        // In third quarter: 0.5 ≤ t < 0.75 → output 0.5.
        fn.Evaluate(0.6).Should().Be(0.5);
        // Exactly at 0.75: step 3 just completed → output 0.75.
        fn.Evaluate(0.75).Should().Be(0.75);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(2, jump-end)</c> — the canonical example from
    /// the spec — at t=0.25 → 0, at t=0.75 → 0.5.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_two_jump_end_spec_example()
    {
        var fn = new StepsTimingFunction(2, StepPosition.JumpEnd);
        fn.Evaluate(0.25).Should().Be(0.0);
        fn.Evaluate(0.75).Should().Be(0.5);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-start)</c> — the jump happens at the
    /// START of each interval, so at t=0 the output is already 1/n = 0.25.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_start_t0_yields_first_level()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpStart);
        fn.Evaluate(0.0).Should().Be(0.25);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-start)</c> at t=1 must output 1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_start_t1_yields_1()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpStart);
        fn.Evaluate(1.0).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-start)</c> sample points — the output
    /// jumps immediately at the start of each interval.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_start_sample_points()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpStart);
        // At t=0: immediate jump to 0.25.
        fn.Evaluate(0.0).Should().Be(0.25);
        // Just before t=0.25: still 0.25 (next jump hasn't triggered yet).
        fn.Evaluate(0.24).Should().Be(0.25);
        // At t=0.25: jumps to 0.5.
        fn.Evaluate(0.25).Should().Be(0.5);
        // At t=0.5: jumps to 0.75.
        fn.Evaluate(0.5).Should().Be(0.75);
        // At t=0.75: jumps to 1.0.
        fn.Evaluate(0.75).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-both)</c> uses n+1=5 levels (including
    /// both 0 and 1). At t=0 output is 1/5=0.2; at t=1 output is 1.0.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_both_endpoints()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpBoth);
        fn.Evaluate(0.0).Should().BeApproximately(0.2, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-both)</c> — with n+1=5 levels,
    /// each step covers 0.25 of the input; the output jumps immediately at
    /// t=0 and at each step boundary.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_both_sample_points()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpBoth);
        fn.Evaluate(0.0).Should().BeApproximately(1.0 / 5.0, Eps);
        fn.Evaluate(0.25).Should().BeApproximately(2.0 / 5.0, Eps);
        fn.Evaluate(0.5).Should().BeApproximately(3.0 / 5.0, Eps);
        fn.Evaluate(0.75).Should().BeApproximately(4.0 / 5.0, Eps);
        fn.Evaluate(1.0).Should().BeApproximately(5.0 / 5.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-none)</c> uses n-1=3 levels
    /// (skipping the jump at both 0 and 1), so the first level starts at 0
    /// and the last level reaches 1 only at t=1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_none_endpoints()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpNone);
        fn.Evaluate(0.0).Should().Be(0.0);
        fn.Evaluate(1.0).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-none)</c> sample points — with 3
    /// actual intervals of output change (divisor = n-1 = 3), each step
    /// boundary is at t = 1/4, 2/4, 3/4; the output jumps at those points.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_jump_none_sample_points()
    {
        var fn = new StepsTimingFunction(4, StepPosition.JumpNone);
        // At t=0: no jump yet → output 0.
        fn.Evaluate(0.0).Should().Be(0.0);
        // Just before first step (t=0.25): still 0.
        fn.Evaluate(0.24).Should().Be(0.0);
        // At t=0.25: first step → 1/3.
        fn.Evaluate(0.25).Should().BeApproximately(1.0 / 3.0, Eps);
        // At t=0.5: second step → 2/3.
        fn.Evaluate(0.5).Should().BeApproximately(2.0 / 3.0, Eps);
        // At t=0.75: third step → 3/3 = 1.
        fn.Evaluate(0.75).Should().BeApproximately(1.0, Eps);
        // At t=1.0: stays at 1.
        fn.Evaluate(1.0).Should().BeApproximately(1.0, Eps);
    }

    /// <summary>
    /// CSS Easing 1 §5: the legacy <c>start</c> keyword is an alias for
    /// <c>jump-start</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_legacy_start_alias_parsed()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("start"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpStart));
    }

    /// <summary>
    /// CSS Easing 1 §5: the legacy <c>end</c> keyword is an alias for
    /// <c>jump-end</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void Steps_legacy_end_alias_parsed()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("end"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpEnd));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-start)</c> must parse via
    /// <see cref="TimingFunction.FromCss"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_jump_start_parses()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("jump-start"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpStart));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-end)</c> must parse via
    /// <see cref="TimingFunction.FromCss"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_jump_end_parses()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("jump-end"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpEnd));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-both)</c> must parse via
    /// <see cref="TimingFunction.FromCss"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_jump_both_parses()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("jump-both"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpBoth));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(4, jump-none)</c> must parse via
    /// <see cref="TimingFunction.FromCss"/>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_jump_none_parses()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4), new CssKeyword("jump-none"),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpNone));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps(n)</c> with no position keyword defaults to
    /// <c>jump-end</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_no_position_defaults_to_jump_end()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssNumber(4),
        ]));
        fn.Should().Be(new StepsTimingFunction(4, StepPosition.JumpEnd));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>step-start</c> keyword must alias
    /// <c>steps(1, jump-start)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-step-easing-function-step-start", "§5")]
    [SpecFact]
    public void Keyword_step_start_aliases_steps_1_jump_start()
    {
        var fn = TimingFunction.FromCss(new CssKeyword("step-start"));
        fn.Should().Be(new StepsTimingFunction(1, StepPosition.JumpStart));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>step-end</c> keyword must alias
    /// <c>steps(1, jump-end)</c>.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-step-easing-function-step-end", "§5")]
    [SpecFact]
    public void Keyword_step_end_aliases_steps_1_jump_end()
    {
        var fn = TimingFunction.FromCss(new CssKeyword("step-end"));
        fn.Should().Be(new StepsTimingFunction(1, StepPosition.JumpEnd));
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>step-start</c> evaluates like <c>steps(1, jump-start)</c>
    /// — output is 1 at t=0.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-step-easing-function-step-start", "§5")]
    [SpecFact]
    public void Keyword_step_start_evaluates_to_1_at_t0()
    {
        var fn = TimingFunction.FromCss(new CssKeyword("step-start"));
        fn.Evaluate(0.0).Should().Be(1.0);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>step-end</c> evaluates like <c>steps(1, jump-end)</c>
    /// — output is 0 at t=0 and 1 at t=1.
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#valdef-step-easing-function-step-end", "§5")]
    [SpecFact]
    public void Keyword_step_end_evaluates_correctly()
    {
        var fn = TimingFunction.FromCss(new CssKeyword("step-end"));
        fn.Evaluate(0.0).Should().Be(0.0);
        fn.Evaluate(0.5).Should().Be(0.0);
        fn.Evaluate(1.0).Should().Be(1.0);
    }

    // -------------------------------------------------------------------------
    // Invalid input — graceful fallback
    // -------------------------------------------------------------------------

    /// <summary>
    /// CSS Easing 1 (general): an unknown keyword falls back to <c>ease</c>
    /// (no exception thrown).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/")]
    [SpecFact]
    public void FromCss_unknown_keyword_falls_back_to_ease()
    {
        TimingFunction.FromCss(new CssKeyword("not-a-valid-easing")).Should().Be(TimingFunction.Ease);
    }

    /// <summary>
    /// CSS Easing 1 (general): a null value falls back to <c>ease</c>
    /// (no exception thrown).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/")]
    [SpecFact]
    public void FromCss_null_falls_back_to_ease()
    {
        TimingFunction.FromCss(null).Should().Be(TimingFunction.Ease);
    }

    /// <summary>
    /// CSS Easing 1 §5: <c>steps</c> with a non-numeric first argument falls
    /// back to <c>ease</c> (no crash).
    /// </summary>
    [Spec("css-easing-1", "https://www.w3.org/TR/css-easing-1/#step-easing-functions", "§5")]
    [SpecFact]
    public void FromCss_steps_invalid_n_falls_back_to_ease()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("steps",
        [
            new CssKeyword("bad"),
        ]));
        fn.Should().Be(TimingFunction.Ease);
    }
}
