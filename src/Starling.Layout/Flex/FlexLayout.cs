using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout.Block;
using Starling.Layout.Box;

namespace Starling.Layout.Flex;

/// <summary>
/// Single-line flex layout (CSS Flex Box Layout Module Level 1, simplified).
/// Handles <c>flex-direction</c>, <c>justify-content</c>, <c>align-items</c>,
/// the <c>flex</c> shorthand + individual <c>flex-grow</c>/<c>shrink</c>/
/// <c>basis</c>, and <c>gap</c>/<c>row-gap</c>/<c>column-gap</c>.
/// Out of scope (deferred to B6-3+):
/// <list type="bullet">
///   <item><c>flex-wrap: wrap</c> — parsed but treated as <c>nowrap</c>.</item>
///   <item><c>align-content</c>, <c>align-self</c>.</item>
///   <item>True baseline alignment (currently falls back to <c>flex-start</c>).</item>
///   <item>Min/max-width interaction with flex base size beyond a final clamp.</item>
/// </list>
/// </summary>
/// <remarks>
/// Coordinate convention matches <see cref="BlockLayout"/>: each child's
/// <see cref="Box.Box.Frame"/> is in the flex container's content-box
/// coordinate space (so x = 0 is the left edge of padding, not the parent's
/// content edge). The container's own frame is set by the calling block
/// layout pass.
/// </remarks>
internal sealed class FlexLayout
{
    private readonly BlockLayout _block;
    private readonly Size _viewport;

    public FlexLayout(BlockLayout block, Size viewport)
    {
        _block = block;
        _viewport = viewport;
    }

