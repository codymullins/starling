using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Layout.Tree;

namespace Starling.Paint.DisplayList;

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
    /// Overdraw margin (CSS px) added around the viewport before culling.
    /// Items whose AABB falls within this margin of the visible region are
    /// still emitted so partial-pixel scroll and sub-pixel rounding don't pop
    /// content at the edges.
    /// </summary>
    private const double OverdrawMargin = 64d;

    /// <summary>
    /// Builds a display list from a laid-out box tree (no culling — every item
    /// is emitted). <paramref name="styleOverride"/> is an optional per-box
    /// hook used to swap in fresh styles without re-laying out — interactive
    /// shells call it with hover/focus-recascaded styles so
    /// <c>a:hover { color: red }</c> repaints in red at the same glyph
    /// positions. Returning <c>null</c> for a box keeps the layout-time
    /// <see cref="Box.Style"/>.
    /// </summary>
    public DisplayList Build(BlockBox root, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null)
        => Build(root, viewport: null, styleOverride, images);

    /// <summary>
    /// Builds a display list, optionally culling to a page-coordinate
    /// <paramref name="viewport"/>. When <paramref name="viewport"/> is
    /// non-null only items whose (post-transform) page-coordinate AABB
    /// intersects the viewport (expanded by <see cref="OverdrawMargin"/>) are
    /// emitted, and transformed subtrees with no surviving items skip their
    /// <see cref="PushTransform"/>/<see cref="PopTransform"/> bracket entirely
    /// so the cost stays O(items on screen). When null, every item is emitted
    /// (the full-page behavior the headless screenshot path relies on).
    /// </summary>
    public DisplayList Build(BlockBox root, Rect? viewport, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new DisplayList();
        Rect? cull = viewport is { } v
            ? new Rect(v.X - OverdrawMargin, v.Y - OverdrawMargin, v.Width + 2 * OverdrawMargin, v.Height + 2 * OverdrawMargin)
            : null;
        Visit(box: root, list, originX: 0, originY: 0, current: Matrix2D.Identity, cull, styleOverride, images, slice: null);
        return list;
    }

    /// <summary>
    /// Builds the display-list <em>slice</em> for a single compositor layer
    /// rooted at <paramref name="sliceRoot"/>: the items <paramref name="sliceRoot"/>
    /// and its subtree paint, EXCLUDING any box (other than the slice root
    /// itself) for which <paramref name="isLayerBoundary"/> returns true — those
    /// boxes are descendant layers and the <see cref="Compositor.LayerTreeBuilder"/>
    /// emits them into their own slices. <paramref name="originX"/> /
    /// <paramref name="originY"/> are the page-coord content origin of the slice
    /// root's PARENT (i.e. the same origin its enclosing <see cref="Visit"/> call
    /// would have used), so a slice is painted in the SAME page coordinate space
    /// as the flat build — the layer's transform/opacity are applied later, at
    /// composite time, not baked into the slice.
    /// <para>
    /// When <paramref name="suppressRootTransform"/> is true the slice root's own
    /// CSS <c>transform</c> bracket is skipped: the layer carries that transform
    /// (<see cref="Compositor.CompositorLayer.Transform"/>) and applies it at
    /// composite time, so the slice holds the untransformed (upright) content.
    /// Nested non-promoted transformed descendants inside the slice still get
    /// their normal push/pop brackets.
    /// </para>
    /// </summary>
    internal DisplayList BuildLayerSlice(
        Box sliceRoot,
        double originX,
        double originY,
        Func<Box, bool> isLayerBoundary,
        bool suppressRootTransform,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null)
    {
        ArgumentNullException.ThrowIfNull(sliceRoot);
        ArgumentNullException.ThrowIfNull(isLayerBoundary);
        var list = new DisplayList();
        var slice = new LayerSlice(sliceRoot, isLayerBoundary);
        if (suppressRootTransform)
        {
            // The slice root's transform is owned by its layer, so paint its
            // own box + children directly (no transform bracket) in local space.
            var frameX = originX + sliceRoot.Frame.X;
            var frameY = originY + sliceRoot.Frame.Y;
            PaintBoxAndChildren(sliceRoot, list, frameX, frameY, Matrix2D.Identity, cull: null, styleOverride, images, slice);
        }
        else
        {
            Visit(sliceRoot, list, originX, originY, Matrix2D.Identity, cull: null, styleOverride, images, slice);
        }
        return list;
    }

    /// <summary>
    /// Threaded through the recursion during a layer-slice build. Carries the
    /// slice root (which is always emitted even though it is itself a layer
    /// boundary) and the predicate identifying descendant layer boundaries to
    /// exclude. Null on the flat full-document build, where every box is emitted.
    /// </summary>
    private sealed record LayerSlice(Box Root, Func<Box, bool> IsBoundary);

    private static ComputedStyle? EffectiveStyle(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => styleOverride?.Invoke(box) ?? box.Style;

    private static void Visit(Box box, DisplayList list, double originX, double originY, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images, LayerSlice? slice)
    {
        // Layer-slice mode: a descendant that is itself a layer root paints into
        // its OWN slice, not this one. The slice root is exempt — it is the box
        // this slice is rooted at, so it must paint here.
        if (slice is not null && !ReferenceEquals(box, slice.Root) && slice.IsBoundary(box))
            return;

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
        {
            // Items inside the transform must be culled using their
            // post-transform AABB, so compose the matrix onto the active one
            // and paint the subtree into a scratch list. Only if it produced
            // visible items do we emit the bracket — an entirely off-screen
            // transformed subtree contributes nothing and stays O(on-screen).
            var composed = current.Multiply(transformMatrix!.Value);
            var scratch = new DisplayList();
            PaintBoxAndChildren(box, scratch, frameX, frameY, composed, cull, styleOverride, images, slice);
            if (scratch.Items.Count > 0)
            {
                list.Add(new PushTransform(transformMatrix.Value));
                foreach (var item in scratch.Items)
                    list.Add(item);
                list.Add(PopTransform.Instance);
            }
            return;
        }

        PaintBoxAndChildren(box, list, frameX, frameY, current, cull, styleOverride, images, slice);
    }

    /// <summary>
    /// Adds <paramref name="item"/> to <paramref name="list"/> unless culling is
    /// active and the item's page-coordinate AABB (the local <paramref name="bounds"/>
    /// transformed by <paramref name="current"/>) does not intersect the
    /// viewport.
    /// </summary>
    private static void Emit(DisplayList list, DisplayItem item, Rect bounds, Matrix2D current, Rect? cull)
    {
        if (cull is { } c && !Intersects(TransformedAabb(bounds, current), c)) return;
        list.Add(item);
    }

    private static Rect TransformedAabb(Rect r, Matrix2D m)
    {
        if (m.IsIdentity) return r;
        var (x0, y0) = m.Transform(r.X, r.Y);
        var (x1, y1) = m.Transform(r.X + r.Width, r.Y);
        var (x2, y2) = m.Transform(r.X + r.Width, r.Y + r.Height);
        var (x3, y3) = m.Transform(r.X, r.Y + r.Height);
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool Intersects(Rect a, Rect b)
        => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;

    private static void PaintBoxAndChildren(Box box, DisplayList list, double frameX, double frameY, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images, LayerSlice? slice)
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
                var bounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
                Emit(list, new FillRect(bounds, bg, FillRectPixelAlignment.Preserve), bounds, current, cull);
            }

            // CSS Backgrounds 3 §3 — background-image paints inside the
            // box's padding box (border-box is the default origin per spec,
            // but at this layout fidelity the padding+border distinction
            // is unobservable and using the frame is correct).
            EmitBackgroundImage(box, frameX, frameY, list, current, cull, style, images);

            // Borders. Painter renders one stroke per side that has a non-zero width.
            EmitBorders(box, frameX, frameY, list, current, cull, style);
        }

        // Inline content: text fragments live on TextBoxes, positioned in their
        // anonymous-block parent's content box.
        if (box is TextBox textBox)
        {
            EmitTextFragments(textBox, frameX, frameY, list, current, cull, styleOverride);
            return; // Text boxes have no children.
        }

        // Replaced inline content (currently <img>): its Frame was set by the
        // inline formatting context relative to the anonymous-block container,
        // so frameX/frameY (which already includes that translation) is its
        // document-space top-left.
        if (box is ImageBox imageBox)
        {
            var bounds = new Rect(frameX, frameY, imageBox.Frame.Width, imageBox.Frame.Height);
            Emit(list, new DrawImage(bounds, imageBox.Source), bounds, current, cull);
            return; // ImageBox has no children.
        }

        // Children paint after backgrounds. Their frames are in our content-box
        // coords, so push our padding+border origin.
        var contentOriginX = frameX + box.Border.Left + box.Padding.Left;
        var contentOriginY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Visit(child, list, contentOriginX, contentOriginY, current, cull, styleOverride, images, slice);
    }

    /// <summary>
    /// Resolve <c>background-image</c> via <paramref name="images"/> and emit
    /// a sliced <see cref="DrawImage"/> using <c>background-position</c> +
    /// <c>background-size</c> to compute the source rectangle. This is the
    /// path that makes CSS sprite sheets render — the box's frame defines the
    /// visible window, the sprite PNG is the source, and bg-position picks
    /// which slice maps to the window.
    /// </summary>
    private static void EmitBackgroundImage(Box box, double frameX, double frameY, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style, IImageResolver? images)
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

        var dest = new Rect(frameX + destX, frameY + destY, destW, destH);
        Emit(list, new DrawImage(dest, decoded, new Rect(srcX, srcY, srcW, srcH)), dest, current, cull);
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
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
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
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
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
    internal static Matrix2D? TryGetTransformMatrix(Box box, Func<Box, ComputedStyle?>? styleOverride, double frameX, double frameY)
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

    private static void EmitBorders(Box box, double x, double y, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style)
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

        {
            var b = new Rect(x, y, box.Frame.Width, top);
            Emit(list, new FillRect(b, topColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (right > 0 && rightColor.A > 0)
        {
            var b = new Rect(x + box.Frame.Width - right, y, right, box.Frame.Height);
            Emit(list, new FillRect(b, rightColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (bottom > 0 && bottomColor.A > 0)
        {
            var b = new Rect(x, y + box.Frame.Height - bottom, box.Frame.Width, bottom);
            Emit(list, new FillRect(b, bottomColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (left > 0 && leftColor.A > 0)
        {
            var b = new Rect(x, y, left, box.Frame.Height);
            Emit(list, new FillRect(b, leftColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
    }

    private static void EmitTextFragments(TextBox text, double x, double y, DisplayList list, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride)
    {
        if (text.Fragments.Count == 0) return;
        var style = EffectiveStyle(text, styleOverride);
        var color = style?.GetColor(PropertyId.Color) ?? CssColor.Black;
        var fontSize = style?.Get(PropertyId.FontSize) switch
        {
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
            _ => 16d,
        };
        var spec = FontSpec.FromStyle(style);

        foreach (var frag in text.Fragments)
        {
            if (frag.Text.Length == 0 || string.IsNullOrWhiteSpace(frag.Text)) continue;
            var baselineY = y + frag.Y + frag.Baseline;
            // Cull bounds: the glyph run sits on the baseline. Cover ascent
            // (≈ font size above the baseline) and a small descent below so the
            // AABB safely encloses the rasterized glyphs.
            var glyphBounds = new Rect(
                x + frag.X,
                baselineY - fontSize,
                frag.Width,
                fontSize * 1.3d);
            Emit(list, new DrawText(
                frag.Text,
                x + frag.X,
                y + frag.Y,
                fontSize,
                color,
                spec.Families,
                spec.Bold,
                spec.Italic,
                frag.Shaped), glyphBounds, current, cull);

            if (IsUnderlined(style))
            {
                // TODO: Underline position and thickness are font-dependent
                // as are overline and strikethrough.
                // Investigate whether browsers actually use the font metric or just eyeball it.
                var underlineY = baselineY;
                var underlineHeight = Math.Max(1d, Math.Round(fontSize / 16d));
                var ul = new Rect(x + frag.X, underlineY, frag.Width, underlineHeight);
                Emit(list, new FillRect(ul, color, FillRectPixelAlignment.SnapToDevicePixels), ul, current, cull);
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
