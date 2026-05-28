namespace Starling.Css.WebAnimations;

/// <summary>
/// The phase an animation effect occupies at a given local time.
/// Web Animations 1 §4.5
/// (https://www.w3.org/TR/web-animations-1/#animation-effect-phases-and-states).
/// </summary>
public enum AnimationPhase
{
    /// <summary>
    /// Local time is null or outside the range covered by fill modes.
    /// The effect produces no output.
    /// </summary>
    Idle,
    /// <summary>
    /// Local time is before the active interval and a backwards (or both)
    /// fill is in effect, or we are simply before the start of the active interval.
    /// </summary>
    Before,
    /// <summary>Local time falls within the active interval.</summary>
    Active,
    /// <summary>Local time is past the active interval.</summary>
    After,
}

/// <summary>
/// The resolved output of the Web Animations timing algorithm for a single
/// local-time sample.
/// Web Animations 1 §4.5–§4.8
/// (https://www.w3.org/TR/web-animations-1/#calculating-the-transformed-progress).
/// </summary>
/// <param name="Phase">
/// The phase the effect occupies at the sampled time.
/// </param>
/// <param name="CurrentIteration">
/// The zero-based index of the current (or last completed) iteration.
/// Null when <see cref="Phase"/> is <see cref="AnimationPhase.Idle"/> and
/// no fill applies.
/// </param>
/// <param name="Progress">
/// The eased iteration progress in [0, 1] that an effect should use for
/// interpolation.  Null when the effect has no output (idle with no fill).
/// </param>
public sealed record ComputedTiming(
    AnimationPhase Phase,
    int? CurrentIteration,
    double? Progress);
