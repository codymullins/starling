using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;

namespace Tessera.Css.Animations;

/// <summary>
/// CSS Animations 1 runtime — drives `@keyframes`-based property animation
/// (the long-running, declaratively defined kind, as opposed to
/// <c>transition</c>'s implicit-on-change kind handled by
/// <see cref="TransitionEngine"/>).
/// <para>
/// The cascade calls <see cref="OnAnimationsCascaded"/> after computing
/// the element's <c>animation-*</c> properties. The engine diffs the new
/// declaration set against the previously-active animations for the
/// element: new names start, dropped names stop, and existing names
/// update their parameters in place (per CSS Animations 1 §6 — only the
/// timing properties refresh, not the playback position). The frame
/// driver calls <see cref="Tick"/> each animation frame; the cascade
/// reads sampled property values back via <see cref="GetEffective"/>.
/// </para>
/// </summary>
public sealed class AnimationEngine
{
    private readonly Dictionary<Element, List<AnimationInstance>> _active
        = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, KeyframesRule> _keyframes
        = new(StringComparer.Ordinal);
    private double _nowMs;

    /// <summary>Current monotonic clock — exposed for diagnostics and tests.</summary>
    public double NowMs => _nowMs;

    /// <summary>Total in-flight animation instances across all elements; for tests.</summary>
    public int ActiveCount
    {
        get
        {
            var n = 0;
            foreach (var list in _active.Values) n += list.Count;
            return n;
        }
    }

    /// <summary>
    /// Register (or replace) a parsed <c>@keyframes</c> rule by name. The
    /// most recent registration wins per spec — later <c>@keyframes</c> in
    /// the cascade replace earlier ones with the same name.
    /// </summary>
    public void RegisterKeyframes(KeyframesRule rule)
        => _keyframes[rule.Name] = rule;

    /// <summary>Clear all registered keyframes (e.g. between stylesheet swaps).</summary>
    public void ClearKeyframes() => _keyframes.Clear();

    /// <summary>True if a <c>@keyframes</c> rule with the given name is
    /// currently registered. Mostly useful for tests + diagnostics.</summary>
    public bool HasKeyframes(string name) => _keyframes.ContainsKey(name);

    /// <summary>Look up a registered <c>@keyframes</c> rule by name; returns
    /// <c>null</c> when no rule by that name exists.</summary>
    public KeyframesRule? GetKeyframes(string name)
        => _keyframes.TryGetValue(name, out var rule) ? rule : null;

    /// <summary>
    /// Diff the element's previously-active animation list against the
    /// newly cascaded <c>animation-name</c> list and start / update / stop
    /// instances accordingly. Pass <c>null</c> or an empty list to stop all
    /// animations on the element.
    /// </summary>
    public void OnAnimationsCascaded(
        Element element,
        IReadOnlyList<AnimationDeclaration>? declarations)
    {
        if (declarations is null || declarations.Count == 0)
        {
            _active.Remove(element);
            return;
        }

        if (!_active.TryGetValue(element, out var list))
        {
            list = new List<AnimationInstance>();
            _active[element] = list;
        }

        // Match-by-name across the existing list so a re-cascade that
        // changes only the duration / direction doesn't reset playback.
        // Per spec, a re-cascade that includes the same name on the same
        // element keeps the instance ticking from its current StartMs.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rebuilt = new List<AnimationInstance>(declarations.Count);
        foreach (var d in declarations)
        {
            if (string.IsNullOrEmpty(d.Name) || d.Name == "none") continue;
            if (!seen.Add(d.Name)) continue; // duplicates use the first occurrence

            var existing = list.FirstOrDefault(a => a.Name == d.Name);
            if (existing is not null)
            {
                existing.Update(d);
                rebuilt.Add(existing);
            }
            else
            {
                rebuilt.Add(new AnimationInstance(element, d, _nowMs));
            }
        }
        if (rebuilt.Count == 0) _active.Remove(element);
        else _active[element] = rebuilt;
    }

    /// <summary>Returns the current sampled value for <paramref name="property"/>, or null if no animation affects it.</summary>
    public CssValue? GetEffective(Element element, PropertyId property)
    {
        if (!_active.TryGetValue(element, out var list)) return null;
        // Later-listed animations override earlier ones for the same
        // property, per CSS Animations 1 §6.
        CssValue? winning = null;
        foreach (var inst in list)
        {
            if (!_keyframes.TryGetValue(inst.Name, out var rule)) continue;
            var sample = inst.Sample(property, rule, _nowMs);
            if (sample is not null) winning = sample;
        }
        return winning;
    }

    /// <summary>
    /// Enumerate the distinct <see cref="PropertyId"/>s currently targeted by
    /// any active animation on <paramref name="element"/>. Returns an empty
    /// sequence if the element has no active animations or none of their
    /// keyframes touch a recognised property.
    /// </summary>
    public IEnumerable<PropertyId> ActiveProperties(Element element)
    {
        if (!_active.TryGetValue(element, out var list)) yield break;
        var seen = new HashSet<PropertyId>();
        foreach (var inst in list)
        {
            if (!_keyframes.TryGetValue(inst.Name, out var rule)) continue;
            foreach (var frame in rule.Frames)
                foreach (var decl in frame.Declarations)
                    if (PropertyRegistry.TryGetPropertyId(decl.Property, out var id) && seen.Add(id))
                        yield return id;
        }
    }

