using Tessera.Common.Diagnostics;
using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout.Box;
using Tessera.Layout.Inline;
using Tessera.Layout.Text;

namespace Tessera.Layout.Block;

/// <summary>
/// Block formatting context layout. Children stack vertically; inline children
/// are folded into anonymous blocks before this pass so we only ever see block
/// children here.
/// </summary>
/// <remarks>
/// Coordinate convention: every box's <see cref="Box.Box.Frame"/> is in its
/// <em>parent's content-box</em> coordinate space. To get a document-space
/// position, the painter walks the tree adding parent content origins.
/// </remarks>
internal sealed class BlockLayout
{
    private readonly ITextMeasurer _measurer;
    private readonly Size _viewport;
    private readonly InlineLayout _inline;

    public BlockLayout(ITextMeasurer measurer, Size viewport, IDiagnostics? diagnostics = null)
    {
        _measurer = measurer;
        _viewport = viewport;
        _inline = new InlineLayout(measurer, viewport, diagnostics);
    }

    public void Layout(Box.Box root)
    {
        ResolveBoxModel(root, _viewport.Width);
        var contentWidth = _viewport.Width - root.Margin.Horizontal - root.Border.Horizontal - root.Padding.Horizontal;
        var consumedHeight = LayoutChildren(root, contentWidth);
        var explicitHeight = ResolveLength(root.Style, PropertyId.Height, _viewport.Height, _viewport, allowAuto: true);
        var contentHeight = explicitHeight ?? consumedHeight;
        root.Frame = new Rect(
            root.Margin.Left,
            root.Margin.Top,
            contentWidth + root.Padding.Horizontal + root.Border.Horizontal,
            contentHeight + root.Padding.Vertical + root.Border.Vertical);
    }

    /// <summary>
    /// Lay out <paramref name="parent"/>'s children as a block formatting
    /// context using <paramref name="containerWidth"/> as the available
    /// content width. Returns the total consumed height. The parent's own
    /// frame is not touched — callers (e.g. <see cref="Inline.InlineLayout"/>
    /// for an inline-block) compose the result into their own box model.
    /// </summary>
    internal double LayoutChildren(Box.Box parent, double containerWidth)
        => LayoutChildren(parent, containerWidth, measure: false);

    /// <summary>
    /// Lay out children with the option of running in measurement mode, used
    /// by the inline-block shrink-to-fit pass. In measurement mode the inline
    /// formatting context skips its post-layout alignment shifts so the
    /// caller's <c>MeasureUsedWidth</c> walk sees natural pre-shift positions.
    /// </summary>
    internal double LayoutChildren(Box.Box parent, double containerWidth, bool measure)
    {
        var cursorY = 0d;
        var prevBottomMargin = 0d;
        var first = true;
        foreach (var child in parent.Children)
        {
            // CSS 2.1 §9.3.1: `position: absolute` and `position: fixed`
            // remove the element from normal flow. We skip them here so the
            // cursor doesn't advance — they're placed in a second pass by
            // PositionLayout. The child's Frame is left at default (zero)
            // until that pass writes it.
            if (IsOutOfFlow(child.Style))
                continue;

            LayoutBlock(child, containerWidth, ref cursorY, ref prevBottomMargin, ref first, measure);
        }
        return cursorY;
    }

    private static bool IsOutOfFlow(ComputedStyle? style)
    {
        if (style is null) return false;
        if (style.Get(PropertyId.Position) is not CssKeyword k) return false;
        var name = k.Name;
        return string.Equals(name, "absolute", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "fixed", StringComparison.OrdinalIgnoreCase);
    }

