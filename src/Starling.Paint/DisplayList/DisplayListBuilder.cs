using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout;
using Tessera.Layout.Box;
using Tessera.Layout.Text;
using Tessera.Layout.Tree;

namespace Tessera.Paint.DisplayList;

/// <summary>
/// Walks a laid-out box tree and emits a display list in paint order.
/// </summary>
/// <remarks>
/// v1 paint order is the simple tree-walk order: background → children. Full
/// stacking-context order (z-index, positioned ancestors, opacity boundaries)
/// is M5+ work. The painter handles plain block backgrounds and inline text.
/// </remarks>
public sealed class DisplayListBuilder
{
    /// <summary>
    /// Builds a display list from a laid-out box tree. <paramref name="styleOverride"/>
    /// is an optional per-box hook used to swap in fresh styles without
    /// re-laying out — interactive shells call it with hover/focus-recascaded
    /// styles so <c>a:hover { color: red }</c> repaints in red at the same
    /// glyph positions. Returning <c>null</c> for a box keeps the
    /// layout-time <see cref="Box.Style"/>.
    /// </summary>
    public DisplayList Build(BlockBox root, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new DisplayList();
        Visit(root, new Rect(0, 0, root.Frame.Width, root.Frame.Height), list, originX: 0, originY: 0, styleOverride, images);
        return list;
    }

    private static ComputedStyle? EffectiveStyle(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => styleOverride?.Invoke(box) ?? box.Style;

    private static void Visit(Box box, Rect rootBounds, DisplayList list, double originX, double originY, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        // CSS `transform` is applied around the box's transform-origin (default
        // 50% 50% 0). Layout is unaffected: the box keeps its frame coordinates
        // and paint composes T(+origin) × M × T(-origin) on top of every
        // primitive in this subtree (paint of this box and every descendant).
        // Transform-origin parsing is a follow-up; the default centre is the
        // most common case and matches the spec's initial value.
        var transformMatrix = TryGetTransformMatrix(box, styleOverride, frameX, frameY);
        var transformed = transformMatrix is not null;
        if (transformed)
            list.Add(new PushTransform(transformMatrix!.Value));

        PaintBoxAndChildren(box, rootBounds, list, frameX, frameY, styleOverride, images);

        if (transformed)
            list.Add(PopTransform.Instance);
    }

    private static void PaintBoxAndChildren(Box box, Rect rootBounds, DisplayList list, double frameX, double frameY, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images)
    {
        // Backgrounds and borders for box-bearing boxes. BlockContainer and
        // AnonymousBlock always qualify; InlineBox qualifies only when it is
        // an atomic inline (display:inline-block) — flattened spans have a
        // zero frame and would otherwise emit a phantom rect at the origin.
        var hasFrame = box.Frame.Width > 0 && box.Frame.Height > 0;
        var paintsBox = box.Kind is BoxKind.BlockContainer or BoxKind.AnonymousBlock
            || (box.Kind == BoxKind.Inline && hasFrame);
        if (paintsBox && EffectiveStyle(box, styleOverride) is { } style)
        {
            var bg = style.GetColor(PropertyId.BackgroundColor);
            if (bg is { A: > 0 })
            {
                list.Add(new FillRect(new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height), bg));
            }

            // CSS Backgrounds 3 §3 — background-image paints inside the
            // box's padding box (border-box is the default origin per spec,
            // but at this layout fidelity the padding+border distinction
            // is unobservable and using the frame is correct).
            EmitBackgroundImage(box, frameX, frameY, list, style, images);

            // Borders. Painter renders one stroke per side that has a non-zero width.
            EmitBorders(box, frameX, frameY, list, style);
        }

        // Inline content: text fragments live on TextBoxes, positioned in their
        // anonymous-block parent's content box.
        if (box is TextBox textBox)
        {
            EmitTextFragments(textBox, frameX, frameY, list, styleOverride);
            return; // Text boxes have no children.
        }

        // Replaced inline content (currently <img>): its Frame was set by the
        // inline formatting context relative to the anonymous-block container,
        // so frameX/frameY (which already includes that translation) is its
        // document-space top-left.
        if (box is ImageBox imageBox)
        {
            list.Add(new DrawImage(
                new Rect(frameX, frameY, imageBox.Frame.Width, imageBox.Frame.Height),
                imageBox.Source));
            return; // ImageBox has no children.
        }

        // Children paint after backgrounds. Their frames are in our content-box
        // coords, so push our padding+border origin.
        var contentOriginX = frameX + box.Border.Left + box.Padding.Left;
        var contentOriginY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Visit(child, rootBounds, list, contentOriginX, contentOriginY, styleOverride, images);
    }

