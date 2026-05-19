using System.Diagnostics;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Layout.Box;
using Starling.Layout.Text;

namespace Starling.Layout.Inline;

/// <summary>
/// Lays out an anonymous block's inline children into line boxes. Produces
/// <see cref="TextFragment"/> entries on each <see cref="TextBox"/> describing
/// where each piece appears on its parent's line.
/// </summary>
internal sealed class InlineLayout
{
    private readonly ITextMeasurer _measurer;
    private readonly Size? _viewport;
    private readonly IDiagnostics _diag;

    public InlineLayout(ITextMeasurer measurer, Size? viewport = null, IDiagnostics? diagnostics = null)
    {
        _measurer = measurer;
        _viewport = viewport;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
    }

    public double Layout(Box.Box container, double availableWidth)
        => Layout(container, availableWidth, measure: false);

    /// <summary>
    /// Internal overload used by the inline-block shrink-to-fit measurement
    /// pass. When <paramref name="measure"/> is <c>true</c>, alignment shifts
    /// (<see cref="AlignLines"/>) are skipped so callers walking the resulting
    /// frames (via <see cref="MeasureUsedWidth"/>) see the natural pre-shift
    /// positions. Otherwise a text-align:center under a huge measurement
    /// width would shift everything right by ~availableWidth/2, defeating
    /// shrink-to-fit.
    /// </summary>
    internal double Layout(Box.Box container, double availableWidth, bool measure)
    {
        using var span = _diag.Span("layout", "inline");
        Activity.Current?.SetTag("inline.measure", measure);
        Activity.Current?.SetTag("inline.available_width", availableWidth);

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
        Activity.Current?.SetTag("inline.runs", runs.Count);

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

        if (!measure)
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
        var localBaseline = _measurer.Baseline(localFontSize, localSpec);
        currentLineHeight = Math.Max(currentLineHeight, localLineHeight);

        // Shape the whole normalized run once and slice per-word. This collapses
        // the per-word native HarfBuzz call to a single call per text run.
        // Slicing relies on a 1:1 glyph-to-character mapping; non-ASCII text
        // with ligatures, combining marks, or surrogate pairs may shape to a
        // different glyph count, in which case we fall back to per-word shaping
        // to preserve correctness on complex scripts.
        _diag.Counter("layout.text.measures", 1);
        var whole = _measurer.Shape(normalized, localFontSize, localSpec);
        var canSlice = whole.Glyphs.Length == normalized.Length;

        var charOffset = 0;
        foreach (var word in SplitToWords(normalized))
        {
            var wordStart = charOffset;
            charOffset += word.Length;

            if (word.Length == 0) continue;
            // Collapse whitespace at the start of a line (white-space: normal).
            if (cursorX == 0 && string.IsNullOrWhiteSpace(word)) continue;

            var shaped = canSlice
                ? whole.Slice(wordStart, wordStart + word.Length)
                : ShapeWord(word, localFontSize, localSpec);
            var width = shaped.Advance;
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
                    // The trimmed slice starts past the run's leading spaces;
                    // its glyph indices and char indices stay aligned because
                    // every leading char was a space (1 glyph, 1 char).
                    var trimmedStart = wordStart + (word.Length - trimmed.Length);
                    var trimmedShape = canSlice
                        ? whole.Slice(trimmedStart, trimmedStart + trimmed.Length)
                        : ShapeWord(trimmed, localFontSize, localSpec);
                    AddFragment(run.Owner, fragments,
                        new TextFragment(trimmed, cursorX, cursorY, trimmedShape.Advance, currentLineHeight, containerBaseline, trimmedShape));
                    cursorX += trimmedShape.Advance;
                    continue;
                }
            }