    /// <summary>Advance the engine clock. Returns the number of animations that
    /// completed (entered a non-replaying terminal state) during this tick.</summary>
    public int Tick(double nowMs)
    {
        if (nowMs < _nowMs) nowMs = _nowMs; // clamp on time-going-backwards
        _nowMs = nowMs;
        var completed = 0;
        foreach (var list in _active.Values)
            foreach (var inst in list)
                if (inst.NoteTick(nowMs)) completed++;
        return completed;
    }

    /// <summary>Forget all animation state for <paramref name="element"/> (detached element cleanup).</summary>
    public void Forget(Element element) => _active.Remove(element);

    /// <summary>Reset to empty state — for tests.</summary>
    public void Reset()
    {
        _active.Clear();
        _keyframes.Clear();
        _nowMs = 0;
    }
}

/// <summary>
/// Snapshot of the <c>animation-*</c> cascade for a single layer
/// (one entry in a comma-separated animation shorthand). The engine binds
/// one of these to a matching <see cref="KeyframesRule"/>.
/// </summary>
public sealed record AnimationDeclaration(
    string Name,
    double DurationMs,
    double DelayMs,
    TimingFunction TimingFunction,
    double IterationCount,
    AnimationDirection Direction,
    AnimationFillMode FillMode,
    AnimationPlayState PlayState);

public enum AnimationDirection { Normal, Reverse, Alternate, AlternateReverse }
public enum AnimationFillMode { None, Forwards, Backwards, Both }
public enum AnimationPlayState { Running, Paused }

/// <summary>
/// Mutable, per-element-per-animation playback state. Holds enough
/// timing context to sample the right keyframe pair at any clock value.
/// </summary>
public sealed class AnimationInstance
{
    private AnimationDeclaration _decl;
    private double _startMs;
    private bool _completed;
    // When paused, we accumulate the elapsed time the playback head reached
    // at pause time; on resume, StartMs is shifted forward by the pause
    // duration so Sample() reads the same offset.
    private double _pausedAtElapsedMs = -1;

    public AnimationInstance(Element element, AnimationDeclaration decl, double nowMs)
    {
        Element = element;
        _decl = decl;
        _startMs = nowMs;
    }

    public Element Element { get; }
    public string Name => _decl.Name;
    public double StartMs => _startMs;

    /// <summary>Update timing parameters without resetting playback (re-cascade case).</summary>
    public void Update(AnimationDeclaration decl)
    {
        _decl = decl;
        if (decl.PlayState == AnimationPlayState.Paused && _pausedAtElapsedMs < 0)
        {
            // Newly paused: freeze the elapsed offset.
            _pausedAtElapsedMs = Math.Max(0, _nowSeenMs - _startMs - _decl.DelayMs);
        }
        else if (decl.PlayState == AnimationPlayState.Running && _pausedAtElapsedMs >= 0)
        {
            // Resumed: shift start so the next sample reads the same elapsed.
            _startMs = _nowSeenMs - _decl.DelayMs - _pausedAtElapsedMs;
            _pausedAtElapsedMs = -1;
        }
    }

    private double _nowSeenMs;

