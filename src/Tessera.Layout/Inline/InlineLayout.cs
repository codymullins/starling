using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Tessera.Layout.Box;
using Tessera.Layout.Text;

namespace Tessera.Layout.Inline;

/// <summary>
/// Lays out an anonymous block's inline children into line boxes. Produces
/// <see cref="TextFragment"/> entries on each <see cref="TextBox"/> describing
/// where each piece appears on its parent's line.
/// </summary>
internal sealed class InlineLayout
{
    private readonly ITextMeasurer _measurer;
    private readonly Size? _viewport;

    public InlineLayout(ITextMeasurer measurer, Size? viewport = null)
    {
        _measurer = measurer;
        _viewport = viewport;
    }

    public double Layout(Box.Box container, double availableWidth)
    {
        var fontSize = ResolveFontSize(container.Style);
        var containerSpec = ResolveFontSpec(container.Style);
        var lineHeight = ResolveLineHeight(container.Style, fontSize, containerSpec);
        var baseline = _measurer.Baseline(fontSize, containerSpec);

        // Collect a flat sequence of inline-formatting items by walking the
        // container's inline subtree. Regular `<span>` wrappers flatten so
        // their text contributes directly; `inline-block` boxes (form
        // controls, explicit display:inline-block) stay atomic and lay out
        // as a single unit with their own frame + box model.
        var runs = new List<InlineRun>();
        Flatten(container, runs);

        // No content → zero height.
        if (runs.Count == 0) return 0;

        double cursorX = 0, cursorY = 0;
        double currentLineHeight = lineHeight;
        var fragments = new List<(TextBox Owner, int Index)>();
        var placedImages = new List<ImageBox>();
        var placedAtomics = new List<InlineBox>();

        foreach (var run in runs)
        {
            switch (run)
            {
                case TextRun text:
                    LayoutText(text, container.Style, availableWidth, baseline,
                        fragments, ref cursorX, ref cursorY, ref currentLineHeight);
                    break;
                case ImageRun image:
                    LayoutImage(image.Box, availableWidth,
                        placedImages, ref cursorX, ref cursorY, ref currentLineHeight);
                    break;
                case AtomicRun atomic:
                    LayoutAtomic(atomic.Box, availableWidth,
                        placedAtomics, ref cursorX, ref cursorY, ref currentLineHeight);
                    break;
                case LineBreakRun:
                    // <br>: a forced line break. Close the current line and
                    // start a fresh one at the container's line height.
                    cursorY += currentLineHeight;
                    cursorX = 0;
                    currentLineHeight = lineHeight;
                    break;
            }
        }

        AlignLines(container.Style, availableWidth, fragments, placedImages, placedAtomics);
        return cursorY + currentLineHeight;
    }

    private void LayoutText(
        TextRun run,
        ComputedStyle? containerStyle,
        double availableWidth,
        double containerBaseline,
        List<(TextBox Owner, int Index)> fragments,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        var normalized = NormalizeWhitespace(run.Text);
        if (normalized.Length == 0) return;

        var effectiveStyle = run.Style ?? containerStyle;
        var localFontSize = ResolveFontSize(effectiveStyle);
        var localSpec = ResolveFontSpec(effectiveStyle);
        var localLineHeight = ResolveLineHeight(effectiveStyle, localFontSize, localSpec);
        currentLineHeight = Math.Max(currentLineHeight, localLineHeight);

        foreach (var word in SplitToWords(normalized))
        {
            if (word.Length == 0) continue;
            // Collapse whitespace at the start of a line (white-space: normal).
            if (cursorX == 0 && string.IsNullOrWhiteSpace(word)) continue;
            var width = _measurer.MeasureWidth(word, localFontSize, localSpec);
            var leadingSpace = word.StartsWith(" ", StringComparison.Ordinal);

            if (cursorX > 0 && cursorX + width > availableWidth)
            {
                cursorY += currentLineHeight;
                cursorX = 0;
                currentLineHeight = localLineHeight;
                if (leadingSpace)
                {
                    var trimmed = word.TrimStart(' ');
                    if (trimmed.Length == 0) continue;
                    var trimmedWidth = _measurer.MeasureWidth(trimmed, localFontSize, localSpec);
                    AddFragment(run.Owner, fragments,
                        new TextFragment(trimmed, cursorX, cursorY, trimmedWidth, currentLineHeight, containerBaseline));
                    cursorX += trimmedWidth;
                    continue;
                }
            }

            AddFragment(run.Owner, fragments,
                new TextFragment(word, cursorX, cursorY, width, currentLineHeight, _measurer.Baseline(localFontSize, localSpec)));
            cursorX += width;
        }
    }

