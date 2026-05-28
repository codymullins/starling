using AwesomeAssertions;
using Starling.Css.WebAnimations;

namespace Starling.Css.Spec.Tests.WebAnimations1;

/// <summary>
/// Timing model conformance for
/// <see href="https://www.w3.org/TR/web-animations-1/">Web Animations 1</see>
/// §4 (https://www.w3.org/TR/web-animations-1/#timing-model).
/// </summary>
[TestClass]
[Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/")]
public sealed class TimingModelTests
{
    // -------------------------------------------------------------------------
    // §4.7 / §4.8.5 — active phase, linear easing, basic progress
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.7 + §4.8.5: a 1000 ms effect at t = 500 ms with
    /// linear easing and 1 iteration produces iteration progress 0.5.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#calculating-the-simple-iteration-progress")]
    [SpecFact]
    public void Linear_easing_midpoint_yields_progress_half()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 1 };
        var result = TimingModel.ComputeProgress(timing, localTime: 500);

        result.Phase.Should().Be(AnimationPhase.Active);
        result.CurrentIteration.Should().Be(0);
        result.Progress.Should().BeApproximately(0.5, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.5 — before-phase fill modes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.5: before-phase with fill:none — the effect has
    /// no output; <c>Progress</c> must be null.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states")]
    [SpecFact]
    public void Before_phase_fill_none_yields_null_progress()
    {
        var timing = new EffectTiming { Delay = 200, Duration = 1000, Iterations = 1, Fill = FillMode.None };
        var result = TimingModel.ComputeProgress(timing, localTime: 100);

        result.Phase.Should().Be(AnimationPhase.Before);
        result.Progress.Should().BeNull();
    }

    /// <summary>
    /// Web Animations 1 §4.5: before-phase with fill:backwards — the effect
    /// holds its start value; <c>Progress</c> must be 0 (the beginning of the
    /// first iteration with linear easing).
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states")]
    [SpecFact]
    public void Before_phase_fill_backwards_yields_progress_zero()
    {
        var timing = new EffectTiming { Delay = 200, Duration = 1000, Iterations = 1, Fill = FillMode.Backwards };
        var result = TimingModel.ComputeProgress(timing, localTime: 50);

        result.Phase.Should().Be(AnimationPhase.Before);
        result.Progress.Should().BeApproximately(0.0, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.5 — after-phase fill modes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.5: after-phase with fill:forwards — the effect
    /// holds its end value; <c>Progress</c> must be 1.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states")]
    [SpecFact]
    public void After_phase_fill_forwards_yields_progress_one()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 1, Fill = FillMode.Forwards };
        var result = TimingModel.ComputeProgress(timing, localTime: 1500);

        result.Phase.Should().Be(AnimationPhase.After);
        result.Progress.Should().BeApproximately(1.0, 1e-9);
    }

    /// <summary>
    /// Web Animations 1 §4.5: after-phase with fill:none — no output.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states")]
    [SpecFact]
    public void After_phase_fill_none_yields_null_progress()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 1, Fill = FillMode.None };
        var result = TimingModel.ComputeProgress(timing, localTime: 1500);

        result.Phase.Should().Be(AnimationPhase.After);
        result.Progress.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // §4.8 — direction: reverse
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.8: direction:reverse inverts the progress.
    /// At t = 250 ms of a 1000 ms effect the raw fraction is 0.25; reversed
    /// it becomes 0.75.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#calculating-the-directed-progress")]
    [SpecFact]
    public void Direction_reverse_inverts_progress()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 1, Direction = PlaybackDirection.Reverse };
        var result = TimingModel.ComputeProgress(timing, localTime: 250);

        result.Phase.Should().Be(AnimationPhase.Active);
        result.Progress.Should().BeApproximately(0.75, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.8 — direction: alternate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.8: direction:alternate — iteration 0 (even) plays
    /// forward and iteration 1 (odd) plays in reverse.
    /// At t = 1500 ms with duration = 1000 ms the effect is in iteration 1
    /// at fractional position 0.5 within that iteration.  Because iteration 1
    /// is reversed, the directed progress should be 1 - 0.5 = 0.5; but the
    /// key is that the currentIteration index is 1 and the phase is Active.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#calculating-the-directed-progress")]
    [SpecFact]
    public void Alternate_odd_iteration_plays_in_reverse()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 2, Direction = PlaybackDirection.Alternate };

        // Iteration 0: t in [0, 1000) → forward
        var iter0Result = TimingModel.ComputeProgress(timing, localTime: 250);
        iter0Result.CurrentIteration.Should().Be(0);
        iter0Result.Progress.Should().BeApproximately(0.25, 1e-9);

        // Iteration 1: t in [1000, 2000) → reverse; at t=1250 fraction=0.25 → directed=0.75
        var iter1Result = TimingModel.ComputeProgress(timing, localTime: 1250);
        iter1Result.CurrentIteration.Should().Be(1);
        iter1Result.Progress.Should().BeApproximately(0.75, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.7 — iterations > 1: currentIteration advances
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.7: with 2 iterations, after the first iteration
    /// completes the current-iteration index must be 1.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#calculating-the-simple-iteration-progress")]
    [SpecFact]
    public void Two_iterations_currentIteration_advances_after_first_duration()
    {
        var timing = new EffectTiming { Duration = 1000, Iterations = 2 };
        var result = TimingModel.ComputeProgress(timing, localTime: 1200);

        result.Phase.Should().Be(AnimationPhase.Active);
        result.CurrentIteration.Should().Be(1);
        result.Progress.Should().BeApproximately(0.2, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.5 — delay shifts the active phase
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.5: a positive delay pushes the active interval
    /// forward.  At t = delay the effect should enter the active phase at
    /// progress 0.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states")]
    [SpecFact]
    public void Delay_shifts_active_phase_start()
    {
        var timing = new EffectTiming { Delay = 300, Duration = 1000, Iterations = 1 };

        // Just before delay: before-phase, no fill → null.
        var before = TimingModel.ComputeProgress(timing, localTime: 299);
        before.Phase.Should().Be(AnimationPhase.Before);

        // Exactly at delay: active-phase, progress 0.
        var atStart = TimingModel.ComputeProgress(timing, localTime: 300);
        atStart.Phase.Should().Be(AnimationPhase.Active);
        atStart.Progress.Should().BeApproximately(0.0, 1e-9);

        // Midway through active interval: progress 0.5.
        var mid = TimingModel.ComputeProgress(timing, localTime: 800);
        mid.Phase.Should().Be(AnimationPhase.Active);
        mid.Progress.Should().BeApproximately(0.5, 1e-9);
    }

    // -------------------------------------------------------------------------
    // §4.6 — infinite iterations never enter the after-phase
    // -------------------------------------------------------------------------

    /// <summary>
    /// Web Animations 1 §4.6: when iterations is ∞ the active duration is
    /// infinite and the effect never reaches the after-phase.
    /// </summary>
    [Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/#calculating-the-active-duration")]
    [SpecFact]
    public void Infinite_iterations_never_enter_after_phase()
    {
        var timing = new EffectTiming { Duration = 500, Iterations = double.PositiveInfinity };
        var result = TimingModel.ComputeProgress(timing, localTime: 1_000_000);

        result.Phase.Should().Be(AnimationPhase.Active);
        result.Progress.Should().NotBeNull();
    }
}