    /// <summary>
    /// Lay out <paramref name="container"/>'s children as flex items inside a
    /// content box of <paramref name="containerWidth"/> wide. Returns the
    /// consumed content height (cross size for row, main size for column) so
    /// the caller can size the container's box.
    /// </summary>
    /// <param name="container">The flex container box. Its own
    /// <c>Frame</c>/<c>Margin</c>/<c>Padding</c>/<c>Border</c> are not
    /// touched — the caller composes them.</param>
    /// <param name="containerWidth">Content-box width of the container.</param>
    /// <param name="explicitHeight">Explicit content-box height of the
    /// container, or null when <c>height: auto</c>. Used as the main basis
    /// for column direction and as the cross basis for row direction.</param>
    public double Layout(Box.Box container, double containerWidth, double? explicitHeight)
    {
        var props = FlexParser.ParseContainer(
            container.Style,
            mainAxisBasisPx: containerWidth,
            crossAxisBasisPx: explicitHeight ?? _viewport.Height,
            _viewport);

        // Main / cross sizes of the container's content box.
        var mainSize = props.IsRow ? containerWidth : (explicitHeight ?? _viewport.Height);
        var crossSize = props.IsRow ? (explicitHeight ?? double.NaN) : containerWidth;

        var children = container.Children;
        if (children.Count == 0)
            return explicitHeight ?? 0;

        // ---- step 1: resolve each item's flex base size + box model ----
        // MainSize / CrossSize are tracked as *content-box* sizes throughout;
        // the per-axis padding+border (MainPad / CrossPad) is added back only
        // when distributing free space and sizing frames. Keeping content-box
        // internally lets the final block pass over each item subtract its own
        // padding exactly once — without this a padded item (e.g. a button with
        // `padding: 16px 32px`) had its padding subtracted twice and collapsed
        // to a sliver, wrapping its label.
        var items = new Item[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            ResolveBoxModel(child, containerWidth);
            var itemProps = FlexParser.ParseItem(child.Style);
            var basis = ResolveBasis(child, itemProps.Basis, props, containerWidth, explicitHeight);
            items[i] = new Item
            {
                Box = child,
                Props = itemProps,
                Basis = basis,
                MainSize = basis,
                MainPad = props.IsRow
                    ? child.Padding.Horizontal + child.Border.Horizontal
                    : child.Padding.Vertical + child.Border.Vertical,
                CrossPad = props.IsRow
                    ? child.Padding.Vertical + child.Border.Vertical
                    : child.Padding.Horizontal + child.Border.Horizontal,
            };
        }

        // ---- step 2: distribute free space along the main axis ----
        // Free space compares the container's content-box main size against the
        // items' *outer* (content + padding + border) main sizes, plus gaps.
        var gapTotal = props.MainGap * Math.Max(0, items.Length - 1);
        var outerSum = 0d;
        for (var i = 0; i < items.Length; i++) outerSum += items[i].MainSize + items[i].MainPad;
        var free = mainSize - outerSum - gapTotal;

        if (free > 0)
        {
            var totalGrow = 0d;
            for (var i = 0; i < items.Length; i++) totalGrow += items[i].Props.Grow;
            if (totalGrow > 0)
                for (var i = 0; i < items.Length; i++)
                    items[i].MainSize += free * (items[i].Props.Grow / totalGrow);
        }
        else if (free < 0)
        {
            // Simplified shrink: weighted by flex-shrink only (the spec
            // multiplies by basis to make larger items absorb more
            // overflow — that's a TODO for the full algorithm).
            var totalShrink = 0d;
            for (var i = 0; i < items.Length; i++) totalShrink += items[i].Props.Shrink;
            if (totalShrink > 0)
                for (var i = 0; i < items.Length; i++)
                    items[i].MainSize = Math.Max(0, items[i].MainSize + free * (items[i].Props.Shrink / totalShrink));
        }

        // ---- step 3: measure each item's cross size (content-box) ----
        var maxCrossOuter = 0d;
        for (var i = 0; i < items.Length; i++)
        {
            var crossSelf = MeasureCrossSize(items[i].Box, props, items[i].MainSize, containerWidth, crossSize);
            items[i].CrossSize = crossSelf;
            maxCrossOuter = Math.Max(maxCrossOuter, crossSelf + items[i].CrossPad);
        }

        // Resolved cross-axis line size (a border-box extent): the explicit
        // container cross size if given, else the tallest item's outer cross.
        var lineCrossSize = !double.IsNaN(crossSize) && crossSize > 0 ? crossSize : maxCrossOuter;

        // ---- step 4: align-items: stretch grows auto-cross items to the line ----
        for (var i = 0; i < items.Length; i++)
        {
            if (props.Align == AlignItems.Stretch && ChildCrossSizeIsAuto(items[i].Box, props))
                items[i].CrossSize = Math.Max(0, lineCrossSize - items[i].CrossPad);
        }

        // ---- step 5: lay each item's contents at the chosen content size ----
        for (var i = 0; i < items.Length; i++)
            LayoutItemContents(items[i], props);

        // ---- step 6: position items along main + cross axes ----
        var usedMain = gapTotal;
        for (var i = 0; i < items.Length; i++) usedMain += items[i].MainSize + items[i].MainPad;

        var (leadingMain, betweenMain) = ResolveMainAxisSpacing(props.Justify, mainSize, usedMain, items.Length, props.MainGap);

        // Compute main-axis positions in "logical" order (item[0] at the
        // main-start edge). For non-reverse directions logical == visual.
        var logicalPositions = new double[items.Length];
        {
            var cursor = leadingMain;
            for (var i = 0; i < items.Length; i++)
            {
                logicalPositions[i] = cursor;
                cursor += items[i].MainSize + items[i].MainPad + betweenMain;
            }
        }

        for (var paintIdx = 0; paintIdx < items.Length; paintIdx++)
        {
            var item = items[paintIdx];
            // Frame spans the border box (content + padding + border).
            var mainExtent = item.MainSize + item.MainPad;
            var crossExtent = item.CrossSize + item.CrossPad;

            // For *-reverse the main-start edge is on the right (row-reverse)
            // or bottom (column-reverse). Mirror the logical position across
            // the container's main axis so item[0] ends up at the far end of
            // the container while paint order (children[0..n]) is preserved.
            var logical = logicalPositions[paintIdx];
            var mainPos = props.IsReverse ? mainSize - logical - mainExtent : logical;
            var cross = ResolveCrossOffset(props.Align, lineCrossSize, crossExtent);

            item.Box.Frame = props.IsRow
                ? new Rect(mainPos, cross, mainExtent, crossExtent)
                : new Rect(cross, mainPos, crossExtent, mainExtent);
        }

        return props.IsRow ? lineCrossSize : usedMain;
    }

    private struct Item
    {
        public Box.Box Box;
        public FlexItemProps Props;
        public double Basis;
        public double MainSize;
        public double CrossSize;
        /// <summary>Main-axis padding + border (both edges), in px.</summary>
        public double MainPad;
        /// <summary>Cross-axis padding + border (both edges), in px.</summary>
        public double CrossPad;
    }