    private void LayoutBlock(Box.Box child, double containerWidth, ref double cursorY, ref double prevBottomMargin, ref bool first, bool measure = false)
    {
        if (child.Kind == BoxKind.AnonymousBlock)
        {
            // Anonymous blocks take the initial value for box-model properties
            // (margin/padding/border = 0); they only inherit text-formatting for
            // the inline pass.
            child.Margin = Edges.Zero;
            child.Padding = Edges.Zero;
            child.Border = Edges.Zero;

            var inlineHeight = _inline.Layout(child, containerWidth, measure);
            child.Frame = new Rect(0, cursorY, containerWidth, inlineHeight);
            cursorY = child.Frame.Bottom;
            prevBottomMargin = 0;
            first = false;
            return;
        }

        ResolveBoxModel(child, containerWidth);

        // Margin-collapse against previous sibling.
        var topMargin = child.Margin.Top;
        var collapseTop = first ? topMargin : Math.Max(0, Math.Max(topMargin, prevBottomMargin) - prevBottomMargin);
        cursorY += collapseTop;
        first = false;

        var width = ContentWidth(child, containerWidth);

        // CSS 2.1 §10.3.3 — resolve `auto` horizontal margins for non-replaced
        // block-level elements in normal flow. Only when `width` is not `auto`
        // do auto margins absorb slack; otherwise they resolve to 0 (already
        // handled by ResolveBoxModel).
        ResolveAutoHorizontalMargins(child, containerWidth, width);

        // Flex container: hand off to the flex formatting context. Flex sizes
        // its items inside the container's content box; the result replaces
        // what block stacking would have produced.
        var explicitHeight = ResolveLength(child.Style, PropertyId.Height, _viewport.Height, _viewport, allowAuto: true);
        double childContentHeight;
        if (IsFlexContainer(child.Style))
        {
            var flex = new Tessera.Layout.Flex.FlexLayout(this, _viewport);
            childContentHeight = flex.Layout(child, width, explicitHeight);
        }
        else
        {
            childContentHeight = LayoutChildren(child, width, measure);
        }

        var resolvedHeight = explicitHeight ?? childContentHeight;
        var fullHeight = resolvedHeight + child.Padding.Vertical + child.Border.Vertical;

        child.Frame = new Rect(
            child.Margin.Left,
            cursorY,
            width + child.Padding.Horizontal + child.Border.Horizontal,
            fullHeight);

        cursorY += fullHeight + child.Margin.Bottom;
        prevBottomMargin = child.Margin.Bottom;
    }

    /// <summary>
    /// CSS 2.1 §10.3.3 — for a block-level non-replaced element in normal flow,
    /// `auto` values for `margin-left` / `margin-right` absorb the slack between
    /// the used width and the containing block's content width:
    /// <list type="bullet">
    ///   <item>Both auto → center the box (each margin gets half the slack; if
    ///   negative, both go to 0).</item>
    ///   <item>One auto → that one absorbs all slack.</item>
    ///   <item>Neither auto → leave margins as computed (over-constrained;
    ///   spec says ignore margin-right in LTR but we keep the user's values).</item>
    /// </list>
    /// This is only meaningful when `width` is not `auto`; if width filled the
    /// available space, slack is zero (or negative) and there is nothing to
    /// distribute.
    /// </summary>
    private static void ResolveAutoHorizontalMargins(Box.Box child, double containerWidth, double usedWidth)
    {
        var leftAuto = IsAutoMargin(child.Style, PropertyId.MarginLeft);
        var rightAuto = IsAutoMargin(child.Style, PropertyId.MarginRight);
        if (!leftAuto && !rightAuto) return;

        // CSS 2.1 §10.3.3 says auto margins absorb slack when width is not
        // auto. §10.4 extends this to the max-width / min-width clamp: after
        // the tentative width is constrained, the leftover horizontal space
        // is redistributed to auto margins, even though the *computed* width
        // value is `auto`. Without this, `body { max-width: 35em; margin: auto }`
        // (the words.html / Justin Jackson layout, MDN article body, etc.)
        // pins to the left instead of centering. Compute the slack from the
        // used width — if it's zero (true auto width, no clamp) the autos
        // collapse to 0 the same way the §10.3.3 path produces.
        var outerWidth = usedWidth + child.Padding.Horizontal + child.Border.Horizontal;
        var slack = containerWidth - outerWidth - child.Margin.Left - child.Margin.Right;
        if (slack <= 0) return;

        double newLeft = child.Margin.Left;
        double newRight = child.Margin.Right;
        if (leftAuto && rightAuto)
        {
            if (slack < 0)
            {
                // Negative slack: both autos go to 0 (left edge).
                newLeft = 0;
                newRight = 0;
            }
            else
            {
                newLeft = slack / 2d;
                newRight = slack - newLeft;
            }
        }
        else if (leftAuto)
        {
            newLeft = Math.Max(0, slack);
        }
        else // rightAuto
        {
            newRight = Math.Max(0, slack);
        }

        child.Margin = new Edges(child.Margin.Top, newRight, child.Margin.Bottom, newLeft);
    }

    private static bool IsAutoMargin(ComputedStyle? style, PropertyId property)
        => style is not null && style.Get(property) is CssKeyword k && k.Name == "auto";

