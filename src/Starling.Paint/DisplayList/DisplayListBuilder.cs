using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout;
using Tessera.Layout.Box;
using Tessera.Layout.Text;

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
    public DisplayList Build(BlockBox root, Func<Box, ComputedStyle?>? styleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new DisplayList();
        Visit(root, new Rect(0, 0, root.Frame.Width, root.Frame.Height), list, originX: 0, originY: 0, styleOverride);
        return list;
    }

    private static ComputedStyle? EffectiveStyle(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => styleOverride?.Invoke(box) ?? box.Style;

    private static void Visit(Box box, Rect rootBounds, DisplayList list, double originX, double originY, Func<Box, ComputedStyle?>? styleOverride)
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

        PaintBoxAndChildren(box, rootBounds, list, frameX, frameY, styleOverride);

        if (transformed)
            list.Add(PopTransform.Instance);
    }

    private static void PaintBoxAndChildren(Box box, Rect rootBounds, DisplayList list, double frameX, double frameY, Func<Box, ComputedStyle?>? styleOverride)
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
            Visit(child, rootBounds, list, contentOriginX, contentOriginY, styleOverride);
    }

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
        return style.Get(PropertyId.TextDecoration) switch
        {
            CssKeyword { Name: "underline" } => true,
            CssValueList list => list.Values.OfType<CssKeyword>()
                .Any(k => k.Name.Equals("underline", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }
}
