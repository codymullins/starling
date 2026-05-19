namespace Tessera.Layout.Position;

/// <summary>
/// CSS <c>position</c> keyword values. <see cref="Sticky"/> is parsed but
/// currently behaves like <see cref="Relative"/> — full sticky behaviour
/// (scroll-based clamping inside the nearest scroll container) lands in B6-3.
/// </summary>
internal enum PositionKind : byte
{
    Static,
    Relative,
    Absolute,
    Fixed,
    // TODO(B6-3): sticky is treated as relative here. Sticky needs a scroll-
    // container hookup which the layout engine doesn't expose yet.
    Sticky,
}

/// <summary>
/// A length-or-auto inset value for one of <c>top</c>/<c>right</c>/
/// <c>bottom</c>/<c>left</c>. Percentages resolve at layout time against the
/// containing block's content-box dimension, so we keep the original CSS
/// shape around long enough to apply the right basis.
/// </summary>
internal readonly record struct Inset
{
    private Inset(bool isAuto, double px, bool isPercentage, double percentage)
    {
        IsAuto = isAuto;
        Px = px;
        IsPercentage = isPercentage;
        Percentage = percentage;
    }

    public bool IsAuto { get; }
    public bool IsPercentage { get; }
    public double Px { get; }
    public double Percentage { get; }

    public static Inset Auto { get; } = new(isAuto: true, 0, false, 0);
    public static Inset Pixels(double px) => new(false, px, false, 0);
    public static Inset Percent(double pct) => new(false, 0, true, pct);

    /// <summary>Resolve this inset against a containing-block basis (in px).</summary>
    public double? Resolve(double basisPx)
    {
        if (IsAuto) return null;
        if (IsPercentage) return basisPx * Percentage / 100d;
        return Px;
    }
}

/// <summary>
/// Resolved position-related properties for an element.
/// <see cref="ZIndex"/> is exposed for paint-order use but paint reordering
/// is out of scope for B6-2; the value is parsed and stored only.
/// </summary>
internal readonly record struct PositionedProps(
    PositionKind Kind,
    Inset Top,
    Inset Right,
    Inset Bottom,
    Inset Left,
    int? ZIndex)
{
    public static PositionedProps Static { get; } = new(
        PositionKind.Static, Inset.Auto, Inset.Auto, Inset.Auto, Inset.Auto, null);

    /// <summary>True when this element establishes a containing block for
    /// <c>position: absolute</c> descendants — i.e. any positioning other
    /// than <c>static</c>.</summary>
    public bool IsContainingBlockForAbsolute =>
        Kind is PositionKind.Relative or PositionKind.Absolute or PositionKind.Fixed or PositionKind.Sticky;

    /// <summary>True when this element is removed from normal flow.</summary>
    public bool IsOutOfFlow => Kind is PositionKind.Absolute or PositionKind.Fixed;
}