    /// <summary>
    /// Place an inline-block box atomically on the current line. Resolves its
    /// own box model, lays its (text) children out within its content box,
    /// and sets its <see cref="Box.Box.Frame"/> in the enclosing anonymous
    /// block's content-coordinate space. The painter walks the box like any
    /// other block: background + border first, then children translated by
    /// our padding+border edge.
    /// </summary>
    private void LayoutAtomic(
        InlineBox box,
        double availableWidth,
        List<InlineBox> placedAtomics,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        ResolveAtomicBoxModel(box, availableWidth);

        var fontSize = ResolveFontSize(box.Style);
        var spec = ResolveFontSpec(box.Style);
        var lineHeight = ResolveLineHeight(box.Style, fontSize, spec);
        var contentWidth = LayoutAtomicContent(box, fontSize, spec);

        // CSS width: prefer an explicit value if the cascade gave us one.
        // Percentages resolve against the enclosing block's content width.
        var explicitWidth = ResolveLength(box.Style, PropertyId.Width, availableWidth);
        if (explicitWidth is { } w) contentWidth = w;

        var contentHeight = lineHeight;
        var explicitHeight = ResolveLength(box.Style, PropertyId.Height, lineHeight);
        if (explicitHeight is { } h) contentHeight = h;

        var outerWidth = contentWidth + box.Padding.Horizontal + box.Border.Horizontal;
        var outerHeight = contentHeight + box.Padding.Vertical + box.Border.Vertical;

        // Wrap to a new line if this atomic doesn't fit and the line already
        // has content. Atomic items never split.
        if (cursorX > 0 && cursorX + outerWidth + box.Margin.Horizontal > availableWidth)
        {
            cursorY += currentLineHeight;
            cursorX = 0;
            currentLineHeight = outerHeight + box.Margin.Vertical;
        }
        else
        {
            currentLineHeight = Math.Max(currentLineHeight, outerHeight + box.Margin.Vertical);
        }

        box.Frame = new Rect(
            cursorX + box.Margin.Left,
            cursorY + box.Margin.Top,
            outerWidth,
            outerHeight);
        placedAtomics.Add(box);
        cursorX += outerWidth + box.Margin.Horizontal;
    }

    /// <summary>
    /// Measure and place the inline-block's direct text children, returning
    /// the natural content width. The synthesized TextBox for an
    /// <c>&lt;input&gt;</c>'s value/placeholder lives here, as does a
    /// <c>&lt;button&gt;</c>'s label text.
    /// </summary>
    private double LayoutAtomicContent(InlineBox box, double fontSize, FontSpec spec)
    {
        var baseline = _measurer.Baseline(fontSize, spec);
        double width = 0;
        double height = 0;

        foreach (var child in box.Children)
        {
            if (child is TextBox tb)
            {
                tb.Fragments.Clear();
                var text = NormalizeWhitespace(tb.Text);
                if (text.Length == 0) continue;
                var fragWidth = _measurer.MeasureWidth(text, fontSize, spec);
                tb.Fragments.Add(new TextFragment(text, width, 0, fragWidth, fontSize, baseline));
                width += fragWidth;
                height = Math.Max(height, fontSize);
            }
        }

        // <input size=N>: HTML attribute that hints at an N-character width
        // (used for text-like input types). Per HTML, the default size is 20
        // and the rendered width is N "average character widths" of the font.
        // We approximate "average character width" with the advance of the
        // "0" glyph (a fair proxy for the CSS `ch` unit), which keeps the
        // sizing in line with what browsers settle on for proportional fonts.
        var cols = ResolveInputSizeCols(box.Element);
        if (cols > 0)
        {
            var charWidth = _measurer.MeasureWidth("0", fontSize, spec);
            if (charWidth <= 0) charWidth = fontSize * 0.5;
            var minWidth = cols * charWidth;
            if (minWidth > width) width = minWidth;
        }

        return width;
    }

    /// <summary>
    /// Resolve the effective character-column width for an <c>&lt;input&gt;</c>'s
    /// <c>size</c> attribute. Returns 0 for elements that don't honor <c>size</c>
    /// (non-input, or input types like checkbox/submit/file) so callers don't
    /// inflate their width. Text-like input types default to 20 columns when
    /// the attribute is missing, matching the HTML spec.
    /// </summary>
    private static int ResolveInputSizeCols(Element? element)
    {
        if (element is null) return 0;
        if (!string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase))
            return 0;

        var sizeAttr = element.GetAttribute("size");
        if (!string.IsNullOrEmpty(sizeAttr) &&
            int.TryParse(sizeAttr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var explicitCols) && explicitCols > 0)
        {
            return explicitCols;
        }

