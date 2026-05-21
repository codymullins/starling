namespace Starling.Css.Values;

/// <summary>
/// Resolved value of the CSS <c>box-shadow</c> property — an ordered list of
/// shadow layers per <see href="https://www.w3.org/TR/css-backgrounds-3/#box-shadow">
/// CSS Backgrounds 3 §6</see>. Layers paint front-to-back in source order: the
/// first listed layer is on top. The keyword <c>none</c> resolves to an empty
/// list.
/// </summary>
public sealed record CssBoxShadow(IReadOnlyList<CssShadow> Layers) : CssValue
{
    public static CssBoxShadow None { get; } = new([]);

    public bool IsNone => Layers.Count == 0;
}

/// <summary>
/// A single <c>box-shadow</c> layer: <c>&lt;color&gt;? &amp;&amp; [&lt;length&gt;{2,4}
/// &amp;&amp; inset?]</c>. The two required lengths are the horizontal and
/// vertical offset; the optional third and fourth are the blur radius
/// (non-negative) and the spread distance. <see cref="Inset"/> selects an inner
/// shadow. A <c>null</c> <see cref="Color"/> means the layer used (or defaulted
/// to) <c>currentColor</c> and the painter must substitute the element's
/// <c>color</c>.
/// </summary>
public sealed record CssShadow(
    CssLength OffsetX,
    CssLength OffsetY,
    CssLength Blur,
    CssLength Spread,
    CssColor? Color,
    bool Inset);
