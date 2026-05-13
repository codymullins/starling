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

    public BlockLayout(ITextMeasurer measurer, Size viewport)
    {
        _measurer = measurer;
        _viewport = viewport;
        _inline = new InlineLayout(measurer, viewport);
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

    private double LayoutChildren(Box.Box parent, double containerWidth)
    {
        var cursorY = 0d;
        var prevBottomMargin = 0d;
        var first = true;
        foreach (var child in parent.Children)
        {
            LayoutBlock(child, containerWidth, ref cursorY, ref prevBottomMargin, ref first);
        }
        return cursorY;
    }

    private void LayoutBlock(Box.Box child, double containerWidth, ref double cursorY, ref double prevBottomMargin, ref bool first)
    {
        if (child.Kind == BoxKind.AnonymousBlock)
        {
            // Anonymous blocks take the initial value for box-model properties
            // (margin/padding/border = 0); they only inherit text-formatting for
            // the inline pass.
            child.Margin = Edges.Zero;
            child.Padding = Edges.Zero;
            child.Border = Edges.Zero;

            var inlineHeight = _inline.Layout(child, containerWidth);
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
        var childContentHeight = LayoutChildren(child, width);

        var explicitHeight = ResolveLength(child.Style, PropertyId.Height, _viewport.Height, _viewport, allowAuto: true);
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
        var explicitWidth = ResolveLength(box.Style, PropertyId.Width, containerWidth, _viewport, allowAuto: true);
        if (explicitWidth is { } w) return w;
        var available = containerWidth - box.Margin.Horizontal - box.Border.Horizontal - box.Padding.Horizontal;
        return Math.Max(0, available);
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
