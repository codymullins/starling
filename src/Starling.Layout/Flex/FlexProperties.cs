namespace Tessera.Layout.Flex;

/// <summary>
/// Strongly-typed flex container properties resolved from a
/// <see cref="Tessera.Css.Cascade.ComputedStyle"/> in <see cref="FlexParser"/>.
/// Single-line (no <c>flex-wrap: wrap</c>) is all this scope covers — the wrap
/// keyword is parsed but treated as <c>nowrap</c>; multi-line layout is
/// deferred to B6-3.
/// </summary>
internal enum FlexDirection : byte
{
    Row,
    RowReverse,
    Column,
    ColumnReverse,
}

internal enum FlexWrap : byte
{
    NoWrap,
    // Parsed but not honoured yet — single-line is all this milestone covers.
    Wrap,
    WrapReverse,
}

internal enum JustifyContent : byte
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

internal enum AlignItems : byte
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    // Baseline currently falls back to FlexStart — true baseline alignment
    // requires per-item baseline metrics from the inline formatting context
    // that the flex layout doesn't have hands on yet. Wire it up alongside
    // mixed inline/flex content.
    Baseline,
}

internal readonly record struct FlexContainerProps(
    FlexDirection Direction,
    FlexWrap Wrap,
    JustifyContent Justify,
    AlignItems Align,
    double RowGap,
    double ColumnGap)
{
    public bool IsRow => Direction is FlexDirection.Row or FlexDirection.RowReverse;
    public bool IsReverse => Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

    /// <summary>The gap between adjacent items along the main axis.</summary>
    public double MainGap => IsRow ? ColumnGap : RowGap;
}

/// <summary>
/// Per-child flex item properties. <see cref="Basis"/> is the parsed
/// flex-basis: a non-negative length in px, or <c>null</c> when it resolves
/// to <c>auto</c>. The layout resolves <c>auto</c> against the child's width/
/// height property and falls back to its content size when those are also
/// <c>auto</c>.
/// </summary>
internal readonly record struct FlexItemProps(
    double Grow,
    double Shrink,
    double? Basis);
