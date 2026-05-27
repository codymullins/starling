using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout.Block;
using Starling.Layout.Box;

namespace Starling.Layout.Grid;

/// <summary>
/// Minimal CSS Grid layout (CSS Grid Layout Module Level 1, simplified).
/// Supports the common explicit-column / auto-flow case that real-world page
/// shells use: <c>grid-template-columns</c> with <c>&lt;length&gt;</c>,
/// <c>&lt;percentage&gt;</c>, <c>fr</c>, <c>auto</c>, and <c>repeat()</c>;
/// <c>gap</c>/<c>row-gap</c>/<c>column-gap</c>; row-major auto-placement; and
/// box alignment — <c>stretch</c> (the default, items fill their grid area)
/// plus <c>start</c>/<c>center</c>/<c>end</c> via <c>justify-items</c>/
/// <c>justify-self</c> (inline axis) and <c>align-items</c>/<c>align-self</c>
/// (block axis), which size the item to its content and position it in the cell.
/// </summary>
/// <remarks>
/// Out of scope for now (degrade gracefully): explicit line placement
/// (<c>grid-column</c>/<c>grid-row</c>), spanning, named lines/areas,
/// <c>grid-template-rows</c> sizing (rows are content-sized), <c>minmax()</c>
/// (treated as its max), <c>dense</c> packing, <c>baseline</c> alignment
/// (falls back to <c>stretch</c>), and <c>auto</c> margins.
/// Coordinate convention matches <see cref="BlockLayout"/> / flex: each item's
/// <see cref="Box.Box.Frame"/> is in the container's content-box space.
/// </remarks>
internal sealed class GridLayout
{
    private readonly BlockLayout _block;
    private readonly Size _viewport;

    public GridLayout(BlockLayout block, Size viewport)
    {
        _block = block;
        _viewport = viewport;
    }

    public double Layout(Box.Box container, double containerWidth, double? explicitHeight)
    {
        // CSS Grid §9: absolutely/fixed-positioned children are not grid items
        // — they're excluded from placement/sizing and positioned later by
        // PositionLayout. Mirror FlexLayout so a hidden `position: fixed`
        // overlay doesn't occupy a grid cell.
        var items = new List<Box.Box>(container.Children.Count);
        foreach (var c in container.Children)
            if (!BlockLayout.IsOutOfFlow(c.Style)) items.Add(c);
        if (items.Count == 0)
            return explicitHeight ?? 0;

        var columnGap = ResolveGap(container.Style, PropertyId.ColumnGap, containerWidth);
        var rowGap = ResolveGap(container.Style, PropertyId.RowGap, _viewport.Height);

        var tracks = ParseTracks(container.Style, PropertyId.GridTemplateColumns);
        if (tracks.Count == 0)
            tracks.Add(new Track(TrackKind.Fr, 1)); // implicit single auto column

        var colWidths = ResolveColumnWidths(tracks, containerWidth, columnGap);
        var numCols = colWidths.Length;

        // Column x offsets (content-box space).
        var colX = new double[numCols];
        var cursor = 0d;
        for (var c = 0; c < numCols; c++)
        {
            colX[c] = cursor;
            cursor += colWidths[c] + columnGap;
        }

        var numRows = (items.Count + numCols - 1) / numCols;
        var rowHeights = new double[numRows];

        // Resolve each item's box model, its inline/block alignment, and its
        // content-box sizes. Non-stretch items (justify-items/justify-self or
        // align-items/align-self of start/center/end) are sized to their own
        // content (max-content inline, natural block) rather than stretched to
        // fill the grid area; stretch (the default) keeps the fill behaviour.
        // MainPad/CrossPad-style tracking keeps content sizes content-box (see
        // FlexLayout for the same reasoning).
        var pad = new (double H, double V)[items.Count];
        var jAlign = new Align[items.Count];
        var aAlign = new Align[items.Count];
        var inlineContent = new double[items.Count];
        var blockContent = new double[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            ResolveBoxModel(item, containerWidth);
            pad[i] = (item.Padding.Horizontal + item.Border.Horizontal,
                      item.Padding.Vertical + item.Border.Vertical);
            jAlign[i] = ResolveInlineAlign(container.Style, item.Style);
            aAlign[i] = ResolveBlockAlign(container.Style, item.Style);

            var col = i % numCols;
            var availInline = Math.Max(0, colWidths[col] - pad[i].H);

            // Inline (column-axis) content size.
            if (jAlign[i] == Align.Stretch)
            {
                inlineContent[i] = availInline;
            }
            else
            {
                var explicitW = BlockLayout.ResolveLength(item.Style, PropertyId.Width, colWidths[col], _viewport, allowAuto: true);
                inlineContent[i] = explicitW ?? MeasureMaxContentWidth(item, availInline);
            }

            // Block (row-axis) natural content size, measured at the chosen
            // inline width. An explicit height wins; a percentage height has no
            // definite grid-area basis here (rows are content-sized) so it
            // collapses to auto and we measure.
            var explicitH = BlockLayout.ResolveHeight(item.Style, PropertyId.Height, null, _viewport, allowAuto: true);
            blockContent[i] = explicitH ?? _block.LayoutItem(item, inlineContent[i], null, measure: true);

            var outerH = blockContent[i] + pad[i].V;
            var row = i / numCols;
            if (outerH > rowHeights[row]) rowHeights[row] = outerH;
        }

        // Row y offsets.
        var rowY = new double[numRows];
        var y = 0d;
        for (var r = 0; r < numRows; r++)
        {
            rowY[r] = y;
            y += rowHeights[r] + rowGap;
        }
        var totalHeight = numRows > 0 ? rowY[numRows - 1] + rowHeights[numRows - 1] : 0;

        // Final placement. Stretch items fill the grid area (the default);
        // start/center/end items keep their content size and are positioned
        // within the cell along each axis.
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var col = i % numCols;
            var row = i / numCols;
            var cellW = colWidths[col];
            var cellH = rowHeights[row];

            var contentW = jAlign[i] == Align.Stretch ? Math.Max(0, cellW - pad[i].H) : inlineContent[i];
            var contentH = aAlign[i] == Align.Stretch ? Math.Max(0, cellH - pad[i].V) : blockContent[i];
            _block.LayoutItem(item, contentW, contentH);

            var outerW = contentW + pad[i].H;
            var outerH = contentH + pad[i].V;
            var xOff = AlignOffset(jAlign[i], cellW, outerW);
            var yOff = AlignOffset(aAlign[i], cellH, outerH);
            item.Frame = new Rect(colX[col] + xOff, rowY[row] + yOff, outerW, outerH);
        }

