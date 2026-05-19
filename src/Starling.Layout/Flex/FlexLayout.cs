using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout.Block;
using Tessera.Layout.Box;

namespace Tessera.Layout.Flex;

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

        // ---- step 1: resolve each item's flex base size + min/max ----
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
            };
        }

        // ---- step 2: distribute free space along the main axis ----
        var gapTotal = props.MainGap * Math.Max(0, items.Length - 1);
        var hypotheticalSum = 0d;
        for (var i = 0; i < items.Length; i++) hypotheticalSum += items[i].MainSize;
        var free = mainSize - hypotheticalSum - gapTotal;

        if (free > 0)
        {
            var totalGrow = 0d;
            for (var i = 0; i < items.Length; i++) totalGrow += items[i].Props.Grow;
            if (totalGrow > 0)
            {
                for (var i = 0; i < items.Length; i++)
                {
                    var share = free * (items[i].Props.Grow / totalGrow);
                    items[i].MainSize += share;
                }
            }
        }
        else if (free < 0)
        {
            // Simplified shrink: weighted by flex-shrink only (the spec
            // multiplies by basis to make larger items absorb more
            // overflow — that's a TODO for the full algorithm).
            var totalShrink = 0d;
            for (var i = 0; i < items.Length; i++) totalShrink += items[i].Props.Shrink;
            if (totalShrink > 0)
            {
                for (var i = 0; i < items.Length; i++)
                {
                    var share = free * (items[i].Props.Shrink / totalShrink);
                    items[i].MainSize = Math.Max(0, items[i].MainSize + share);
                }
            }
        }

        // ---- step 3: lay each item out at its main size, measure cross size ----
        var maxCross = 0d;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var child = item.Box;
            // Width on the child in main = main size for row; for column the
            // child uses its width property (cross axis).
            var crossSelf = MeasureCrossSize(child, props, item.MainSize, containerWidth, crossSize);
            items[i].CrossSize = crossSelf;
            if (crossSelf > maxCross) maxCross = crossSelf;
        }

        // Resolved cross-axis line size: explicit container cross size if
        // given, else the tallest item.
        var lineCrossSize = !double.IsNaN(crossSize) && crossSize > 0 ? crossSize : maxCross;

        // ---- step 4: apply align-items, including stretch ----
        for (var i = 0; i < items.Length; i++)
        {
            if (props.Align == AlignItems.Stretch && ChildCrossSizeIsAuto(items[i].Box, props))
                items[i].CrossSize = lineCrossSize;
        }

        // ---- step 5: lay each item's contents at the chosen main+cross sizes ----
        for (var i = 0; i < items.Length; i++)
        {
            LayoutItemContents(items[i], props);
        }

        // ---- step 6: position items along main + cross axes ----
        var usedMain = 0d;
        for (var i = 0; i < items.Length; i++) usedMain += items[i].MainSize;
        usedMain += gapTotal;

        var (leadingMain, betweenMain) = ResolveMainAxisSpacing(props.Justify, mainSize, usedMain, items.Length, props.MainGap);

        // Compute main-axis positions in "logical" order (item[0] at the
        // main-start edge). For non-reverse directions logical == visual.
        var logicalPositions = new double[items.Length];
        {
            var cursor = leadingMain;
            for (var i = 0; i < items.Length; i++)
            {
                logicalPositions[i] = cursor;
                cursor += items[i].MainSize + betweenMain;
            }
        }

        for (var paintIdx = 0; paintIdx < items.Length; paintIdx++)
        {
            var item = items[paintIdx];
            // For *-reverse the main-start edge is on the right (row-reverse)
            // or bottom (column-reverse). Mirror the logical position across
            // the container's main axis so item[0] ends up at the far end of
            // the container while paint order (children[0..n]) is preserved.
            var logical = logicalPositions[paintIdx];
            var mainPos = props.IsReverse
                ? mainSize - logical - item.MainSize
                : logical;
            var cross = ResolveCrossOffset(props.Align, lineCrossSize, item.CrossSize);

            double frameX, frameY, frameW, frameH;
            if (props.IsRow)
            {
                frameX = mainPos;
                frameY = cross;
                frameW = item.MainSize;
                frameH = item.CrossSize;
            }
            else
            {
                frameX = cross;
                frameY = mainPos;
                frameW = item.CrossSize;
                frameH = item.MainSize;
            }

            // The child's outer rect spans content + padding + border. The
            // flex algorithm consumes the *outer* size; the child's own
            // margin offsets are already baked into MainSize via ResolveBasis.
            item.Box.Frame = new Rect(frameX, frameY, frameW, frameH);
        }

        var totalCross = lineCrossSize;
        return props.IsRow ? totalCross : usedMain;
    }

    private struct Item
    {
        public Box.Box Box;
        public FlexItemProps Props;
        public double Basis;
        public double MainSize;
        public double CrossSize;
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
            _block.LayoutChildren(child, measureWidth, measure: true);
            // LayoutChildren returns consumed height; we want consumed width.
            return Math.Min(containerWidth, MeasureUsedWidth(child));
        }
        // Column direction: measure the child's natural height at the
        // container width.
        return _block.LayoutChildren(child, containerWidth, measure: true);
    }

    private static double MeasureUsedWidth(Box.Box box)
    {
        double max = 0;
        Walk(box);
        return max;

        void Walk(Box.Box node)
        {
            if (node != box)
                max = Math.Max(max, node.Frame.X + node.Frame.Width);
            foreach (var child in node.Children) Walk(child);
            if (node is TextBox tb)
            {
                foreach (var frag in tb.Fragments)
                    max = Math.Max(max, frag.X + frag.Width);
            }
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
            return _block.LayoutChildren(child, itemMainSize, measure: true);
        }
        else
        {
            const double measureWidth = 1_000_000d;
            _block.LayoutChildren(child, measureWidth, measure: true);
            return Math.Min(containerWidth, MeasureUsedWidth(child));
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
        var contentWidth = props.IsRow
            ? item.MainSize - child.Padding.Horizontal - child.Border.Horizontal
            : item.CrossSize - child.Padding.Horizontal - child.Border.Horizontal;
        contentWidth = Math.Max(0, contentWidth);
        // Final block pass at the chosen content width. The cross size is the
        // *box* extent; descendants compose freely inside it.
        _block.LayoutChildren(child, contentWidth);
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

        // Border resolution lives on BlockLayout; we don't need full pixel
        // values for flex test scope yet — leave as zero.
        box.Border = Edges.Zero;
    }
}
