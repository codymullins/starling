using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Layout;
using Tessera.Layout.Box;

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
    public DisplayList Build(BlockBox root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new DisplayList();
        Visit(root, new Rect(0, 0, root.Frame.Width, root.Frame.Height), list, originX: 0, originY: 0);
        return list;
    }

    private static void Visit(Box box, Rect rootBounds, DisplayList list, double originX, double originY)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        // Backgrounds for block-ish boxes (skip TextBoxes — backgrounds are on their parents).
        if (box.Kind is BoxKind.BlockContainer or BoxKind.AnonymousBlock && box.Style is { } style)
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
            EmitTextFragments(textBox, frameX, frameY, list);
            return; // Text boxes have no children.
        }

        // Children paint after backgrounds. Their frames are in our content-box
        // coords, so push our padding+border origin.
        var contentOriginX = frameX + box.Border.Left + box.Padding.Left;
        var contentOriginY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Visit(child, rootBounds, list, contentOriginX, contentOriginY);
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

    private static void EmitTextFragments(TextBox text, double x, double y, DisplayList list)
    {
        if (text.Fragments.Count == 0) return;
        var style = text.Style;
        var color = style?.GetColor(PropertyId.Color) ?? CssColor.Black;
        var fontSize = style?.Get(PropertyId.FontSize) switch
        {
            CssLength len => Tessera.Layout.Block.BlockLayout.ToPx(len),
            _ => 16d,
        };
        var fontFamily = style?.Get(PropertyId.FontFamily) switch
        {
            CssKeyword kw => kw.Name,
            CssString s => s.Value,
            CssValueList vl when vl.Values.Count > 0 => FirstFamily(vl),
            _ => "sans-serif",
        };
        var bold = IsBold(style);
        var italic = IsItalic(style);

        foreach (var frag in text.Fragments)
        {
            if (frag.Text.Length == 0 || string.IsNullOrWhiteSpace(frag.Text)) continue;
            list.Add(new DrawText(
                frag.Text,
                x + frag.X,
                y + frag.Y + frag.Baseline,
                fontSize,
                color,
                fontFamily,
                bold,
                italic));
        }
    }

    private static string FirstFamily(CssValueList list)
    {
        var first = list.Values[0];
        return first switch
        {
            CssKeyword kw => kw.Name,
            CssString s => s.Value,
            _ => "sans-serif",
        };
    }

    private static bool IsBold(ComputedStyle? style)
    {
        if (style is null) return false;
        return style.Get(PropertyId.FontWeight) switch
        {
            CssKeyword { Name: "bold" } => true,
            CssNumber n => n.Value >= 600,
            _ => false,
        };
    }

    private static bool IsItalic(ComputedStyle? style)
    {
        if (style is null) return false;
        return style.Get(PropertyId.FontStyle) is CssKeyword { Name: "italic" or "oblique" };
    }
}