            AddFragment(run.Owner, fragments,
                new TextFragment(word, cursorX, cursorY, width, currentLineHeight, localBaseline, shaped));
            cursorX += width;
        }
    }

    private ShapedRun ShapeWord(string word, double fontSize, FontSpec spec)
    {
        _diag.Counter("layout.text.measures", 1);
        return _measurer.Shape(word, fontSize, spec);
    }

    /// <summary>
    /// Place an inline-block box atomically on the current line. Resolves its
    /// own box model, lays its children out within its content box, and sets
    /// its <see cref="Box.Box.Frame"/> in the enclosing anonymous block's
    /// content-coordinate space. The painter walks the box like any other
    /// block: background + border first, then children translated by our
    /// padding+border edge.
    /// </summary>
    /// <remarks>
    /// Per CSS 2.1 §10.1, an inline-block establishes a new block formatting
    /// context. We dispatch on the child mix:
    /// <list type="bullet">
    ///   <item>Any block-level child → nested <see cref="Block.BlockLayout"/>
    ///   pass (true BFC sub-pass, with anonymous-block wrapping already done
    ///   by <see cref="Tree.BoxTreeBuilder"/>).</item>
    ///   <item>Only inline-level non-text children (nested inline-blocks,
    ///   images, <c>&lt;br&gt;</c>, spans) → a recursive inline-formatting
    ///   sub-pass via <see cref="Layout(Box.Box, double)"/>, sized via a max-content
    ///   approximation so the inline-block shrinks-to-fit (CSS 2.1 §10.3.5).</item>
    ///   <item>Only text children → the lightweight
    ///   <see cref="LayoutAtomicContent"/> path that measures glyph widths
    ///   directly.</item>
    /// </list>
    /// </remarks>
    private void LayoutAtomic(
        InlineBox box,
        double availableWidth,
        List<InlineBox> placedAtomics,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        using var span = _diag.Span("layout", "inline.atomic");

        ResolveAtomicBoxModel(box, availableWidth);

        var fontSize = ResolveFontSize(box.Style);
        var spec = ResolveFontSpec(box.Style);
        var lineHeight = ResolveLineHeight(box.Style, fontSize, spec);

        double contentWidth;
        double contentHeight;

        if (HasBlockLevelChild(box))
        {
            Activity.Current?.SetTag("atomic.path", "bfc");
            // Inline-block with mixed/block children: run a BFC sub-pass.
            //
            // CSS 2.1 §10.3.5 shrink-to-fit:
            //     used-width = min(max(min-content, available), max-content).
            //
            // The hard problem here: block children sized at the available
            // width inflate their *frame* to that width even when their
            // content is narrow. A naive single-pass therefore paints the
            // block child's background across the whole row. We approximate
            // max-content with a measurement pass at "infinite" width, walk
            // text/atomic descendants to find the rightmost edge they
            // actually used (block frames after this pass are at the wide
            // width — we deliberately ignore them), then re-layout at the
            // shrunk width so block descendants' frames match too.
            //
            // Cost: two BFC passes per inline-block-with-block-child. At
            // this stage of the project that's fine; real engines compute
            // intrinsic sizes recursively without an extra placement pass.
            var viewport = _viewport ?? new Size(availableWidth, 0);
            var explicitInner = ResolveLength(box.Style, PropertyId.Width, availableWidth);

            double subWidth;
            if (explicitInner is { } iw)
            {
                subWidth = iw;
            }
            else
            {
                using (_diag.Span("layout", "inline.measure_pass"))
                {
                    const double measureWidth = 1_000_000d;
                    var measureLayout = new Block.BlockLayout(_measurer, viewport, _diag);
                    measureLayout.LayoutChildren(box, measureWidth, measure: true);
                    var maxContent = MeasureUsedWidth(box);
                    var available = Math.Max(0, availableWidth - cursorX);
                    subWidth = Math.Min(maxContent, available);
                }
            }

            var sub = new Block.BlockLayout(_measurer, viewport, _diag);
            var consumed = sub.LayoutChildren(box, subWidth);
            contentWidth = subWidth;
            contentHeight = consumed;
        }
        else if (HasNonTextInlineChild(box))
        {
            Activity.Current?.SetTag("atomic.path", "ifc");
            // Inline-block whose children are inline-level non-text (nested
            // inline-blocks, <br>, <img>, spans). The text-only path would
            // silently drop these. Run an IFC sub-pass shrunk-to-fit via the
            // same two-pass approach used for BFC sub-pass above: measure at
            // huge width to find max-content, then re-lay at the constrained
            // width so atomic-inline frames sit at their final X.
            //
            // CSS 2.1 §10.3.5: used-width = min(max(min-content, available), max-content).
            var explicitInner = ResolveLength(box.Style, PropertyId.Width, availableWidth);
            double subWidth;
            if (explicitInner is { } iw)
            {
                subWidth = iw;
            }
            else
            {
                using (_diag.Span("layout", "inline.measure_pass"))
                {
                    const double measureWidth = 1_000_000d;
                    this.Layout(box, measureWidth, measure: true);
                    var maxContent = MeasureUsedWidth(box);
                    var available = Math.Max(0, availableWidth - cursorX);
                    subWidth = Math.Min(maxContent, available);
                }
            }

            var consumed = this.Layout(box, subWidth);
            contentHeight = consumed;
            contentWidth = subWidth;
        }
        else
        {
            Activity.Current?.SetTag("atomic.path", "text");
            contentWidth = LayoutAtomicContent(box, fontSize, spec);
            contentHeight = lineHeight;
        }

        // CSS width: prefer an explicit value if the cascade gave us one.
        // Percentages resolve against the enclosing block's content width.
        var explicitWidth = ResolveLength(box.Style, PropertyId.Width, availableWidth);
        if (explicitWidth is { } w) contentWidth = w;

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

    private void LayoutImage(
        ImageBox image,
        double availableWidth,
        List<ImageBox> placedImages,
        ref double cursorX,
        ref double cursorY,
        ref double currentLineHeight)
    {
        var (width, height) = ResolveReplacedSize(image, availableWidth);

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

    /// <summary>
    /// CSS 2.1 §10.3.2 (used dimensions of a replaced element) + §10.4
    /// (min-/max-width/height clamps). Resolves the box's computed
    /// <c>width</c>/<c>height</c>/<c>min-*</c>/<c>max-*</c> against the
    /// available inline-axis width, preserving the intrinsic aspect ratio
    /// when one axis is <c>auto</c>. Height percentages resolve against an
    /// unknown containing-block height in inline context, so they collapse
    /// to <c>auto</c> here (matches the common Chromium behaviour).
    /// </summary>
    internal (double Width, double Height) ResolveReplacedSize(ImageBox image, double availableWidth)
    {
        var style = image.Style;
        var iw = image.IntrinsicWidth > 0 ? image.IntrinsicWidth : 1;
        var ih = image.IntrinsicHeight > 0 ? image.IntrinsicHeight : 1;
        var ratio = iw / ih;

        var specW = Block.BlockLayout.ResolveLength(style, PropertyId.Width, availableWidth, _viewport, allowAuto: true);
        // Height percentages need a known containing-block height; in inline
        // context we don't have one, so leave height as auto when authored
        // as a percentage. Lengths still resolve normally.
        var specH = style?.Get(PropertyId.Height) is CssPercentage
            ? (double?)null
            : Block.BlockLayout.ResolveLength(style, PropertyId.Height, 0, _viewport, allowAuto: true);

        double w, h;
        if (specW.HasValue && specH.HasValue)
        {
            w = specW.Value;
            h = specH.Value;
        }
        else if (specW.HasValue)
        {
            w = specW.Value;
            h = w / ratio;
        }
        else if (specH.HasValue)
        {
            h = specH.Value;
            w = h * ratio;
        }
        else
        {
            w = iw;
            h = ih;
        }

        // §10.4 min/max constraints. When the constrained axis was derived
        // from the other axis (auto), keep aspect ratio while clamping; when
        // it was authored explicitly, clamp only that axis.
        var maxW = Block.BlockLayout.ResolveLength(style, PropertyId.MaxWidth, availableWidth, _viewport);
        if (style?.Get(PropertyId.MaxWidth) is CssKeyword mwk && mwk.Name == "none") maxW = null;
        var minW = Block.BlockLayout.ResolveLength(style, PropertyId.MinWidth, availableWidth, _viewport) ?? 0;

        var maxH = Block.BlockLayout.ResolveLength(style, PropertyId.MaxHeight, 0, _viewport);
        if (style?.Get(PropertyId.MaxHeight) is CssKeyword mhk && mhk.Name == "none") maxH = null;
        // Height percentages on min/max-height resolve against an unknown
        // containing-block height; ignore them in inline context.
        if (style?.Get(PropertyId.MaxHeight) is CssPercentage) maxH = null;
        var minHRaw = style?.Get(PropertyId.MinHeight) is CssPercentage
            ? (double?)null
            : Block.BlockLayout.ResolveLength(style, PropertyId.MinHeight, 0, _viewport);
        var minH = minHRaw ?? 0;

        if (maxW.HasValue && w > maxW.Value)
        {
            var scale = maxW.Value / w;
            w = maxW.Value;
            if (!specH.HasValue) h *= scale;
        }
        if (w < minW)
        {
            var scale = w > 0 ? minW / w : 1;
            w = minW;
            if (!specH.HasValue) h *= scale;
        }
        if (maxH.HasValue && h > maxH.Value)
        {
            var scale = maxH.Value / h;
            h = maxH.Value;
            if (!specW.HasValue) w *= scale;
        }
        if (h < minH)
        {
            var scale = h > 0 ? minH / h : 1;
            h = minH;
            if (!specW.HasValue) w *= scale;
        }

        return (w, h);
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

    /// <summary>
    /// True if any direct child is block-level (BlockContainer or
    /// AnonymousBlock — the latter being the wrapper around inline runs that
    /// share scope with real block children).
    /// </summary>
    private static bool HasBlockLevelChild(Box.Box box)
    {
        foreach (var child in box.Children)
        {
            if (child.Kind is BoxKind.BlockContainer or BoxKind.AnonymousBlock)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if any direct (or indirectly-nested via plain inline spans) child
    /// is something the text-only <see cref="LayoutAtomicContent"/> path
    /// cannot place: a nested inline-block, a replaced inline (<c>&lt;img&gt;</c>),
    /// or a <c>&lt;br&gt;</c>. Bare inline spans wrapping such children also
    /// count — those flatten into the same inline formatting context as their
    /// contents. We deliberately do NOT count plain inline <c>&lt;span&gt;</c>s
    /// that contain only text: the text-only path handles them via the
    /// container's direct TextBox children, and the box tree builder normally
    /// produces text children at the inline-block level for void content like
    /// <c>&lt;input&gt;</c>.
    /// </summary>
    private static bool HasNonTextInlineChild(Box.Box box)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case ImageBox:
                    return true;
                case InlineBox ib when IsLineBreak(ib):
                    return true;
                case InlineBox ib when IsAtomicInline(ib):
                    return true;
                case InlineBox ib:
                    // A wrapper inline (e.g. <span>) recurses; treat its
                    // subtree as part of this inline-block's IFC.
                    if (HasNonTextInlineChild(ib)) return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>
    /// After a recursive <see cref="Layout(Box.Box, double)"/> sub-pass on
    /// <paramref name="box"/>, measure the rightmost X reached by any placed
    /// descendant. This is the max-content extent of the inline-block —
    /// suitable as its content-box width for shrink-to-fit sizing (CSS 2.1
    /// §10.3.5). Descends through inline spans because their text fragments
    /// live on the spans themselves; atomic children carry their own
    /// <see cref="Box.Box.Frame"/>.
    /// </summary>
    private static double MeasureUsedWidth(Box.Box box)
    {
        double max = 0;
        Walk(box);
        return max;

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
                case InlineBox ib when IsAtomicInline(ib) && ib != box:
                    max = Math.Max(max, ib.Frame.X + ib.Frame.Width);
                    return;
            }
            foreach (var child in node.Children) Walk(child);
        }
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
