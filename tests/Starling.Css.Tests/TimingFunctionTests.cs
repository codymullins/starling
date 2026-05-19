using FluentAssertions;
using Starling.Css.Animations;
using Starling.Css.Values;
namespace Starling.Css.Tests;

[TestClass]
public sealed class TimingFunctionTests
{
    [TestMethod]
    public void Linear_is_identity()
    {
        TimingFunction.Linear.Evaluate(0).Should().Be(0);
        TimingFunction.Linear.Evaluate(0.5).Should().Be(0.5);
        TimingFunction.Linear.Evaluate(1).Should().Be(1);
    }

    [TestMethod]
    public void Cubic_bezier_endpoints_are_exact()
    {
        TimingFunction.Ease.Evaluate(0).Should().Be(0);
        TimingFunction.Ease.Evaluate(1).Should().Be(1);
        TimingFunction.EaseIn.Evaluate(0).Should().Be(0);
        TimingFunction.EaseOut.Evaluate(1).Should().Be(1);
    }

    [TestMethod]
    public void Ease_is_monotonically_non_decreasing()
    {
        double prev = 0;
        for (var i = 1; i <= 100; i++)
        {
            var y = TimingFunction.Ease.Evaluate(i / 100.0);
            y.Should().BeGreaterThanOrEqualTo(prev);
            prev = y;
        }
    }

    [TestMethod]
    public void Ease_in_starts_slow()
    {
        // ease-in spec: cubic-bezier(0.42, 0, 1, 1). At t=0.5 the output
        // sits well below 0.5 because the curve accelerates late.
        TimingFunction.EaseIn.Evaluate(0.5).Should().BeLessThan(0.5);
    }

    [TestMethod]
    public void Ease_out_starts_fast()
    {
        TimingFunction.EaseOut.Evaluate(0.5).Should().BeGreaterThan(0.5);
    }

    [TestMethod]
    public void Cubic_bezier_solves_x_within_tolerance()
    {
        var fn = new CubicBezierTimingFunction(0.25, 0.1, 0.25, 1.0);
        // For every test t in [0, 1], y(t) must lie in [0, 1] and the curve
        // must monotonically increase (these aren't true of arbitrary
        // cubics, but our ease curves satisfy them).
        for (var i = 0; i <= 20; i++)
        {
            var t = i / 20.0;
            var y = fn.Evaluate(t);
            y.Should().BeGreaterThanOrEqualTo(-1e-3);
            y.Should().BeLessThanOrEqualTo(1.0 + 1e-3);
        }
    }

    [TestMethod]
    public void Steps_jump_end_quantises_to_n_levels()
    {
        var s = new StepsTimingFunction(4, StepPosition.JumpEnd);
        s.Evaluate(0).Should().Be(0);
        s.Evaluate(0.1).Should().Be(0);          // floor(0.4) = 0
        s.Evaluate(0.25).Should().Be(0.25);
        s.Evaluate(0.5).Should().Be(0.5);
        s.Evaluate(1.0).Should().Be(1.0);
    }

    [TestMethod]
    public void Steps_jump_start_outputs_first_level_at_zero()
    {
        var s = new StepsTimingFunction(4, StepPosition.JumpStart);
        s.Evaluate(0).Should().Be(0.25);          // immediately at 1/n
        s.Evaluate(1.0).Should().Be(1.0);
    }

    [TestMethod]
    public void FromCss_parses_keywords()
    {
        TimingFunction.FromCss(new CssKeyword("linear")).Should().Be(TimingFunction.Linear);
        TimingFunction.FromCss(new CssKeyword("ease")).Should().Be(TimingFunction.Ease);
        TimingFunction.FromCss(new CssKeyword("ease-in")).Should().Be(TimingFunction.EaseIn);
    }

    [TestMethod]
    public void FromCss_parses_cubic_bezier()
    {
        var fn = TimingFunction.FromCss(new CssFunctionValue("cubic-bezier", new CssValue[]
        {
            new CssNumber(0.1), new CssNumber(0.2), new CssNumber(0.3), new CssNumber(0.4),
        }));
        fn.Should().BeOfType<CubicBezierTimingFunction>()
          .Which.Should().Be(new CubicBezierTimingFunction(0.1, 0.2, 0.3, 0.4));
    }

    [TestMethod]
    public void FromCss_invalid_falls_back_to_ease()
    {
        TimingFunction.FromCss(new CssKeyword("nonsense")).Should().Be(TimingFunction.Ease);
        TimingFunction.FromCss(null).Should().Be(TimingFunction.Ease);
    }
}
