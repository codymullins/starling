using Tessera.Css.Values;

namespace Tessera.Css.Animations;

/// <summary>
/// A parsed <c>@keyframes</c> at-rule (CSS Animations 1 §4). The animation
/// engine samples the listed <see cref="Frames"/> by playback offset (0..1)
/// to drive an element's animated properties.
/// </summary>
public sealed record KeyframesRule(
    string Name,
    IReadOnlyList<Keyframe> Frames);

/// <summary>
/// A single keyframe — an offset in [0,1] and the declarations applied at
/// that point in the animation. The selector <c>from</c> is 0, <c>to</c> is 1,
/// and a percentage <c>X%</c> is <c>X/100</c>. A keyframe rule with multiple
/// selectors (e.g. <c>0%, 50%</c>) expands into one <see cref="Keyframe"/> per offset.
/// <para>
/// <see cref="SegmentTimingFunction"/> captures an <c>animation-timing-function</c>
/// declared inside the keyframe block. Per CSS Animations 1 §7.1 it overrides
/// the animation-level timing function for the segment *starting at this
/// keyframe*. Null means "use the animation-level function."
/// </para>
/// </summary>
public sealed record Keyframe(
    double Offset,
    IReadOnlyList<KeyframeDeclaration> Declarations,
    TimingFunction? SegmentTimingFunction = null);

/// <summary>A single property declaration within a keyframe.</summary>
public sealed record KeyframeDeclaration(string Property, CssValue Value);
