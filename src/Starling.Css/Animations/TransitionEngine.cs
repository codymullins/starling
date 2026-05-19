using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;

namespace Tessera.Css.Animations;

/// <summary>
/// Runtime side of CSS Transitions 1. Owns the active transition table
/// (per element + property) and produces a "current effective value" that
/// the cascade layers above the computed value while a transition is in
/// flight.
/// <para>
/// Lifecycle: the cascade calls <see cref="OnComputedValueChanged"/> after
/// recomputing styles for an element. If the property is listed in
/// <c>transition-property</c> (or covered by <c>all</c>) and the new value
/// differs from the previous effective value, a new <see cref="ActiveTransition"/>
/// replaces any prior one for that (element, property) tuple. The
/// engine-driver ticks <see cref="Tick"/> from its frame loop
/// (intended to plug into the event-loop's <c>requestAnimationFrame</c>
/// queue once that's wired up — see WP notes); each tick advances the
/// progress and removes completed transitions.
/// </para>
/// <para>
/// The engine is single-threaded and not thread-safe; pages own their own
/// cascade thread and transition-engine instance.
/// </para>
/// </summary>
public sealed class TransitionEngine
{
    private readonly Dictionary<(Element, PropertyId), ActiveTransition> _active
        = new(ActiveKeyComparer.Instance);
    // Previous effective values per (element, property) — needed to detect
    // changes and to derive the from-value when a new transition starts.
    // Kept separate from _active because the previous value is still
    // interesting even after a transition completes (the next change is
    // measured from the completed end value, not from the original start).
    private readonly Dictionary<(Element, PropertyId), CssValue> _lastEffective
        = new(ActiveKeyComparer.Instance);

    private double _nowMs;

    /// <summary>
    /// Snapshot of an active transition for a given (element, property).
    /// Immutable once created — a new value for the same key replaces the
    /// record rather than mutating it.
    /// </summary>
    public sealed record ActiveTransition(
        Element Element,
        PropertyId Property,
        CssValue From,
        CssValue To,
        double StartMs,
        double DurationMs,
        double DelayMs,
        TimingFunction TimingFunction)
    {
        public double EndMs => StartMs + DelayMs + DurationMs;
    }

    /// <summary>
    /// Current monotonic clock in milliseconds — the engine's notion of
    /// "now". Tests advance this directly via <see cref="Tick"/>; the host
    /// is expected to feed it monotonic time (e.g.,
    /// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>-derived).
    /// </summary>
    public double NowMs => _nowMs;

    /// <summary>Number of in-flight transitions; primarily exposed for tests / diagnostics.</summary>
    public int ActiveCount => _active.Count;

    /// <summary>
    /// Called by the cascade after computing a property value for
    /// <paramref name="element"/>. If the property is transitionable and
    /// <paramref name="newValue"/> differs from the previously effective
    /// value, registers a new transition driven by the transition shorthand
    /// values on the same element (read from <paramref name="readProperty"/>).
    /// </summary>
    public void OnComputedValueChanged(
        Element element,
        PropertyId property,
        CssValue newValue,
        Func<PropertyId, CssValue?> readProperty)
    {
        var key = (element, property);
        if (!_lastEffective.TryGetValue(key, out var previous))
        {
            // First time we see this property for this element: prime the
            // table without firing a transition (transitions only fire on
            // *changes* per Transitions 1 §3 — the initial style is exempt).
            _lastEffective[key] = newValue;
            return;
        }

        if (Equals(previous, newValue))
            return;

        if (!Interpolator.IsAnimatable(property))
        {
            _lastEffective[key] = newValue;
            return;
        }

        if (!IsPropertyTransitioned(property, readProperty))
        {
            // No transition declared for this property — record the new
            // effective value and skip animating.
            _lastEffective[key] = newValue;
            _active.Remove(key);
            return;
        }

        var duration = ReadTimeMs(readProperty(PropertyId.TransitionDuration), 0);
        var delay = ReadTimeMs(readProperty(PropertyId.TransitionDelay), 0);
        var timing = TimingFunction.FromCss(readProperty(PropertyId.TransitionTimingFunction));

        if (duration <= 0 && delay <= 0)
        {
            // Zero-duration transitions are no-ops by spec (the new value
            // takes effect immediately) — short-circuit instead of pushing
            // a transition that would complete on the next tick anyway.
            _lastEffective[key] = newValue;
            _active.Remove(key);
            return;
        }

        // The from-value is the current effective value at the time of the
        // change — if there's an in-flight transition we sample it first so
        // a transition-in-progress doesn't snap back to its original start.
        var fromValue = previous;
        if (_active.TryGetValue(key, out var prior))
            fromValue = Sample(prior, _nowMs);

        _active[key] = new ActiveTransition(
            Element: element,
            Property: property,
            From: fromValue,
            To: newValue,
            StartMs: _nowMs,
            DurationMs: duration,
            DelayMs: delay,
            TimingFunction: timing);
        // _lastEffective stays at `previous` until the next Tick computes a
        // sample — that way the next OnComputedValueChanged still sees the
        // pre-transition value as the comparison baseline if the cascade
        // re-runs before a Tick.
    }