    /// <summary>
    /// Resolve a child's flex-basis to pixels. <c>flex-basis: auto</c> falls
    /// back to the child's main-axis size property (width for row, height for
    /// column); when that is also <c>auto</c>, we approximate the content size
    /// at "infinite" width using a block measurement pass — the same
    /// max-content trick the inline-block sub-pass uses.
    /// </summary>
    private double ResolveBasis(Box.Box child, double? parsedBasis, FlexContainerProps props, double containerWidth, double? containerHeight)
    {
        if (parsedBasis is { } basis)
            return Math.Max(0, basis);

        var mainProperty = props.IsRow ? PropertyId.Width : PropertyId.Height;
        var mainBasis = props.IsRow ? containerWidth : (containerHeight ?? _viewport.Height);
        var explicitMain = BlockLayout.ResolveLength(child.Style, mainProperty, mainBasis, _viewport, allowAuto: true);
        if (explicitMain is { } main)
            return Math.Max(0, main);

        // auto + no explicit width/height: use content size.
        return MeasureContentMainSize(child, props, containerWidth);
    }

    /// <summary>
    /// Approximate the child's max-content main size. For row direction we
    /// run a block measurement pass at very wide width and take the consumed
    /// width; for column we just run block layout at the available width and
    /// return its consumed height. This matches what
    /// <see cref="Inline.InlineLayout"/> does for inline-block shrink-to-fit.
    /// </summary>
    private double MeasureContentMainSize(Box.Box child, FlexContainerProps props, double containerWidth)
    {
        if (props.IsRow)
        {
            const double measureWidth = 1_000_000d;
            _block.LayoutItem(child, measureWidth, null, measure: true);
            // LayoutItem returns consumed height; we want consumed width.
            return Math.Min(containerWidth, UsedMainWidth(child));
        }
        // Column direction: measure the child's natural height at the
        // container width.
        return _block.LayoutItem(child, containerWidth, null, measure: true);
    }

    /// <summary>
    /// Rightmost content edge of a measured item, in its own content-box space.
    /// For a nested flex container the item's content lives on its flex
    /// children's frames (positioned along the main axis), so the extent is the
    /// max child right edge — <see cref="MeasureUsedWidth"/>'s text-fragment
    /// walk would miss those per-item offsets and under-measure the row.
    /// </summary>
    private static double UsedMainWidth(Box.Box box)
    {
        // An AnonymousBlockBox inherits its parent's ComputedStyle (for text
        // inheritance), so its Display can read "flex"/"inline-flex" even though
        // it is really an inline-run wrapper — never treat it as a flex
        // container, or this would read its (frame-less) text children as items
        // and report width 0.
        if (box.Kind == BoxKind.AnonymousBlock || !BlockLayout.IsFlexContainer(box.Style))
            return MeasureUsedWidth(box);
        double max = 0;
        foreach (var item in box.Children)
            max = Math.Max(max, item.Frame.X + item.Frame.Width);
        return max;
    }

    private static double MeasureUsedWidth(Box.Box box)
    {
        double max = 0;
        Walk(box);
        return max;

        // Mirror Inline.InlineLayout.MeasureUsedWidth: take the rightmost edge
        // of *content* (text fragments, replaced boxes, atomic inlines) only.
        // Block/anonymous container frames are sized to the measurement width
        // (1,000,000px) during the max-content pass, so counting their
        // Frame.Width here would report the measurement width as the item's
        // content size — the bug that made every flex item balloon to the
        // container width. We descend through containers but never read their
        // own frame.
        void Walk(Box.Box node)
        {
            switch (node)
            {
                case TextBox tb:
                    foreach (var frag in tb.Fragments)
                        max = Math.Max(max, frag.X + frag.Width);
                    return;
                case ImageBox img:
                    max = Math.Max(max, img.Frame.X + img.Frame.Width);
                    return;
                case InlineBox ib when ib != box
                    && ib.Style?.Get(PropertyId.Display) is CssKeyword { Name: "inline-block" }:
                    max = Math.Max(max, ib.Frame.X + ib.Frame.Width);
                    return;
            }
            foreach (var child in node.Children) Walk(child);
        }
    }

