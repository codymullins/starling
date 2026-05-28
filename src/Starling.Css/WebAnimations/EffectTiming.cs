using Starling.Css.Animations;

namespace Starling.Css.WebAnimations;

/// <summary>
/// Input timing parameters for a Web Animations effect, per
/// Web Animations 1 §4.1
/// (https://www.w3.org/TR/web-animations-1/#the-effecttiming-dictionaries).
/// All time values are in milliseconds.
/// </summary>
public sealed record EffectTiming
{
    /// <summary>
    /// Delay before the active phase begins, in milliseconds.
    /// Web Animations 1 §4.1 — <c>delay</c>, default 0.
    /// </summary>
    public double Delay { get; init; } = 0;

    /// <summary>
    /// Delay after the active phase ends, in milliseconds.
    /// Web Animations 1 §4.1 — <c>endDelay</c>, default 0.
    /// </summary>
    public double EndDelay { get; init; } = 0;

    /// <summary>
    /// Duration of a single iteration, in milliseconds.
    /// Web Animations 1 §4.1 — <c>duration</c>, default 0.
    /// </summary>
    public double Duration { get; init; } = 0;

    /// <summary>
    /// Offset into the first iteration at which the effect begins,
    /// expressed as a fraction in [0, 1).
    /// Web Animations 1 §4.1 — <c>iterationStart</c>, default 0.
    /// </summary>
    public double IterationStart { get; init; } = 0;

    /// <summary>
    /// Number of times the effect repeats. May be
    /// <see cref="double.PositiveInfinity"/> for an infinite loop.
    /// Web Animations 1 §4.1 — <c>iterations</c>, default 1.
    /// </summary>
    public double Iterations { get; init; } = 1;

    /// <summary>
    /// Rate at which the animation clock advances relative to document time.
    /// Web Animations 1 §4.1 — <c>playbackRate</c>, default 1.
    /// </summary>
    public double PlaybackRate { get; init; } = 1;

    /// <summary>
    /// Direction in which successive iterations play.
    /// Web Animations 1 §4.1 — <c>direction</c>, default <c>normal</c>.
    /// </summary>
    public PlaybackDirection Direction { get; init; } = PlaybackDirection.Normal;

    /// <summary>
    /// Whether the effect is applied outside the active interval.
    /// Web Animations 1 §4.1 — <c>fill</c>, default <c>none</c>.
    /// </summary>
    public FillMode Fill { get; init; } = FillMode.None;

    /// <summary>
    /// Easing applied to the overall iteration progress before it is used
    /// for interpolation. Defaults to <see cref="TimingFunction.Linear"/>.
    /// Web Animations 1 §4.8.5 — effect easing.
    /// </summary>
    public TimingFunction Easing { get; init; } = TimingFunction.Linear;
}

/// <summary>
/// Playback direction values.
/// Web Animations 1 §4.1 — <c>PlaybackDirection</c> enumeration.
/// </summary>
public enum PlaybackDirection
{
    /// <summary>Each iteration plays forward.</summary>
    Normal,
    /// <summary>Each iteration plays in reverse.</summary>
    Reverse,
    /// <summary>Even iterations play forward; odd iterations play in reverse.</summary>
    Alternate,
    /// <summary>Even iterations play in reverse; odd iterations play forward.</summary>
    AlternateReverse,
}

/// <summary>
/// Fill-mode values that control whether the effect is applied outside
/// its active interval.
/// Web Animations 1 §4.1 — <c>FillMode</c> enumeration.
/// </summary>
public enum FillMode
{
    /// <summary>No fill: the effect is not applied outside the active interval.</summary>
    None,
    /// <summary>The effect persists after the active interval ends.</summary>
    Forwards,
    /// <summary>The effect is applied before the active interval starts.</summary>
    Backwards,
    /// <summary>The effect is applied both before and after the active interval.</summary>
    Both,
}
