using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout.Box;
using Starling.Layout.Text;
using DomElement = Starling.Dom.Element;
using DomNode = Starling.Dom.Node;

namespace Starling.Gui;

/// <summary>
/// Re-derives interaction from the laid-out box tree now that the GUI paints a
/// single flat bitmap instead of a native MAUI view tree. Everything
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
    /// <summary>
    /// A text fragment with its absolute (document-space) rectangle. The
    /// optional <see cref="Shaped"/> carries the glyph-level pen positions
    /// produced at layout time so the selection model can map a pointer X
    /// onto a character offset inside the fragment without re-shaping.
    /// </summary>
    public readonly record struct PlacedFragment(
        double X, double Y, double Width, double Height, string Text,
        ShapedRun? Shaped = null);

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
            // Include whitespace fragments so a click on the space between two
            // words inside a link still hits the link, and so drag-selection
            // covers the inter-word gaps instead of skipping over them.
            foreach (var frag in tb.Fragments)
            {
                if (frag.Width <= 0) continue;
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
    /// Resolves the CSS <c>cursor</c> keyword for <paramref name="hit"/>.
    /// Walks the box tree from the hit upward picking the first computed
    /// <c>cursor</c> that isn't <c>auto</c>; if every ancestor is <c>auto</c>
    /// (the default), falls back to HTML-semantic defaults:
    /// <list type="bullet">
    /// <item><c>&lt;a href&gt;</c> → <c>pointer</c></item>
    /// <item>text content (a <c>TextBox</c> hit) → <c>text</c></item>
    /// <item>everything else → <c>default</c></item>
    /// </list>
    /// Returns one of the CSS3 UI keyword strings (<c>default</c>,
    /// <c>pointer</c>, <c>text</c>, <c>not-allowed</c>, etc.). Callers map it
    /// to a platform cursor.
    /// </summary>
    public static string ResolveCursor(HitResult hit)
    {
        if (hit.Box is null) return "default";

        // 1) Author-supplied cursor wins. Walk up the box chain (which also
        //    follows the DOM ancestor chain for non-anonymous boxes) and stop
        //    on the first non-auto keyword.
        for (var node = (Box?)hit.Box; node is not null; node = node.Parent)
        {
            if (node.Style?.Get(PropertyId.Cursor) is CssKeyword kw
                && !string.Equals(kw.Name, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return kw.Name.ToLowerInvariant();
            }
        }

        // 2) Element-semantic defaults. Anchors with hrefs are pointers; a
        //    direct text-content hit is the I-beam; otherwise the arrow.
        if (hit.LinkAnchor is not null) return "pointer";
        if (hit.Box is TextBox) return "text";

        // Form controls in the DOM ancestor chain pick up their conventional
        // shape (button → default arrow, input/select → text/arrow based on
        // type). Walk a small distance up the DOM in case the textbox's
        // immediate element is anonymous wrapper content.
        for (var n = (DomNode?)hit.Box.Element; n is not null; n = n.ParentNode)
        {
            if (n is not DomElement el) continue;
            switch (el.LocalName)
            {
                case "a":
                    if (!string.IsNullOrEmpty(el.GetAttribute("href")))
                        return "pointer";
                    break;
                case "button":
                case "select":
                case "option":
                    return "default";
                case "input":
                    var type = (el.GetAttribute("type") ?? "text").ToLowerInvariant();
                    return type is "text" or "search" or "email" or "url" or "tel"
                            or "password" or "number"
                        ? "text"
                        : "default";
                case "textarea":
                    return "text";
            }
        }

        return "default";
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
            // Emit whitespace fragments too so selection painting covers the
            // gaps between words; the caller filters them out where empty text
            // would be wrong (copied-text join, find index).
            foreach (var frag in tb.Fragments)
            {
                if (frag.Width <= 0) continue;
                sink.Add(new PlacedFragment(
                    frameX + frag.X, frameY + frag.Y, frag.Width, frag.Height, frag.Text, frag.Shaped));
            }
            return;
        }

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Collect(child, contentX, contentY, sink);
    }
}