    internal static bool IsFlexContainer(ComputedStyle? style)
    {
        if (style is null) return false;
        if (style.Get(PropertyId.Display) is not CssKeyword k) return false;
        return k.Name.Equals("flex", StringComparison.OrdinalIgnoreCase)
            || k.Name.Equals("inline-flex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitWidth(ComputedStyle? style)
    {
        if (style is null) return false;
        var value = style.Get(PropertyId.Width);
        return value is not CssKeyword k || k.Name != "auto";
    }

    private void ResolveBoxModel(Box.Box box, double containerWidth)
    {
        box.Margin = new Edges(
            ResolveLength(box.Style, PropertyId.MarginTop, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginRight, containerWidth, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginBottom, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginLeft, containerWidth, _viewport) ?? 0);

        box.Padding = new Edges(
            ResolveLength(box.Style, PropertyId.PaddingTop, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingRight, containerWidth, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingBottom, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingLeft, containerWidth, _viewport) ?? 0);

        box.Border = new Edges(
            ResolveBorderWidth(box.Style, PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, _viewport));
    }

    private double ContentWidth(Box.Box box, double containerWidth)
    {
        // CSS 2.1 §10.4: tentative width is `width` if specified, otherwise the
        // available space within the parent. The tentative width is then
        // clamped by `max-width` (upper bound) and `min-width` (lower bound).
        // Honouring max-width is what makes the classic "max-width: 35em"
        // narrow-column layout work (justinjackson.ca/words.html, MDN, etc).
        var explicitWidth = ResolveLength(box.Style, PropertyId.Width, containerWidth, _viewport, allowAuto: true);
        double tentative;
        if (explicitWidth is { } w)
        {
            tentative = w;
        }
        else
        {
            var available = containerWidth - box.Margin.Horizontal - box.Border.Horizontal - box.Padding.Horizontal;
            tentative = Math.Max(0, available);
        }

        // max-width: `none` (the initial value) is a no-op; any concrete length
        // or percentage clamps the tentative width down.
        var maxWidth = ResolveMaxLength(box.Style, PropertyId.MaxWidth, containerWidth, _viewport);
        if (maxWidth is { } mx && tentative > mx)
            tentative = mx;

        // min-width: initial value `0` is a no-op; a concrete length/percentage
        // expands the box back up when it's narrower than the floor.
        var minWidth = ResolveLength(box.Style, PropertyId.MinWidth, containerWidth, _viewport) ?? 0;
        if (tentative < minWidth)
            tentative = minWidth;

        return Math.Max(0, tentative);
    }

    // max-* properties accept the keyword `none` (the initial value) to mean
    // "no upper bound". ResolveLength maps `none` to 0, which would collapse
    // the box; intercept it here so callers can skip the clamp.
    private static double? ResolveMaxLength(ComputedStyle? style, PropertyId property, double percentageBasis, Size? viewport)
    {
        if (style is null) return null;
        var value = style.Get(property);
        if (value is CssKeyword k && k.Name == "none") return null;
        return ResolveLength(style, property, percentageBasis, viewport);
    }

    private static double ResolveBorderWidth(ComputedStyle? style, PropertyId widthId, PropertyId styleId, Size? viewport = null)
    {
        if (style is null) return 0;
        var styleValue = style.Get(styleId);
        if (styleValue is CssKeyword k && k.Name == "none") return 0;
        return style.Get(widthId) is CssLength len ? ToPx(len, viewport) : 0;
    }

    internal static double? ResolveLength(ComputedStyle? style, PropertyId property, double percentageBasis, bool allowAuto = false)
        => ResolveLength(style, property, percentageBasis, viewport: null, allowAuto);

    internal static double? ResolveLength(ComputedStyle? style, PropertyId property, double percentageBasis, Size? viewport, bool allowAuto = false)
    {
        if (style is null) return null;
        var value = style.Get(property);
        return value switch
        {
            CssLength len => ToPx(len, viewport),
            CssPercentage pct => percentageBasis * pct.Value / 100d,
            CssNumber n => n.Value,
            CssKeyword k when k.Name == "auto" => allowAuto ? null : 0,
            CssKeyword k when k.Name == "none" => 0,
            _ => null,
        };
    }

    internal static double ToPx(CssLength length) => ToPx(length, viewport: null);

    internal static double ToPx(CssLength length, Size? viewport) => length.Unit switch
    {
        CssLengthUnit.Px => length.Value,
        CssLengthUnit.Pt => length.Value * 4d / 3d,
        CssLengthUnit.Pc => length.Value * 16d,
        CssLengthUnit.In => length.Value * 96d,
        CssLengthUnit.Cm => length.Value * 96d / 2.54d,
        CssLengthUnit.Mm => length.Value * 96d / 25.4d,
        CssLengthUnit.Q => length.Value * 96d / 101.6d,
        CssLengthUnit.Em => length.Value * 16d,
        CssLengthUnit.Rem => length.Value * 16d,
        CssLengthUnit.Vh => length.Value * (viewport?.Height ?? 100d) / 100d,
        CssLengthUnit.Vw => length.Value * (viewport?.Width ?? 100d) / 100d,
        _ => length.Value,
    };
}
