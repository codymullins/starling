namespace Tessera.Css.Values;

/// <summary>
/// Inputs needed to resolve relative units (em/rem/lh/rlh/vh/vw/sv*/lv*/dv*/cq*)
/// and percentages to absolute pixel lengths per CSS Values 4 §6 and §10.
/// Supplied by the style engine at used-value time.
/// </summary>
public sealed record CssResolutionContext(
    double FontSizePx,
    double RootFontSizePx,
    double LineHeightPx,
    double RootLineHeightPx,
    double XHeightPx,
    double CapHeightPx,
    double ZeroAdvancePx,
    double IcAdvancePx,
    double ViewportWidthPx,
    double ViewportHeightPx,
    double SmallViewportWidthPx,
    double SmallViewportHeightPx,
    double LargeViewportWidthPx,
    double LargeViewportHeightPx,
    double DynamicViewportWidthPx,
    double DynamicViewportHeightPx,
    double ContainerWidthPx,
    double ContainerHeightPx,
    double PercentageBasisPx,
    WritingMode WritingMode = WritingMode.HorizontalTb)
{
    /// <summary>Default desktop-ish context for tests and headless rendering.
    /// <see cref="PercentageBasisPx"/> defaults to <see cref="double.NaN"/>, which
    /// tells the resolver to keep percentages symbolic (they get resolved at
    /// used-value time when a containing-block basis is known).</summary>
    public static CssResolutionContext Default { get; } = new(
        FontSizePx: 16,
        RootFontSizePx: 16,
        LineHeightPx: 19.2,
        RootLineHeightPx: 19.2,
        XHeightPx: 8,
        CapHeightPx: 11,
        ZeroAdvancePx: 8,
        IcAdvancePx: 16,
        ViewportWidthPx: 1024,
        ViewportHeightPx: 768,
        SmallViewportWidthPx: 1024,
        SmallViewportHeightPx: 768,
        LargeViewportWidthPx: 1024,
        LargeViewportHeightPx: 768,
        DynamicViewportWidthPx: 1024,
        DynamicViewportHeightPx: 768,
        ContainerWidthPx: 1024,
        ContainerHeightPx: 768,
        PercentageBasisPx: double.NaN);

    public bool HasPercentageBasis => !double.IsNaN(PercentageBasisPx);

    public double ViewportInlinePx => WritingMode.IsHorizontal() ? ViewportWidthPx : ViewportHeightPx;

    public double ViewportBlockPx => WritingMode.IsHorizontal() ? ViewportHeightPx : ViewportWidthPx;

    public double ContainerInlinePx => WritingMode.IsHorizontal() ? ContainerWidthPx : ContainerHeightPx;

    public double ContainerBlockPx => WritingMode.IsHorizontal() ? ContainerHeightPx : ContainerWidthPx;
}

public enum WritingMode
{
    HorizontalTb,
    VerticalRl,
    VerticalLr,
    SidewaysRl,
    SidewaysLr,
}

public static class WritingModeExtensions
{
    public static bool IsHorizontal(this WritingMode wm)
        => wm is WritingMode.HorizontalTb;
}
