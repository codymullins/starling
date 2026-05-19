using Tessera.Css.Properties;
using Tessera.Layout.Block;

namespace Tessera.Layout.Position;

/// <summary>
/// Second-pass positioning for elements with <c>position: absolute</c> or
/// <c>position: fixed</c>. Runs after normal in-flow layout has set every
/// box's <c>Frame</c>. For each out-of-flow box, this pass:
/// <list type="number">
///   <item>Finds the containing block — the nearest positioned ancestor's
///   padding box, or the initial containing block (the viewport) when no
///   positioned ancestor exists, or always the viewport for <c>fixed</c>.</item>
///   <item>Resolves <c>top</c>/<c>right</c>/<c>bottom</c>/<c>left</c>
///   (with percentages against the containing block's content-box size).</item>
///   <item>Determines the box's used width/height per CSS 2.1 §10.3.7 +
///   §10.6.4 (simplified — see <see cref="ResolveAxis"/>).</item>
///   <item>Lays out the box's subtree at the resolved width (so descendants
///   stay scoped inside the positioned box, establishing a BFC).</item>
///   <item>Translates the resulting document-space rect back into the
///   element's parent's content-box coordinates and writes
///   <see cref="Box.Box.Frame"/>.</item>
/// </list>
/// Simplifications relative to spec:
/// <list type="bullet">
///   <item>Static-position fallback when both <c>left</c> and <c>right</c>
///   are <c>auto</c>: we use the containing block's left edge (offset 0)
///   rather than threading the element's would-have-been in-flow position
///   through the in-flow pass. This matches what most pages get visually
///   when authors omit insets entirely (the result still snaps to the
///   start of the containing block, which is the spec's static-position
///   most of the time).</item>
///   <item>Margin auto on positioned elements: not yet centered against the
///   containing block; margins resolve to their computed value (zero by
///   default).</item>
///   <item>z-index is parsed into <see cref="PositionedProps.ZIndex"/> but
///   does not affect paint order — the painter still walks tree order.</item>
/// </list>
/// </summary>
internal sealed class PositionLayout
{
    private readonly BlockLayout _block;
    private readonly Size _viewport;
    private readonly Rect _initialContainingBlock;

    public PositionLayout(BlockLayout block, Size viewport)
    {
        _block = block;
        _viewport = viewport;
        _initialContainingBlock = new Rect(0, 0, viewport.Width, viewport.Height);
    }

    /// <summary>
    /// Walk the box tree rooted at <paramref name="root"/>, applying the
    /// positioning pass to every out-of-flow descendant. The root itself is
    /// always treated as in-flow (it's the layout document root).
    /// </summary>
    public void LayoutPositioned(Box.Box root)
    {
        // First, apply the cheap "relative" post-translation everywhere so
        // that any positioned ancestor's padding-box rect we read later
        // reflects the shifted position.
        ApplyRelativeOffsets(root, parentOriginX: 0, parentOriginY: 0);

        // Then place out-of-flow descendants. We do this as a second pass
        // because resolving a containing block needs ancestors with final
        // frames already written.
        PositionOutOfFlow(root);
    }

    // ---------------------------------------------------------------------
    // Pass 1: position: relative — shift the element's frame by (left, top).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Apply <c>position: relative</c> offsets in-place. Siblings are not
    /// re-flowed; only this element's painted box shifts.
    /// </summary>
    private void ApplyRelativeOffsets(Box.Box box, double parentOriginX, double parentOriginY)
    {
        var props = PositionParser.Parse(box.Style);
        if (props.Kind is PositionKind.Relative or PositionKind.Sticky)
        {
            // Percentage basis for top/bottom is the containing block's
            // *height*, and for left/right is its *width*. The containing
            // block for an in-flow element is its parent's content box.
            var (cbWidth, cbHeight) = ContainingBlockContentSizeOf(box.Parent);

            var leftPx = props.Left.Resolve(cbWidth);
            var rightPx = props.Right.Resolve(cbWidth);
            var topPx = props.Top.Resolve(cbHeight);
            var bottomPx = props.Bottom.Resolve(cbHeight);

            // CSS 2.1 §9.4.3: if both left & right are non-auto, `right`
            // is ignored for LTR. Same for top/bottom: `bottom` is ignored
            // when both set. So `left`/`top` win; otherwise either side can
            // shift (right = -right offset, bottom = -bottom offset).
            double dx = 0, dy = 0;
            if (leftPx is { } lx) dx = lx;
            else if (rightPx is { } rx) dx = -rx;

            if (topPx is { } ty) dy = ty;
            else if (bottomPx is { } by) dy = -by;

            box.Frame = box.Frame.Translate(dx, dy);
        }

        // Recurse into children with the (possibly shifted) origin baked in.
        // Origin tracking isn't strictly required for the relative pass —
        // frames are stored in parent-relative coords — but we keep it here
        // in case we want to add coverage later.
        foreach (var child in box.Children)
            ApplyRelativeOffsets(child, parentOriginX + box.Frame.X, parentOriginY + box.Frame.Y);
    }