    /// <summary>
    /// Resolve <c>background-image</c> via <paramref name="images"/> and emit
    /// a sliced <see cref="DrawImage"/> using <c>background-position</c> +
    /// <c>background-size</c> to compute the source rectangle. This is the
    /// path that makes CSS sprite sheets render — the box's frame defines the
    /// visible window, the sprite PNG is the source, and bg-position picks
    /// which slice maps to the window.
    /// </summary>
    private static void EmitBackgroundImage(Box box, double frameX, double frameY, DisplayList list, ComputedStyle style, IImageResolver? images)
    {
        if (images is null) return;
        var url = style.Get(PropertyId.BackgroundImage) switch
        {
            CssUrl u => u.Value,
            _ => null,
        };
        if (url is null || !images.TryResolveUrl(url, out var decoded) || decoded is null) return;
        if (box.Frame.Width <= 0 || box.Frame.Height <= 0) return;
        if (decoded.Width <= 0 || decoded.Height <= 0) return;

        var boxW = box.Frame.Width;
        var boxH = box.Frame.Height;

        // background-size — default `auto auto` keeps the image at native
        // dimensions. A single length applies to width with height = auto.
        var (renderW, renderH) = ResolveBackgroundSize(style.Get(PropertyId.BackgroundSize), boxW, boxH, decoded.Width, decoded.Height);
        if (renderW <= 0 || renderH <= 0) return;

        // background-position — where the rendered image's top-left lands
        // inside the box. A single value is the X offset (Y defaults to
        // centred per the spec, but the sprite use case keys on X only, so
        // we default Y to 0 for the single-value case to match common
        // sprite-sheet authoring).
        var (offsetX, offsetY) = ResolveBackgroundPosition(style.Get(PropertyId.BackgroundPosition), boxW, boxH, renderW, renderH);

        // The rendered image rectangle in box coordinates.
        var imgX = offsetX;
        var imgY = offsetY;

        // Clip the rendered image to the box — the visible part is the
        // intersection. Anything outside is discarded.
        var destX = Math.Max(0, imgX);
        var destY = Math.Max(0, imgY);
        var destRight = Math.Min(boxW, imgX + renderW);
        var destBottom = Math.Min(boxH, imgY + renderH);
        var destW = destRight - destX;
        var destH = destBottom - destY;
        if (destW <= 0 || destH <= 0) return;

        // Map the clipped destination rect back to source pixel coords.
        var scaleX = decoded.Width / renderW;
        var scaleY = decoded.Height / renderH;
        var srcX = (destX - imgX) * scaleX;
        var srcY = (destY - imgY) * scaleY;
        var srcW = destW * scaleX;
        var srcH = destH * scaleY;

        list.Add(new DrawImage(
            new Rect(frameX + destX, frameY + destY, destW, destH),
            decoded,
            new Rect(srcX, srcY, srcW, srcH)));
    }

    private static (double Width, double Height) ResolveBackgroundSize(CssValue? value, double boxW, double boxH, double nativeW, double nativeH)
    {
        if (value is CssKeyword { Name: "contain" })
        {
            var scale = Math.Min(boxW / nativeW, boxH / nativeH);
            return (nativeW * scale, nativeH * scale);
        }
        if (value is CssKeyword { Name: "cover" })
        {
            var scale = Math.Max(boxW / nativeW, boxH / nativeH);
            return (nativeW * scale, nativeH * scale);
        }

        double? w = null, h = null;
        if (value is CssValueList list)
        {
            if (list.Values.Count > 0) w = ResolveBgSizeDim(list.Values[0], boxW);
            if (list.Values.Count > 1) h = ResolveBgSizeDim(list.Values[^1], boxH);
        }
        else
        {
            w = ResolveBgSizeDim(value, boxW);
        }

        // `auto` on one axis preserves aspect ratio against the other.
        if (w is null && h is null) return (nativeW, nativeH);
        if (w is null) return (nativeW * (h!.Value / nativeH), h.Value);
        if (h is null) return (w.Value, nativeH * (w.Value / nativeW));
        return (w.Value, h.Value);
    }

    private static double? ResolveBgSizeDim(CssValue? value, double basis)
        => value switch
        {
            CssLength len => Tessera.Layout.Block.BlockLayout.ToPx(len),
            CssPercentage pct => basis * pct.Value / 100d,
            CssNumber n => n.Value,
            _ => null,
        };

    private static (double X, double Y) ResolveBackgroundPosition(CssValue? value, double boxW, double boxH, double imgW, double imgH)
    {
        CssValue? xv = null, yv = null;
        if (value is CssValueList list)
        {
            if (list.Values.Count > 0) xv = list.Values[0];
            if (list.Values.Count > 1) yv = list.Values[^1];
        }
        else
        {
            xv = value;
        }
        var x = ResolveBgPositionAxis(xv, boxW, imgW, axisX: true);
        var y = ResolveBgPositionAxis(yv, boxH, imgH, axisX: false);
        return (x, y);
    }

    private static double ResolveBgPositionAxis(CssValue? value, double boxSize, double imgSize, bool axisX)
        => value switch
        {
            CssLength len => Tessera.Layout.Block.BlockLayout.ToPx(len),
            CssPercentage pct => (boxSize - imgSize) * pct.Value / 100d,
            CssNumber n => n.Value,
            CssKeyword { Name: "left" } => 0,
            CssKeyword { Name: "top" } => 0,
            CssKeyword { Name: "right" } => boxSize - imgSize,
            CssKeyword { Name: "bottom" } => boxSize - imgSize,
            CssKeyword { Name: "center" } => (boxSize - imgSize) / 2d,
            // Default missing-axis behaviour: x defaults to 0, y to 0 — the
            // sprite-sheet use case is the dominant consumer and authors
            // typically pass a single horizontal offset.
            _ => 0,
        };