    /// <summary>
    /// Returns the current effective value for the (element, property)
    /// pair, or null if the engine has never seen this pair. The cascade
    /// calls this after running its compute step and substitutes the
    /// returned value into the final ComputedStyle.
    /// </summary>
    public CssValue? GetEffective(Element element, PropertyId property)
    {
        var key = (element, property);
        if (_active.TryGetValue(key, out var t))
            return Sample(t, _nowMs);
        return _lastEffective.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// Advance the engine clock to <paramref name="nowMs"/> and prune any
    /// transitions that finished at or before this tick. Returns the number
    /// of transitions completed during this tick — non-zero means the host
    /// should request a re-paint.
    /// </summary>
    public int Tick(double nowMs)
    {
        if (nowMs < _nowMs)
            // Time going backwards is a programming error — clamp to avoid
            // negative progress and a corrupted sample.
            nowMs = _nowMs;

        _nowMs = nowMs;
        if (_active.Count == 0) return 0;

        var completed = 0;
        List<(Element, PropertyId)>? toRemove = null;
        foreach (var (key, t) in _active)
        {
            var sample = Sample(t, _nowMs);
            _lastEffective[key] = sample;
            if (_nowMs >= t.EndMs)
            {
                completed++;
                (toRemove ??= []).Add(key);
            }
        }
        if (toRemove is not null)
            foreach (var k in toRemove) _active.Remove(k);
        return completed;
    }

    /// <summary>
    /// Clear all active transitions and the effective-value table for
    /// <paramref name="element"/>. Called by the host when an element is
    /// detached so we don't leak Element references through the dictionary.
    /// </summary>
    public void Forget(Element element)
    {
        var keys = _active.Keys.Where(k => ReferenceEquals(k.Item1, element)).ToList();
        foreach (var k in keys) _active.Remove(k);
        var keys2 = _lastEffective.Keys.Where(k => ReferenceEquals(k.Item1, element)).ToList();
        foreach (var k in keys2) _lastEffective.Remove(k);
    }

    /// <summary>Reset the engine to an empty state — primarily for tests.</summary>
    public void Reset()
    {
        _active.Clear();
        _lastEffective.Clear();
        _nowMs = 0;
    }

    private static CssValue Sample(ActiveTransition t, double nowMs)
    {
        var elapsed = nowMs - t.StartMs - t.DelayMs;
        if (elapsed <= 0) return t.From;
        if (t.DurationMs <= 0 || elapsed >= t.DurationMs) return t.To;
        var linear = elapsed / t.DurationMs;
        var eased = t.TimingFunction.Evaluate(linear);
        return Interpolator.Interpolate(t.Property, t.From, t.To, eased);
    }

    private static bool IsPropertyTransitioned(PropertyId property, Func<PropertyId, CssValue?> readProperty)
    {
        var declared = readProperty(PropertyId.TransitionProperty);
        // `all` (the spec default) matches every animatable property; any
        // explicit keyword that case-insensitively names the property also
        // matches. `none` is the spec opt-out.
        switch (declared)
        {
            case CssKeyword k:
                var name = k.Name.ToLowerInvariant();
                if (name == "none") return false;
                if (name == "all") return true;
                return string.Equals(name, PropertyName(property), StringComparison.OrdinalIgnoreCase);
            case CssValueList list:
                // Comma-separated transition-property list (e.g.,
                // `transition-property: opacity, transform`). We currently
                // accept any list whose items include `all` or the property
                // name; per-layer pairing with duration/timing-function is
                // tracked by wp:M5-css-multi-layer-transitions.
                foreach (var v in list.Values)
                {
                    if (v is CssKeyword item)
                    {
                        var nm = item.Name.ToLowerInvariant();
                        if (nm == "all") return true;
                        if (string.Equals(nm, PropertyName(property), StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                return false;
            default:
                return true;
        }
    }

    private static double ReadTimeMs(CssValue? value, double fallback)
    {
        return value switch
        {
            CssTime t => t.InSeconds * 1000.0,
            CssDimension d when d.Unit == "s" => d.Value * 1000.0,
            CssDimension d when d.Unit == "ms" => d.Value,
            CssNumber n => n.Value, // bare numbers are interpreted as ms — lenient
            _ => fallback,
        };
    }

    private static string PropertyName(PropertyId id)
    {
        // PascalCase → kebab-case. Mirrors the spec's property naming
        // without adding a dependency on the (non-existent) CssPropertyName
        // attribute we'd otherwise want. The transformation is mechanical
        // because all CSS property names already follow kebab-case modulo
        // the casing.
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

    private sealed class ActiveKeyComparer : IEqualityComparer<(Element, PropertyId)>
    {
        public static readonly ActiveKeyComparer Instance = new();
        public bool Equals((Element, PropertyId) x, (Element, PropertyId) y)
            => ReferenceEquals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        public int GetHashCode((Element, PropertyId) obj)
            => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1), (int)obj.Item2);
    }
}
