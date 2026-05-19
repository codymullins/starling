using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Tessera.Layout.Box;

namespace Tessera.Layout.Tree;

/// <summary>
/// Builds the layout box tree from a DOM tree + a <see cref="StyleEngine"/>.
/// </summary>
internal sealed class BoxTreeBuilder
{
    private readonly StyleEngine _style;
    private readonly IImageResolver _images;
    private readonly double? _nowMs;

    public BoxTreeBuilder(StyleEngine style, IImageResolver? images = null, double? nowMs = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _images = images ?? NullImageResolver.Instance;
        _nowMs = nowMs;
    }

    private ComputedStyle Compute(Element el, CascadeCache cache)
        => _nowMs is { } t
            ? _style.ComputeWithAnimations(el, t, context: null, cache)
            : _style.Compute(el, context: null, cache);

    public BlockBox Build(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.DocumentElement;
        if (root is null)
            return new BlockBox(style: null, element: null);

        // One cache per box-tree-build pass: the cascade for a given element
        // is identical across every visit during this traversal, so we can
        // memoize and skip the ancestor recursion on the hot path.
        var cache = new CascadeCache();
        // Pre-cascade the whole DOM in parallel by depth. After this, every
        // _style.Compute(element, ..., cache) call below is a cache hit and
        // the box-tree walk is pure CPU-bound traversal. Falls through to
        // sequential for tiny trees (Parallel.ForEach has fixed overhead
        // around ~50μs per partition that beats serial only past ~12 items
        // per depth).
        _style.PrecomputeTree(root, cache);
        var rootStyle = Compute(root, cache);
        var rootBox = new BlockBox(rootStyle, root);
        BuildChildren(root, rootStyle, rootBox, cache);
        WrapInlinesInAnonymousBlocks(rootBox);
        return rootBox;
    }