        // Default size=20 only for the "text"-family input types per the
        // HTML spec. Buttons, checkboxes, radios, etc. size to their content
        // (label/glyph) instead.
        var type = (element.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
        return type switch
        {
            "text" or "search" or "tel" or "url" or "email" or "password" or "" => 20,
            _ => 0,
        };
    }

    private void ResolveAtomicBoxModel(InlineBox box, double containerWidth)
    {
        box.Margin = new Edges(
            ResolveLength(box.Style, PropertyId.MarginTop, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginRight, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginBottom, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginLeft, containerWidth) ?? 0);

        box.Padding = new Edges(
            ResolveLength(box.Style, PropertyId.PaddingTop, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingRight, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingBottom, containerWidth) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingLeft, containerWidth) ?? 0);

        box.Border = new Edges(
            ResolveBorderWidth(box.Style, PropertyId.BorderTopWidth, PropertyId.BorderTopStyle),
            ResolveBorderWidth(box.Style, PropertyId.BorderRightWidth, PropertyId.BorderRightStyle),
            ResolveBorderWidth(box.Style, PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle),
            ResolveBorderWidth(box.Style, PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle));
    }

    private double? ResolveLength(ComputedStyle? style, PropertyId property, double percentageBasis)
    {
        if (style is null) return null;
        return style.Get(property) switch
        {
            CssLength len => Block.BlockLayout.ToPx(len, _viewport),
            CssPercentage pct => percentageBasis * pct.Value / 100d,
            CssNumber n => n.Value,
            _ => null,
        };
    }

    private double ResolveBorderWidth(ComputedStyle? style, PropertyId widthId, PropertyId styleId)
    {
        if (style is null) return 0;
        if (style.Get(styleId) is CssKeyword k && k.Name == "none") return 0;
        return style.Get(widthId) is CssLength len ? Block.BlockLayout.ToPx(len, _viewport) : 0;
    }

    private static void LayoutImage(
        ImageBox image,
        double availableWidth,
        List<ImageBox> placedImages,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        var width = image.IntrinsicWidth;
        var height = image.IntrinsicHeight;

        // Wrap to the next line if this image won't fit and the current line
        // already has content. An image wider than the container stays on its
        // own line and overflows — matching the simple v1 "no overflow break"
        // behaviour of text.
        if (cursorX > 0 && cursorX + width > availableWidth)
        {
            cursorY += currentLineHeight;
            cursorX = 0;
            currentLineHeight = height;
        }
        else
        {
            currentLineHeight = Math.Max(currentLineHeight, height);
        }

        // v1 places the image top-aligned within its line. Baseline alignment
        // ("bottom of replaced element sits on text baseline") is a follow-up.
        image.Frame = new Rect(cursorX, cursorY, width, height);
        placedImages.Add(image);
        cursorX += width;
    }

    private static void AddFragment(TextBox owner, List<(TextBox Owner, int Index)> fragments, TextFragment fragment)
    {
        owner.Fragments.Add(fragment);
        fragments.Add((owner, owner.Fragments.Count - 1));
    }

    private static void AlignLines(
        ComputedStyle? style,
        double availableWidth,
        List<(TextBox Owner, int Index)> fragments,
        List<ImageBox> placedImages,
        List<InlineBox> placedAtomics)
    {
        var align = style?.Get(PropertyId.TextAlign) is CssKeyword keyword
            ? keyword.Name.ToLowerInvariant()
            : "start";
        if (align is not ("center" or "right" or "end") ||
            (fragments.Count == 0 && placedImages.Count == 0 && placedAtomics.Count == 0))
            return;

        // Group fragments, images, and atomic inline-blocks by their Y so
        // per-line alignment shifts apply uniformly to everything on the line.
        var lines = new Dictionary<double, (List<(TextBox Owner, int Index)> Texts, List<ImageBox> Images, List<InlineBox> Atomics, double RightEdge)>();
        foreach (var item in fragments)
        {
            var frag = item.Owner.Fragments[item.Index];
            var key = frag.Y;
            if (!lines.TryGetValue(key, out var line)) line = ([], [], [], 0);
            line.Texts.Add(item);
            line.RightEdge = Math.Max(line.RightEdge, frag.X + frag.Width);
            lines[key] = line;
        }
        foreach (var image in placedImages)
        {
            var key = image.Frame.Y;
            if (!lines.TryGetValue(key, out var line)) line = ([], [], [], 0);
            line.Images.Add(image);
            line.RightEdge = Math.Max(line.RightEdge, image.Frame.X + image.Frame.Width);
            lines[key] = line;
        }
        foreach (var atomic in placedAtomics)
        {
            var key = atomic.Frame.Y;
            if (!lines.TryGetValue(key, out var line)) line = ([], [], [], 0);
            line.Atomics.Add(atomic);
            line.RightEdge = Math.Max(line.RightEdge, atomic.Frame.X + atomic.Frame.Width);
            lines[key] = line;
        }

        foreach (var (_, line) in lines)
        {
            var offset = align == "center"
                ? Math.Max(0, (availableWidth - line.RightEdge) / 2d)
                : Math.Max(0, availableWidth - line.RightEdge);
            if (offset == 0) continue;

            foreach (var item in line.Texts)
            {
                var fragment = item.Owner.Fragments[item.Index];
                item.Owner.Fragments[item.Index] = fragment with { X = fragment.X + offset };
            }
            foreach (var image in line.Images)
            {
                image.Frame = image.Frame with { X = image.Frame.X + offset };
            }
            foreach (var atomic in line.Atomics)
            {
                atomic.Frame = atomic.Frame with { X = atomic.Frame.X + offset };
            }
        }
    }

    private abstract record InlineRun;
    private sealed record TextRun(string Text, ComputedStyle? Style, TextBox Owner) : InlineRun;
    private sealed record ImageRun(ImageBox Box) : InlineRun;
    private sealed record AtomicRun(InlineBox Box) : InlineRun;
    private sealed record LineBreakRun : InlineRun
    {
        public static readonly LineBreakRun Instance = new();
    }

    private static void Flatten(Box.Box box, List<InlineRun> runs)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextBox tb:
                    tb.Fragments.Clear();
                    runs.Add(new TextRun(tb.Text, tb.Style, tb));
                    break;
                case ImageBox img:
                    runs.Add(new ImageRun(img));
                    break;
                case InlineBox ib when IsLineBreak(ib):
                    runs.Add(LineBreakRun.Instance);
                    break;
                case InlineBox ib when IsAtomicInline(ib):
                    runs.Add(new AtomicRun(ib));
                    break;
                case InlineBox ib:
                    Flatten(ib, runs);
                    break;
                default:
                    // Block-in-inline would land here; in v1 we just walk it as a sub-tree.
                    Flatten(child, runs);
                    break;
            }
        }
    }

    /// <summary>
    /// An "atomic inline" is a box that participates in the inline formatting
    /// context as a single unit — it occupies one indivisible slot in a line
    /// and carries its own box model. Per [CSS 2.1 §9.2.4] this includes
    /// <c>display:inline-block</c>, <c>inline-table</c>, replaced inlines,
    /// and (legacy) <c>inline-flex</c>/<c>inline-grid</c>. We only handle
    /// <c>inline-block</c> in v1; replaced inlines go through <see cref="ImageRun"/>.
    /// </summary>
    /// <summary>An <c>&lt;br&gt;</c> element: a forced line break in the inline flow.</summary>
    private static bool IsLineBreak(InlineBox box)
        => string.Equals(box.Element?.LocalName, "br", StringComparison.OrdinalIgnoreCase);

    private static bool IsAtomicInline(InlineBox box)
    {
        if (box.Style is null) return false;
        return box.Style.Get(PropertyId.Display) is CssKeyword k
            && k.Name.Equals("inline-block", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWhitespace(string text)
    {
        if (text.Length == 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SplitToWords(string text)
    {
        // Each word is one or more non-space chars, possibly preceded by a space.
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                if (i > start) yield return text[start..i];
                yield return " ";
                start = i + 1;
            }
        }
        if (start < text.Length) yield return text[start..];
    }

    private double ResolveFontSize(ComputedStyle? style)
    {
        if (style is null) return 16;
        return style.Get(PropertyId.FontSize) switch
        {
            CssLength len => Block.BlockLayout.ToPx(len, _viewport),
            CssNumber n => n.Value,
            _ => 16,
        };
    }

    private double ResolveLineHeight(ComputedStyle? style, double fontSize, FontSpec spec)
    {
        if (style is null) return _measurer.NormalLineHeight(fontSize, spec);
        return style.Get(PropertyId.LineHeight) switch
        {
            CssNumber n => n.Value * fontSize,
            CssLength len => Block.BlockLayout.ToPx(len, _viewport),
            CssPercentage pct => fontSize * pct.Value / 100d,
            CssKeyword k when k.Name == "normal" => _measurer.NormalLineHeight(fontSize, spec),
            _ => _measurer.NormalLineHeight(fontSize, spec),
        };
    }

    private static FontSpec ResolveFontSpec(ComputedStyle? style) => FontSpec.FromStyle(style);
}