        return explicitHeight ?? totalHeight;
    }

    /// <summary>Self-alignment of a grid item along one axis.</summary>
    private enum Align { Stretch, Start, Center, End }

    /// <summary>
    /// Inline-axis (column) alignment: <c>justify-self</c> falls back to the
    /// container's <c>justify-items</c> when <c>auto</c>. The grid initial
    /// <c>legacy</c>/<c>normal</c> behaves as <c>stretch</c>.
    /// </summary>
    private static Align ResolveInlineAlign(ComputedStyle? container, ComputedStyle? item)
    {
        var self = Keyword(item, PropertyId.JustifySelf);
        if (self is null or "auto") self = Keyword(container, PropertyId.JustifyItems);
        return MapAlign(self);
    }

    /// <summary>
    /// Block-axis (row) alignment: <c>align-self</c> falls back to the
    /// container's <c>align-items</c> when <c>auto</c>. The grid initial
    /// <c>normal</c> behaves as <c>stretch</c>.
    /// </summary>
    private static Align ResolveBlockAlign(ComputedStyle? container, ComputedStyle? item)
    {
        var self = Keyword(item, PropertyId.AlignSelf);
        if (self is null or "auto") self = Keyword(container, PropertyId.AlignItems);
        return MapAlign(self);
    }

    private static string? Keyword(ComputedStyle? style, PropertyId id)
        => (style?.Get(id) as CssKeyword)?.Name.ToLowerInvariant();

    private static Align MapAlign(string? keyword) => keyword switch
    {
        "center" => Align.Center,
        "start" or "flex-start" or "self-start" or "left" => Align.Start,
        "end" or "flex-end" or "self-end" or "right" => Align.End,
        // "stretch", "normal", "legacy", "baseline" (no baseline support), null.
        _ => Align.Stretch,
    };

    private static double AlignOffset(Align align, double cell, double outer) => align switch
    {
        Align.Center => (cell - outer) / 2d,
        Align.End => cell - outer,
        _ => 0, // Start and Stretch both anchor at the cell start.
    };

    /// <summary>
    /// Max-content inline size of a non-stretch item, clamped to the available
    /// cell width. Mirrors <see cref="Flex.FlexLayout"/>'s shrink-to-fit
    /// measurement: lay the item out at an effectively infinite width and take
    /// its rightmost content edge.
    /// </summary>
    private double MeasureMaxContentWidth(Box.Box item, double availInline)
    {
        const double measureWidth = 1_000_000d;
        _block.LayoutItem(item, measureWidth, null, measure: true);
        return Math.Min(availInline, UsedContentWidth(item));
    }

    /// <summary>
    /// Rightmost content edge of a measured item, in its own content-box space.
    /// A flex/grid item carries its content on its children's positioned frames,
    /// so read those directly; otherwise walk for text/replaced/inline-block
    /// fragments (block/anonymous container frames are sized to the measurement
    /// width and must not be counted). Mirrors FlexLayout.UsedMainWidth.
    /// </summary>
    private static double UsedContentWidth(Box.Box box)
    {
        if (box.Kind != BoxKind.AnonymousBlock
            && (BlockLayout.IsFlexContainer(box.Style) || BlockLayout.IsGridContainer(box.Style)))
        {
            double max = 0;
            foreach (var child in box.Children)
                max = Math.Max(max, child.Frame.X + child.Frame.Width);
            return max;
        }

        double edge = 0;
        Walk(box);
        return edge;

        void Walk(Box.Box node)
        {
            switch (node)
            {
                case TextBox tb:
                    foreach (var frag in tb.Fragments) edge = Math.Max(edge, frag.X + frag.Width);
                    return;
                case ImageBox img:
                    edge = Math.Max(edge, img.Frame.X + img.Frame.Width);
                    return;
                case InlineBox ib when ib != box
                    && ib.Style?.Get(PropertyId.Display) is CssKeyword { Name: "inline-block" }:
                    edge = Math.Max(edge, ib.Frame.X + ib.Frame.Width);
                    return;
            }
            foreach (var child in node.Children) Walk(child);
        }
    }

    private enum TrackKind { Fixed, Percent, Fr, Auto }

    private readonly record struct Track(TrackKind Kind, double Value);

    /// <summary>Resolve track sizes to pixel widths along the inline axis.</summary>
    private static double[] ResolveColumnWidths(List<Track> tracks, double containerWidth, double gap)
    {
        var n = tracks.Count;
        var widths = new double[n];
        var gapTotal = gap * Math.Max(0, n - 1);
        var available = Math.Max(0, containerWidth - gapTotal);

        var usedFixed = 0d;
        var totalFr = 0d;
        for (var i = 0; i < n; i++)
        {
            switch (tracks[i].Kind)
            {
                case TrackKind.Fixed:
                    widths[i] = tracks[i].Value;
                    usedFixed += widths[i];
                    break;
                case TrackKind.Percent:
                    widths[i] = containerWidth * tracks[i].Value / 100d;
                    usedFixed += widths[i];
                    break;
                case TrackKind.Fr:
                    totalFr += tracks[i].Value;
                    break;
                case TrackKind.Auto:
                    // Auto behaves like 1fr when free space exists — a pragmatic
                    // approximation that matches typical equal-ish columns.
                    totalFr += 1;
                    break;
            }
        }

        var remaining = Math.Max(0, available - usedFixed);
        var frUnit = totalFr > 0 ? remaining / totalFr : 0;
        for (var i = 0; i < n; i++)
        {
            if (tracks[i].Kind == TrackKind.Fr) widths[i] = tracks[i].Value * frUnit;
            else if (tracks[i].Kind == TrackKind.Auto) widths[i] = frUnit;
        }
        return widths;
    }

    private static List<Track> ParseTracks(ComputedStyle? style, PropertyId id)
    {
        var result = new List<Track>();
        if (style is null) return result;
        AppendTracks(style.Get(id), result);
        return result;
    }

    private static void AppendTracks(CssValue? value, List<Track> into)
    {
        switch (value)
        {
            case CssValueList list:
                foreach (var v in list.Values) AppendTracks(v, into);
                break;
            case CssFunctionValue { Name: "repeat" } f when f.Arguments.Count >= 2:
                // repeat(<count>, <track>+) — expand to <count> copies of the
                // body tracks. Only integer counts are handled (auto-fit/fill
                // need container measurement we don't model yet).
                if (f.Arguments[0] is CssNumber { Value: var cnt } && cnt >= 1)
                {
                    var body = new List<Track>();
                    for (var a = 1; a < f.Arguments.Count; a++) AppendTracks(f.Arguments[a], body);
                    for (var r = 0; r < (int)cnt; r++) into.AddRange(body);
                }
                break;
            case CssFunctionValue { Name: "minmax" } f when f.Arguments.Count == 2:
                // Approximate minmax(a, b) by its max track.
                AppendTracks(f.Arguments[1], into);
                break;
            case CssDimension { Unit: "fr" } d:
                into.Add(new Track(TrackKind.Fr, d.Value));
                break;
            case CssLength len:
                into.Add(new Track(TrackKind.Fixed, BlockLayout.ToPx(len)));
                break;
            case CssPercentage pct:
                into.Add(new Track(TrackKind.Percent, pct.Value));
                break;
            case CssKeyword { Name: "auto" }:
                into.Add(new Track(TrackKind.Auto, 0));
                break;
                // "none" and anything else: contribute no tracks.
        }
    }

    private double ResolveGap(ComputedStyle? style, PropertyId id, double basis)
    {
        if (style is null) return 0;
        return style.Get(id) switch
        {
            CssKeyword { Name: "normal" } => 0,
            CssLength len => BlockLayout.ToPx(len, _viewport),
            CssPercentage pct => basis * pct.Value / 100d,
            CssNumber n => n.Value,
            _ => 0,
        };
    }

    private void ResolveBoxModel(Box.Box box, double containerWidth)
    {
        // Anonymous items (bare-text wrappers) take initial box-model values;
        // they must not inherit the container's padding/border via style.
        if (box.Kind == BoxKind.AnonymousBlock)
        {
            box.Margin = Edges.Zero;
            box.Padding = Edges.Zero;
            box.Border = Edges.Zero;
            return;
        }

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

        box.Margin = Edges.Zero; // grid item margins are rare on these shells; ignore for now.
    }
}