    /// <summary>
    /// Reads <c>PropertyId.Transform</c> off the box's effective style and
    /// composes it with the (centre-of-box) transform-origin into a single
    /// document-space matrix. Returns <c>null</c> when no transform applies
    /// (<c>none</c>, identity, or unparseable). The hardcoded centre origin
    /// is a known limitation tracked in the WP follow-ups; full
    /// <c>transform-origin</c> value parsing is its own work item.
    /// </summary>
    private static Matrix2D? TryGetTransformMatrix(Box box, Func<Box, ComputedStyle?>? styleOverride, double frameX, double frameY)
    {
        if (box.Frame.Width <= 0 && box.Frame.Height <= 0) return null;
        var style = EffectiveStyle(box, styleOverride);
        if (style is null) return null;
        var raw = style.Get(PropertyId.Transform);
        if (raw is null or CssKeyword { Name: "none" }) return null;

        var transform = CssTransformParser.Parse(raw);
        if (transform.IsNone) return null;

        var local = transform.ToMatrix(box.Frame.Width, box.Frame.Height);
        if (local.IsIdentity) return null;

        var originX = frameX + box.Frame.Width * 0.5;
        var originY = frameY + box.Frame.Height * 0.5;

        // final = T(+origin) × local × T(-origin)
        var preOrigin = Matrix2D.Translate(-originX, -originY);
        var postOrigin = Matrix2D.Translate(originX, originY);
        return postOrigin.Multiply(local).Multiply(preOrigin);
    }

    private static void EmitBorders(Box box, double x, double y, DisplayList list, ComputedStyle style)
    {
        var top = box.Border.Top;
        var right = box.Border.Right;
        var bottom = box.Border.Bottom;
        var left = box.Border.Left;
        if (top + right + bottom + left == 0) return;

        var topColor = style.GetColor(PropertyId.BorderTopColor);
        var rightColor = style.GetColor(PropertyId.BorderRightColor);
        var bottomColor = style.GetColor(PropertyId.BorderBottomColor);
        var leftColor = style.GetColor(PropertyId.BorderLeftColor);

        if (top > 0 && topColor.A > 0)
            list.Add(new FillRect(new Rect(x, y, box.Frame.Width, top), topColor));
        if (right > 0 && rightColor.A > 0)
            list.Add(new FillRect(new Rect(x + box.Frame.Width - right, y, right, box.Frame.Height), rightColor));
        if (bottom > 0 && bottomColor.A > 0)
            list.Add(new FillRect(new Rect(x, y + box.Frame.Height - bottom, box.Frame.Width, bottom), bottomColor));
        if (left > 0 && leftColor.A > 0)
            list.Add(new FillRect(new Rect(x, y, left, box.Frame.Height), leftColor));
    }

    private static void EmitTextFragments(TextBox text, double x, double y, DisplayList list, Func<Box, ComputedStyle?>? styleOverride)
    {
        if (text.Fragments.Count == 0) return;
        var style = EffectiveStyle(text, styleOverride);
        var color = style?.GetColor(PropertyId.Color) ?? CssColor.Black;
        var fontSize = style?.Get(PropertyId.FontSize) switch
        {
            CssLength len => Tessera.Layout.Block.BlockLayout.ToPx(len),
            _ => 16d,
        };
        var spec = FontSpec.FromStyle(style);

        foreach (var frag in text.Fragments)
        {
            if (frag.Text.Length == 0 || string.IsNullOrWhiteSpace(frag.Text)) continue;
            list.Add(new DrawText(
                frag.Text,
                x + frag.X,
                y + frag.Y + frag.Baseline,
                fontSize,
                color,
                spec.Families,
                spec.Bold,
                spec.Italic,
                frag.Shaped));

            if (IsUnderlined(style))
            {
                var underlineY = y + frag.Y + frag.Baseline + Math.Max(1d, fontSize * 0.08d);
                var underlineHeight = Math.Max(1d, Math.Round(fontSize / 16d));
                list.Add(new FillRect(
                    new Rect(x + frag.X, underlineY, frag.Width, underlineHeight),
                    color));
            }
        }
    }

    private static bool IsUnderlined(ComputedStyle? style)
    {
        if (style is null) return false;
        // The `text-decoration` shorthand expands to text-decoration-line, so
        // the underline carrier in cascaded style is normally TextDecorationLine.
        // Keep TextDecoration as a fallback for direct longhand authors.
        return IsUnderlineValue(style.Get(PropertyId.TextDecorationLine))
            || IsUnderlineValue(style.Get(PropertyId.TextDecoration));
    }

    private static bool IsUnderlineValue(CssValue? value) => value switch
    {
        CssKeyword { Name: "underline" } => true,
        CssValueList list => list.Values.OfType<CssKeyword>()
            .Any(k => k.Name.Equals("underline", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };
}