    /// <summary>Measure the cross-axis natural size of an item that hasn't
    /// been told to stretch.</summary>
    private double MeasureCrossSize(Box.Box child, FlexContainerProps props, double itemMainSize, double containerWidth, double containerCross)
    {
        var crossProperty = props.IsRow ? PropertyId.Height : PropertyId.Width;
        var crossBasis = props.IsRow ? (double.IsNaN(containerCross) ? _viewport.Height : containerCross) : containerWidth;
        var explicitCross = BlockLayout.ResolveLength(child.Style, crossProperty, crossBasis, _viewport, allowAuto: true);
        if (explicitCross is { } c) return Math.Max(0, c);

        // Auto cross size: lay out the child at the chosen main size and let
        // its natural content height settle. For row direction the child's
        // content width is itemMainSize; the consumed block height becomes
        // the natural cross size.
        if (props.IsRow)
        {
            return _block.LayoutItem(child, itemMainSize, null, measure: true);
        }
        else
        {
            const double measureWidth = 1_000_000d;
            _block.LayoutItem(child, measureWidth, null, measure: true);
            return Math.Min(containerWidth, UsedMainWidth(child));
        }
    }

    private static bool ChildCrossSizeIsAuto(Box.Box child, FlexContainerProps props)
    {
        var crossProperty = props.IsRow ? PropertyId.Height : PropertyId.Width;
        if (child.Style is null) return true;
        return child.Style.Get(crossProperty) is CssKeyword { Name: "auto" };
    }

    /// <summary>
    /// Re-lay the item's box at its final main+cross sizes. After main-axis
    /// distribution the child's content width is decided, so a final block
    /// pass at that width settles its descendants' frames.
    /// </summary>
    private void LayoutItemContents(Item item, FlexContainerProps props)
    {
        var child = item.Box;
        // MainSize / CrossSize are already content-box sizes (see step 1), so
        // they map straight to the block pass's content width/height — no
        // padding subtraction here (that's what double-counted before).
        var (contentWidth, contentHeight) = props.IsRow
            ? (item.MainSize, item.CrossSize)
            : (item.CrossSize, item.MainSize);
        // The item's used cross/main sizes are definite by the time we get
        // here (resolved by main-axis distribution + align-items: stretch),
        // so descendants with `height: 100%` should see the item's content
        // height as their containing block — not collapse to 0. Without this
        // a flex chain like  navbar(height:60) > brand(height:100%) >
        // logo(height:100%, background-image)  renders the logo as zero.
        _block.LayoutItem(child, Math.Max(0, contentWidth), Math.Max(0, contentHeight));
    }

    /// <summary>
    /// Resolve the leading offset + between-item gap for <c>justify-content</c>.
    /// The <c>gap</c> property is honoured as a minimum between items: the
    /// space-* distributions add on top.
    /// </summary>
    private static (double Leading, double Between) ResolveMainAxisSpacing(JustifyContent justify, double mainSize, double usedMain, int itemCount, double gap)
    {
        var free = Math.Max(0, mainSize - usedMain);
        return justify switch
        {
            JustifyContent.FlexEnd => (free, gap),
            JustifyContent.Center => (free / 2d, gap),
            JustifyContent.SpaceBetween when itemCount > 1
                => (0, gap + free / (itemCount - 1)),
            JustifyContent.SpaceAround when itemCount > 0
                => (free / (itemCount * 2d), gap + free / itemCount),
            JustifyContent.SpaceEvenly when itemCount > 0
                => (free / (itemCount + 1d), gap + free / (itemCount + 1d)),
            _ => (0, gap), // flex-start (and space-* with degenerate item counts)
        };
    }

    private static double ResolveCrossOffset(AlignItems align, double lineCross, double itemCross)
    {
        var free = lineCross - itemCross;
        return align switch
        {
            AlignItems.FlexEnd => free,
            AlignItems.Center => free / 2d,
            AlignItems.Stretch => 0, // already resized to lineCross above
            AlignItems.Baseline => 0, // TODO: real baseline alignment
            _ => 0,
        };
    }

    private void ResolveBoxModel(Box.Box box, double containerWidth)
    {
        // An anonymous flex item (a bare-text run wrapper) inherits the
        // container's ComputedStyle for text properties, but box-model
        // properties are not inherited — they take their initial value of 0.
        // Without this it would pick up the container's own padding/border (e.g.
        // a padded button), double-applying it and over-sizing the item.
        if (box.Kind == BoxKind.AnonymousBlock)
        {
            box.Margin = Edges.Zero;
            box.Padding = Edges.Zero;
            box.Border = Edges.Zero;
            return;
        }

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

        box.Border = new Edges(
            BlockLayout.ResolveBorderWidth(box.Style, PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, _viewport),
            BlockLayout.ResolveBorderWidth(box.Style, PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, _viewport),
            BlockLayout.ResolveBorderWidth(box.Style, PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, _viewport),
            BlockLayout.ResolveBorderWidth(box.Style, PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, _viewport));
    }
}