    // ---------------------------------------------------------------------
    // Pass 2: position: absolute / fixed.
    // ---------------------------------------------------------------------

    private void PositionOutOfFlow(Box.Box root)
    {
        // We need to know, for each box, the document-space offset of its
        // padding-box top-left so it can serve as a containing block for
        // any absolutely-positioned descendant. Walk depth-first and keep a
        // stack of (positioned ancestor → padding-box document rect).
        var ancestorStack = new System.Collections.Generic.Stack<Rect>();
        Walk(root, parentContentOriginX: 0, parentContentOriginY: 0, ancestorStack);
    }

    private void Walk(Box.Box box, double parentContentOriginX, double parentContentOriginY, System.Collections.Generic.Stack<Rect> ancestorStack)
    {
        // box.Frame is the *border-box* rect in parent's content-box coords.
        var borderBoxDocX = parentContentOriginX + box.Frame.X;
        var borderBoxDocY = parentContentOriginY + box.Frame.Y;

        // Padding-box rect (= border-box inset by border edges).
        var paddingBoxRect = new Rect(
            borderBoxDocX + box.Border.Left,
            borderBoxDocY + box.Border.Top,
            box.Frame.Width - box.Border.Horizontal,
            box.Frame.Height - box.Border.Vertical);

        // Content-box origin in document space (for descendants' frames).
        var contentOriginX = paddingBoxRect.X + box.Padding.Left;
        var contentOriginY = paddingBoxRect.Y + box.Padding.Top;

        var props = PositionParser.Parse(box.Style);

        // If this box is out-of-flow, position it now. The containing block
        // is the top-of-stack ancestor for absolute; or the viewport for
        // fixed.
        if (props.IsOutOfFlow)
        {
            Rect cb;
            if (props.Kind == PositionKind.Fixed)
            {
                cb = _initialContainingBlock;
            }
            else // Absolute
            {
                cb = ancestorStack.Count > 0 ? ancestorStack.Peek() : _initialContainingBlock;
            }

            PlaceAbsoluteOrFixed(box, props, cb, parentContentOriginX, parentContentOriginY);

            // After placing, the box's frame is in *parent content* coords,
            // and we want to recurse into its children using its NEW
            // padding-box as their containing block. Recompute.
            borderBoxDocX = parentContentOriginX + box.Frame.X;
            borderBoxDocY = parentContentOriginY + box.Frame.Y;
            paddingBoxRect = new Rect(
                borderBoxDocX + box.Border.Left,
                borderBoxDocY + box.Border.Top,
                box.Frame.Width - box.Border.Horizontal,
                box.Frame.Height - box.Border.Vertical);
            contentOriginX = paddingBoxRect.X + box.Padding.Left;
            contentOriginY = paddingBoxRect.Y + box.Padding.Top;
        }

        // Push the padding-box rect for *this* box if it establishes a
        // containing block for absolute descendants.
        var pushed = props.IsContainingBlockForAbsolute;
        if (pushed)
            ancestorStack.Push(paddingBoxRect);

        foreach (var child in box.Children)
            Walk(child, contentOriginX, contentOriginY, ancestorStack);

        if (pushed)
            ancestorStack.Pop();
    }

    /// <summary>
    /// Compute the used frame for a <c>position: absolute</c> or <c>fixed</c>
    /// element, lay out its descendants at the resolved width, and write the
    /// final <see cref="Box.Box.Frame"/> in parent-content-box coordinates.
    /// </summary>
    private void PlaceAbsoluteOrFixed(
        Box.Box box,
        PositionedProps props,
        Rect cbDocRect,
        double parentContentOriginX,
        double parentContentOriginY)
    {
        // Resolve box-model values against the containing block (CSS spec:
        // percentage margins/padding on absolutely-positioned boxes resolve
        // against the containing block's *width*, just like normal).
        ResolveBoxModel(box, cbDocRect.Width);

        // Resolve explicit width/height (auto → null).
        var widthResolved = BlockLayout.ResolveLength(box.Style, PropertyId.Width, cbDocRect.Width, _viewport, allowAuto: true);
        var heightResolved = BlockLayout.ResolveLength(box.Style, PropertyId.Height, cbDocRect.Height, _viewport, allowAuto: true);

        // Resolve insets against the containing block's content-box
        // dimensions.
        var (left, right, usedWidth) = ResolveAxis(
            startInset: props.Left,
            endInset: props.Right,
            cbStart: cbDocRect.X,
            cbExtent: cbDocRect.Width,
            explicitSize: widthResolved,
            startMargin: box.Margin.Left,
            endMargin: box.Margin.Right,
            outerInsets: box.Padding.Horizontal + box.Border.Horizontal);

        // Default height: lay out children at the resolved width and use
        // their consumed height (block formatting context for descendants).
        // For now, lay out children at the chosen content width to get a
        // natural height even when `height: auto` and no bottom inset.
        var contentWidth = System.Math.Max(0, usedWidth - box.Padding.Horizontal - box.Border.Horizontal);
        var naturalHeight = _block.LayoutChildren(box, contentWidth);

        var (top, bottom, usedHeight) = ResolveAxis(
            startInset: props.Top,
            endInset: props.Bottom,
            cbStart: cbDocRect.Y,
            cbExtent: cbDocRect.Height,
            explicitSize: heightResolved ?? (naturalHeight + box.Padding.Vertical + box.Border.Vertical),
            startMargin: box.Margin.Top,
            endMargin: box.Margin.Bottom,
            outerInsets: box.Padding.Vertical + box.Border.Vertical);

        // Document-space top-left of the element's *border box*.
        var docX = left + box.Margin.Left;
        var docY = top + box.Margin.Top;

        // Translate into parent-content-box coordinates.
        box.Frame = new Rect(
            docX - parentContentOriginX,
            docY - parentContentOriginY,
            usedWidth,
            usedHeight);
    }

