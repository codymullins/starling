using Starling.Dom;

// Lives in the engine-neutral seam project but keeps the Starling.Bindings
// namespace (like ILayoutHost) so both JS backends and the engine reach it
// without referencing each other. Depends only on Starling.Dom — the keyframe
// and timing payloads are neutral records, so the seam never sees a CSS type.
namespace Starling.Bindings;

/// <summary>
/// Pluggable host for the Web Animations API (<c>element.animate</c>). The JS
/// binding builds neutral keyframe + timing data and calls
/// <see cref="Animate"/>; the engine translates it into its CSS animation model,
/// persists it per-document, and renders it through the same compositor the
/// declarative <c>@keyframes</c> path uses. Playback control + readback go
/// through the returned integer handle. When no host is installed (bare unit
/// tests), the binding still exposes a spec-shaped <c>Animation</c> object whose
/// control calls are no-ops.
/// </summary>
public interface IAnimationHost
{
    /// <summary>Register a script animation on <paramref name="element"/> and
    /// return its handle. The start time is the host's current timeline value.</summary>
    int Animate(Element element, IReadOnlyList<AnimationKeyframeSpec> keyframes, AnimationEffectTimingSpec timing);

    void Play(int id);
    void Pause(int id);
    void Cancel(int id);
    void Finish(int id);

    /// <summary>currentTime (ms) for the animation, on the document timeline.</summary>
    double CurrentTime(int id);
    void SetCurrentTime(int id, double ms);

    /// <summary>Animation play state: "idle" | "running" | "paused" | "finished".</summary>
    string PlayState(int id);

    /// <summary>Current document-timeline value (ms) — backs
    /// <c>Animation.startTime</c>/<c>timeline.currentTime</c>.</summary>
    double TimelineNow { get; }
}

/// <summary>One keyframe: an offset in [0,1] plus property/value declarations
/// (kebab-case property name → CSS text). Neutral payload for the seam.</summary>
public sealed record AnimationKeyframeSpec(double Offset, IReadOnlyList<KeyValuePair<string, string>> Declarations);

/// <summary>Neutral timing payload mirroring the WAAPI <c>EffectTiming</c>
/// subset the engine consumes. Direction/Fill/Easing are CSS keyword strings
/// the engine maps to its enums.</summary>
public sealed record AnimationEffectTimingSpec(
    double DurationMs,
    double DelayMs,
    double Iterations,
    string Direction,
    string Fill,
    string Easing);
