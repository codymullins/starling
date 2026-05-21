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
/// <c>stretch</c> alignment (the default) so items fill their grid area.
/// </summary>
/// <remarks>
/// Out of scope for now (degrade gracefully): explicit line placement
/// (<c>grid-column</c>/<c>grid-row</c>), spanning, named lines/areas,
/// <c>grid-template-rows</c> sizing (rows are content-sized), <c>minmax()</c>
/// (treated as its max), <c>dense</c> packing, and non-stretch alignment.
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
        var items = container.Children;
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

        // Resolve each item's box model + measure row heights (content-sized
        // rows). MainPad/CrossPad are tracked separately so item content sizes
        // stay content-box (see FlexLayout for the same reasoning).
        var pad = new (double H, double V)[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            ResolveBoxModel(item, containerWidth);
            pad[i] = (item.Padding.Horizontal + item.Border.Horizontal,
                      item.Padding.Vertical + item.Border.Vertical);

            var col = i % numCols;
            var contentW = Math.Max(0, colWidths[col] - pad[i].H);
            var contentH = _block.LayoutItem(item, contentW, null, measure: true);
            var outerH = contentH + pad[i].V;
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

        // Final placement: items stretch to fill their grid area (the default
        // justify-items/align-items: stretch).
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var col = i % numCols;
            var row = i / numCols;
            var cellW = colWidths[col];
            var cellH = rowHeights[row];
            var contentW = Math.Max(0, cellW - pad[i].H);
            var contentH = Math.Max(0, cellH - pad[i].V);
            _block.LayoutItem(item, contentW, contentH);
            item.Frame = new Rect(colX[col], rowY[row], cellW, cellH);
        }

        return explicitHeight ?? totalHeight;
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