    private void BuildChildren(Node parentNode, ComputedStyle parentStyle, Box.Box parentBox, CascadeCache cache)
    {
        for (var child = parentNode.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case Element element:
                    var elementStyle = Compute(element, cache);
                    var display = DisplayKeyword(elementStyle);
                    if (display == "none") continue;
                    if (display == "contents")
                    {
                        BuildChildren(element, elementStyle, parentBox, cache);
                        continue;
                    }
                    if (string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase))
                    {
                        BuildImage(element, elementStyle, parentBox);
                        continue;
                    }
                    if (string.Equals(element.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                    {
                        // We don't have an SVG renderer yet. Real browsers paint
                        // the vector graphics; we degrade to the inline element's
                        // accessible name so an icon like Google's logo
                        // (<svg aria-label="Google">) at least surfaces *something*
                        // recognisable instead of an empty box. Path/Rect/etc.
                        // children carry no text and would otherwise vanish.
                        BuildSvgFallback(element, elementStyle, parentBox);
                        continue;
                    }
                    Box.Box box = display == "inline" || display == "inline-block"
                        ? new InlineBox(elementStyle, element)
                        : new BlockBox(elementStyle, element);
                    parentBox.AppendChild(box);
                    BuildChildren(element, elementStyle, box, cache);
                    // <input> is a void element with no DOM children — synthesize
                    // a TextBox from its value/placeholder so the search box and
                    // submit button labels actually show up. Real intrinsic
                    // sizing from the `size` attribute lands with form layout.
                    if (string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase))
                        AppendInputLabel(element, elementStyle, box);
                    break;
                case Tessera.Dom.Text text:
                    var data = text.Data;
                    if (data.Length == 0) continue;
                    var textBox = new TextBox(data, parentStyle);
                    parentBox.AppendChild(textBox);
                    break;
            }
        }
    }

    /// <summary>
    /// CSS 2.2 §9.2.1.1: when a block container has both block and inline
    /// children, runs of consecutive inline children are wrapped in anonymous
    /// block boxes so block layout sees a uniform list of blocks.
    /// </summary>
    /// <remarks>
    /// An <c>inline-block</c> establishes its own block formatting context
    /// (CSS 2.1 §10.1). When an inline-block has mixed block + inline
    /// children, we apply the same anonymous-block wrapping so the BFC sub-pass
    /// sees a uniform list of blocks. Inline boxes that contain only inline
    /// content are left alone — they continue to flatten into the parent IFC.
    /// </remarks>
    private static void WrapInlinesInAnonymousBlocks(Box.Box parent)
    {
        // Text and Replaced boxes have no children worth recursing into.
        if (parent.Kind == BoxKind.Text || parent.Kind == BoxKind.Replaced)
            return;

        // An anonymous block is itself a wrapper around an inline run; we
        // never re-wrap its children. But we DO need to descend through it
        // because it may host an inline-block with mixed children that needs
        // its own anonymous-block wrapping.
        if (parent.Kind == BoxKind.AnonymousBlock)
        {
            foreach (var child in parent.Children) WrapInlinesInAnonymousBlocks(child);
            return;
        }

        // An inline box with no block-level descendants flattens into the
        // enclosing IFC; no wrapping needed. Only when an inline (including
        // inline-block) has mixed block + inline children do we treat it like
        // a block container for anonymous-block wrapping.
        if (parent.Kind == BoxKind.Inline && !HasBlockLevelChild(parent))
        {
            foreach (var child in parent.Children) WrapInlinesInAnonymousBlocks(child);
            return;
        }

        // Always wrap inline runs in an anonymous block — even when the block
        // contains only inlines — so the block formatting context sees a
        // uniform list of block-level children. ImageBox (BoxKind.Replaced)
        // counts as inline content too: <img> is inline-replaced by default.
        var newChildren = new List<Box.Box>();
        AnonymousBlockBox? bucket = null;
        foreach (var child in parent.Children)
        {
            var isInline = child.Kind is BoxKind.Inline or BoxKind.Text or BoxKind.Replaced;
            if (isInline)
            {
                bucket ??= new AnonymousBlockBox(parent.Style);
                child.Parent = bucket;
                bucket.Children.Add(child);
            }
            else
            {
                FlushBucket(bucket, newChildren, parent);
                bucket = null;
                newChildren.Add(child);
                child.Parent = parent;
            }
        }

        FlushBucket(bucket, newChildren, parent);

        parent.Children.Clear();
        parent.Children.AddRange(newChildren);

        foreach (var child in parent.Children) WrapInlinesInAnonymousBlocks(child);
    }

    private static bool HasBlockLevelChild(Box.Box parent)
    {
        foreach (var child in parent.Children)
        {
            if (child.Kind is BoxKind.BlockContainer or BoxKind.AnonymousBlock)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Append a finished anonymous-block bucket to <paramref name="newChildren"/>,
    /// unless it holds only collapsible whitespace. CSS 2.2 §9.2.2.1: an anonymous
    /// block that would contain only whitespace which subsequently collapses away
    /// is not generated — so the newlines/indentation between block-level siblings
    /// (e.g. between stacked <c>&lt;p&gt;</c>s) don't each become a line-height-tall
    /// box that bloats vertical spacing and breaks adjacent-margin collapse.
    /// </summary>
    private static void FlushBucket(AnonymousBlockBox? bucket, List<Box.Box> newChildren, Box.Box parent)
    {
        if (bucket is null || bucket.Children.Count == 0)
            return;
        if (IsCollapsibleWhitespaceOnly(bucket))
            return;
        bucket.Parent = parent;
        newChildren.Add(bucket);
    }

    private static bool IsCollapsibleWhitespaceOnly(AnonymousBlockBox bucket)
    {
        foreach (var child in bucket.Children)
        {
            if (child is not TextBox text || !string.IsNullOrWhiteSpace(text.Text))
                return false;
            // `white-space: pre*` keeps whitespace significant — don't drop it.
            if (text.Style?.Get(PropertyId.WhiteSpace) is CssKeyword { Name: "pre" or "pre-wrap" or "pre-line" })
                return false;
        }
        return true;
    }

    private static string DisplayKeyword(ComputedStyle style)
        => style.Get(PropertyId.Display) is CssKeyword k ? k.Name.ToLowerInvariant() : "inline";

    /// <summary>
    /// Build an <c>&lt;img&gt;</c> as an <see cref="ImageBox"/> when the
    /// resolver has decoded bytes for it; otherwise degrade to a
    /// <see cref="TextBox"/> carrying the <c>alt</c> (or <c>aria-label</c> /
    /// <c>title</c>) attribute when present. The HTML spec calls this the
    /// "missing image" rendering: nothing if alt="" was explicitly authored,
    /// otherwise show the alternative text.
    /// </summary>
    private void BuildImage(Element img, ComputedStyle style, Box.Box parentBox)
    {
        if (_images.TryResolve(img, out var resolved))
        {
            var (width, height) = ResolveImageSize(img, resolved);
            var box = new ImageBox(style, img, width, height, resolved.Source);
            parentBox.AppendChild(box);
            return;
        }

        var label = AccessibleName(img);
        if (string.IsNullOrEmpty(label)) return;
        parentBox.AppendChild(new TextBox(label, style));
    }

    /// <summary>
    /// We don't paint SVG yet (no vector renderer + no SVG DOM). Until we do,
    /// keep accessibility-driven content visible: render the inline element's
    /// accessible name (aria-label / title / alt-on-image-children) as text
    /// so a labelled icon like Google's <c>&lt;svg aria-label="Google"&gt;</c>
    /// shows up as the word "Google" rather than a vanishing inline.
    /// </summary>
    private void BuildSvgFallback(Element svg, ComputedStyle style, Box.Box parentBox)
    {
        var label = AccessibleName(svg);
        if (string.IsNullOrEmpty(label)) return;
        // Inline the label; the surrounding context controls block/inline
        // flow on the parent.
        parentBox.AppendChild(new TextBox(label, style));
    }

    /// <summary>
    /// The element's accessible name, in priority order: <c>aria-label</c>,
    /// then <c>alt</c> (for <c>&lt;img&gt;</c>), then <c>title</c>. Returns
    /// the empty string when no candidate is set.
    /// </summary>
    private static string AccessibleName(Element element)
    {
        var aria = element.GetAttribute("aria-label");
        if (!string.IsNullOrEmpty(aria)) return aria;
        var alt = element.GetAttribute("alt");
        if (!string.IsNullOrEmpty(alt)) return alt;
        var title = element.GetAttribute("title");
        if (!string.IsNullOrEmpty(title)) return title;
        return string.Empty;
    }

    /// <summary>
    /// HTML <c>width</c>/<c>height</c> attributes take precedence over the
    /// intrinsic dimensions; missing axes scale to preserve aspect ratio.
    /// CSS <c>width</c>/<c>height</c> support is deferred to a follow-up.
    /// </summary>
    private static (double Width, double Height) ResolveImageSize(Element img, ResolvedImage resolved)
    {
        var attrW = ParseDimensionAttribute(img.GetAttribute("width"));
        var attrH = ParseDimensionAttribute(img.GetAttribute("height"));
        var iw = resolved.Width > 0 ? resolved.Width : 1;
        var ih = resolved.Height > 0 ? resolved.Height : 1;

        if (attrW is { } w && attrH is { } h) return (w, h);
        if (attrW is { } onlyW) return (onlyW, onlyW * ih / iw);
        if (attrH is { } onlyH) return (onlyH * iw / ih, onlyH);
        return (iw, ih);
    }

    private static void AppendInputLabel(Element input, ComputedStyle style, Box.Box box)
    {
        var type = (input.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();

        // Controls whose label isn't text content (checkbox/radio glyph, file
        // picker, image button, hidden) get no synthetic text.
        if (type is "checkbox" or "radio" or "file" or "image" or "hidden" or "color" or "range")
            return;

        var value = input.GetAttribute("value");
        if (!string.IsNullOrEmpty(value))
        {
            box.AppendChild(new TextBox(value, style));
            return;
        }

        // Default labels for submit/reset buttons match what browsers show
        // when `value` is omitted — per HTML spec localised defaults.
        var fallback = type switch
        {
            "submit" => "Submit",
            "reset" => "Reset",
            "button" => "",
            _ => input.GetAttribute("placeholder") ?? "",
        };
        if (!string.IsNullOrEmpty(fallback))
            box.AppendChild(new TextBox(fallback, style));
    }

    private static double? ParseDimensionAttribute(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // HTML allows trailing "px"; we accept that and bare integers/decimals.
        var trimmed = raw.Trim().TrimEnd('p', 'P', 'x', 'X');
        return double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }
}
