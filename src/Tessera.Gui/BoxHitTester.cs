using Tessera.Layout.Box;
using DomElement = Tessera.Dom.Element;

namespace Tessera.Gui;

/// <summary>
/// Re-derives interaction from the laid-out box tree now that the GUI paints a
/// single flat Skia bitmap instead of a native MAUI view tree. Everything
/// <c>BoxTreeRenderer</c> used to get from per-<c>Label</c> gesture recognizers
/// — hover, link activation, drag-select, Cmd-F — is recovered here by walking
/// the box tree in document-space CSS px and hit-testing pointer coordinates
/// against it.
/// </summary>
/// <remarks>
/// Coordinates are document-space: the same space the page bitmap is rendered
/// in. The caller maps a pointer position inside the scrolled image view into
/// this space (image-local coordinates already are document-space, since the
/// bitmap is sized to the full document).
/// </remarks>
public static class BoxHitTester
{
    /// <summary>A text fragment with its absolute (document-space) rectangle.</summary>
    public readonly record struct PlacedFragment(
        double X, double Y, double Width, double Height, string Text);

    /// <summary>The result of hit-testing a point against the box tree.</summary>
    /// <param name="Box">The innermost box containing the point, if any.</param>
    /// <param name="LinkAnchor">
    /// The nearest enclosing <c>&lt;a&gt;</c> element, if the point is inside a
    /// hyperlink. Drives both link activation and the <c>:hover</c> re-cascade.
    /// </param>
    public readonly record struct HitResult(Box? Box, DomElement? LinkAnchor)
    {
        public bool IsHit => Box is not null;
    }

    /// <summary>
    /// Finds the innermost box containing (<paramref name="x"/>,
    /// <paramref name="y"/>) and the nearest enclosing link anchor. Returns an
    /// empty result when the point misses every painted box.
    /// </summary>
    public static HitResult HitTest(BlockBox root, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(root);
        var hit = FindDeepest(root, x, y, originX: 0, originY: 0);
        if (hit is null)
            return new HitResult(null, null);
        return new HitResult(hit, FindLinkAnchor(hit));
    }

    private static Box? FindDeepest(Box box, double x, double y, double originX, double originY)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        var insideSelf = x >= frameX && x < frameX + box.Frame.Width
            && y >= frameY && y < frameY + box.Frame.Height;

        // Text fragments are positioned in the enclosing block's content area;
        // a TextBox's own Frame may be zero-sized, so test its fragments too.
        if (box is TextBox tb)
        {
            foreach (var frag in tb.Fragments)
            {
                if (string.IsNullOrWhiteSpace(frag.Text)) continue;
                var fx = frameX + frag.X;
                var fy = frameY + frag.Y;
                if (x >= fx && x < fx + frag.Width && y >= fy && y < fy + frag.Height)
                    return box;
            }
            return insideSelf ? box : null;
        }

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;

        // Last child wins ties — later siblings paint on top.
        for (var i = box.Children.Count - 1; i >= 0; i--)
        {
            var childHit = FindDeepest(box.Children[i], x, y, contentX, contentY);
            if (childHit is not null)
                return childHit;
        }

        return insideSelf ? box : null;
    }

    /// <summary>
    /// Walks a box's ancestors looking for an enclosing <c>&lt;a&gt;</c>. The
    /// inline formatter flattens span/anchor wrappers for layout but preserves
    /// the parent chain, so the anchor element (and its href) is recoverable
    /// without re-walking the DOM. Returning the element keeps it usable for
    /// the <c>:hover</c> re-cascade via the style engine.
    /// </summary>
    public static DomElement? FindLinkAnchor(Box box)
    {
        ArgumentNullException.ThrowIfNull(box);
        for (var node = (Box?)box; node is not null; node = node.Parent)
        {
            if (node is InlineBox ib && ib.Element is DomElement { LocalName: "a" } a)
                return a;
            if (node.Element is DomElement { LocalName: "a" } el)
                return el;
        }
        return null;
    }

    /// <summary>
    /// Collects every non-blank text fragment in the tree with its absolute
    /// document-space rectangle, in document order. Used both for the Cmd-F
    /// find index and for drag-to-select hit-testing.
    /// </summary>
    public static List<PlacedFragment> CollectFragments(BlockBox root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new List<PlacedFragment>();
        Collect(root, originX: 0, originY: 0, list);
        return list;
    }

    private static void Collect(Box box, double originX, double originY, List<PlacedFragment> sink)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        if (box is TextBox tb)
        {
            foreach (var frag in tb.Fragments)
            {
                if (string.IsNullOrWhiteSpace(frag.Text)) continue;
                sink.Add(new PlacedFragment(
                    frameX + frag.X, frameY + frag.Y, frag.Width, frag.Height, frag.Text));
            }
            return;
        }

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Collect(child, contentX, contentY, sink);
    }
}