    /// <summary>
    /// Resolve one axis of a positioned box. Returns:
    /// <list type="bullet">
    ///   <item><c>start</c>: the document-space start coordinate of the
    ///   element's outer-edge (margin-box) along this axis.</item>
    ///   <item><c>end</c>: the document-space end coordinate of the
    ///   element's margin-box.</item>
    ///   <item><c>usedSize</c>: the element's border-box size along this
    ///   axis.</item>
    /// </list>
    /// Implements CSS 2.1 §10.3.7 (horizontal) / §10.6.4 (vertical),
    /// simplified — auto margins resolve to 0 rather than absorbing slack.
    /// </summary>
    private static (double Start, double End, double UsedSize) ResolveAxis(
        Inset startInset,
        Inset endInset,
        double cbStart,
        double cbExtent,
        double? explicitSize,
        double startMargin,
        double endMargin,
        double outerInsets)
    {
        var s = startInset.Resolve(cbExtent);
        var e = endInset.Resolve(cbExtent);

        double startCoord;
        double usedSize;

        if (s is null && e is null)
        {
            // Both auto → static position. Simplification: snap to cb start.
            startCoord = cbStart;
            usedSize = explicitSize ?? 0;
        }
        else if (s is { } sv && e is null)
        {
            startCoord = cbStart + sv;
            usedSize = explicitSize ?? 0;
        }
        else if (s is null && e is { } ev)
        {
            // End edge anchored: usedSize must come from explicitSize, then
            // place start = cbEnd - end - usedSize - margins.
            usedSize = explicitSize ?? 0;
            startCoord = cbStart + cbExtent - ev - usedSize - startMargin - endMargin;
        }
        else
        {
            // Both set. If explicitSize is given, `end` is over-constrained
            // (spec: ignore `right` / `bottom` in LTR/TTB writing modes).
            // If explicit is null, width = remaining space.
            var startVal = s!.Value;
            var endVal = e!.Value;
            if (explicitSize is { } sz)
            {
                startCoord = cbStart + startVal;
                usedSize = sz;
            }
            else
            {
                startCoord = cbStart + startVal;
                var available = cbExtent - startVal - endVal - startMargin - endMargin;
                usedSize = System.Math.Max(0, available);
            }
        }

        usedSize = System.Math.Max(0, usedSize);
        return (startCoord, startCoord + startMargin + endMargin + usedSize, usedSize);
    }

    private static (double Width, double Height) ContainingBlockContentSizeOf(Box.Box? parent)
    {
        if (parent is null) return (0, 0);
        var contentW = System.Math.Max(0, parent.Frame.Width - parent.Padding.Horizontal - parent.Border.Horizontal);
        var contentH = System.Math.Max(0, parent.Frame.Height - parent.Padding.Vertical - parent.Border.Vertical);
        return (contentW, contentH);
    }

    private void ResolveBoxModel(Box.Box box, double containerWidth)
    {
        box.Margin = new Edges(
            BlockLayout.ResolveLength(box.Style, PropertyId.MarginTop, _viewport.Height, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.MarginRight, containerWidth, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.MarginBottom, _viewport.Height, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.MarginLeft, containerWidth, _viewport) ?? 0);

        box.Padding = new Edges(
            BlockLayout.ResolveLength(box.Style, PropertyId.PaddingTop, _viewport.Height, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.PaddingRight, containerWidth, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.PaddingBottom, _viewport.Height, _viewport) ?? 0,
            BlockLayout.ResolveLength(box.Style, PropertyId.PaddingLeft, containerWidth, _viewport) ?? 0);

        // Border resolution is left at zero for the positioned scope — the
        // flex path uses the same simplification. Borders on positioned
        // elements can land alongside flex border support.
        box.Border = Edges.Zero;
    }
}