    /// <summary>
    /// Called by the engine each tick so the instance can update its
    /// internal "last seen clock" (needed by pause logic on later Updates)
    /// and notice when it's reached the end. Returns true on the tick the
    /// instance transitions to its terminal state.
    /// </summary>
    public bool NoteTick(double nowMs)
    {
        _nowSeenMs = nowMs;
        if (_completed) return false;
        if (_decl.PlayState == AnimationPlayState.Paused) return false;
        if (double.IsInfinity(_decl.IterationCount) || _decl.IterationCount <= 0) return false;
        var total = _decl.DelayMs + _decl.DurationMs * _decl.IterationCount;
        if (nowMs - _startMs >= total)
        {
            _completed = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sample the property at <paramref name="nowMs"/> against the given
    /// rule. Returns null if the property is not animated by any of the
    /// rule's keyframes, the animation is in its before-active window with
    /// fill-mode that doesn't backfill, or after-active without
    /// forwards-fill.
    /// </summary>
    public CssValue? Sample(PropertyId property, KeyframesRule rule, double nowMs)
    {
        _nowSeenMs = nowMs;

        // Compute "iteration progress" p ∈ [0, 1] for this clock value,
        // honouring delay, iteration count, direction, and fill mode.
        var elapsed = nowMs - _startMs;
        var beforeActive = elapsed < _decl.DelayMs;
        elapsed -= _decl.DelayMs;

        var duration = _decl.DurationMs;
        if (duration <= 0)
        {
            // Spec §3.1: a duration of 0 makes the animation immediately
            // jump to its end state. We surface the keyframe at offset 1
            // (or 0, depending on direction) without further interpolation.
            return SampleAtProgress(property, rule, _decl.Direction is AnimationDirection.Reverse or AnimationDirection.AlternateReverse ? 0 : 1);
        }

        if (_pausedAtElapsedMs >= 0) elapsed = _pausedAtElapsedMs;

        double progress;
        var iterations = _decl.IterationCount;
        var afterActive = !double.IsInfinity(iterations) && elapsed >= duration * iterations;
        if (beforeActive)
        {
            // Backfill: at progress=0 only if backwards/both fill mode is set.
            if (_decl.FillMode is AnimationFillMode.Backwards or AnimationFillMode.Both)
                progress = StartProgress();
            else
                return null;
        }
        else if (afterActive)
        {
            if (_decl.FillMode is AnimationFillMode.Forwards or AnimationFillMode.Both)
                progress = EndProgress();
            else
                return null;
        }
        else
        {
            var iter = elapsed / duration;
            var iterIndex = (int)Math.Floor(iter);
            var iterFraction = iter - iterIndex;
            // Direction handling: in alternate modes, odd iterations play
            // in reverse. AlternateReverse starts in reverse on iter 0.
            var reverse = _decl.Direction switch
            {
                AnimationDirection.Normal => false,
                AnimationDirection.Reverse => true,
                AnimationDirection.Alternate => (iterIndex & 1) == 1,
                AnimationDirection.AlternateReverse => (iterIndex & 1) == 0,
                _ => false,
            };
            progress = reverse ? 1.0 - iterFraction : iterFraction;
        }

        return SampleAtProgress(property, rule, progress);
    }

    private double StartProgress() => _decl.Direction is AnimationDirection.Reverse or AnimationDirection.AlternateReverse ? 1 : 0;
    private double EndProgress()
    {
        // When the final iteration of an `alternate` animation runs in
        // reverse, the "end progress" is 0; otherwise 1.
        if (double.IsInfinity(_decl.IterationCount)) return 1;
        var lastIterIndex = (int)Math.Floor(Math.Max(0, _decl.IterationCount - 1));
        var endsReversed = _decl.Direction switch
        {
            AnimationDirection.Reverse => true,
            AnimationDirection.Alternate => (lastIterIndex & 1) == 1,
            AnimationDirection.AlternateReverse => (lastIterIndex & 1) == 0,
            _ => false,
        };
        return endsReversed ? 0 : 1;
    }

    private CssValue? SampleAtProgress(PropertyId property, KeyframesRule rule, double progress)
    {
        // Bracket by the raw iteration progress. CSS Animations 1 §7.1 says
        // the per-keyframe timing function (and, as a fallback, the
        // animation-level function) applies to the segment that starts at
        // the "before" keyframe — so we shouldn't pre-ease the iteration
        // progress before bracketing.
        progress = Math.Clamp(progress, 0, 1);

        // Find the bracketing keyframes for this property. Frames are
        // already sorted by offset (KeyframesParser stable-sorts on parse).
        // We need the property-bearing frames only — a keyframe without
        // the property contributes nothing to the segment for it.
        var propertyName = PropertyName(property);
        KeyframeDeclaration? before = null;
        double beforeOffset = 0;
        TimingFunction? beforeSegmentTiming = null;
        KeyframeDeclaration? after = null;
        double afterOffset = 1;
        foreach (var frame in rule.Frames)
        {
            var decl = FindDecl(frame.Declarations, propertyName);
            if (decl is null) continue;
            if (frame.Offset <= progress)
            {
                before = decl;
                beforeOffset = frame.Offset;
                beforeSegmentTiming = frame.SegmentTimingFunction;
            }
            if (frame.Offset >= progress) { after = decl; afterOffset = frame.Offset; break; }
        }
        if (before is null && after is null) return null;
        if (before is null) { before = after; beforeOffset = afterOffset; }
        if (after is null) { after = before; afterOffset = beforeOffset; }

        var span = afterOffset - beforeOffset;
        if (span <= 1e-9) return before!.Value;
        var segmentP = (progress - beforeOffset) / span;

        // §7.1: per-keyframe timing function on the *before* keyframe wins;
        // otherwise fall back to the animation-level function.
        var timing = beforeSegmentTiming ?? _decl.TimingFunction;
        segmentP = timing.Evaluate(Math.Clamp(segmentP, 0, 1));

        return Interpolator.Interpolate(property, before!.Value, after!.Value, segmentP);
    }

    private static KeyframeDeclaration? FindDecl(IReadOnlyList<KeyframeDeclaration> decls, string property)
    {
        for (var i = 0; i < decls.Count; i++)
            if (string.Equals(decls[i].Property, property, StringComparison.OrdinalIgnoreCase))
                return decls[i];
        return null;
    }

    private static string PropertyName(PropertyId id)
    {
        // PascalCase → kebab-case (same transformation TransitionEngine uses).
        var s = id.ToString();
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
