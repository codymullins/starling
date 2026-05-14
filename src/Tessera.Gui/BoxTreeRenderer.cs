using Microsoft.Maui.Layouts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Selectors;
using Tessera.Css.Values;
using Tessera.Layout.Box;
using Color = Microsoft.Maui.Graphics.Color;
using DomElement = Tessera.Dom.Element;

namespace Tessera.Gui;

/// <summary>
/// Walks a laid-out box tree and produces a MAUI view tree: an
/// <see cref="AbsoluteLayout"/> sized to the document's full height, with one
/// child per visible primitive (background fill, border stroke, text fragment,
/// image, replaced element).
/// </summary>
/// <remarks>
/// This is the interactive sibling of the ImageSharp rasterizer:
/// <list type="bullet">
///   <item>Native pointer + tap gestures mean hover and link navigation work.</item>
///   <item>The full document height is realized so a <see cref="ScrollView"/>
///         around the result scrolls past the viewport.</item>
/// </list>
/// Body text is emitted as plain <see cref="Label"/>s — MAUI Labels are not
/// user-selectable. Drag-to-select / copy is layered on top by the GUI shell
/// (<c>MainPage</c>) via a custom highlight overlay driven by
/// <c>Tessera.Layout.Selection.PageSelection</c>, which hit-tests the same box
/// tree and flows selection across paragraph boundaries.
/// </remarks>
public static class BoxTreeRenderer
{
    /// <summary>Build the native view tree for the laid-out page.</summary>
    /// <param name="root">Laid-out box tree.</param>
    /// <param name="style">
    /// Optional style engine. When supplied, link hover triggers a true CSS
    /// :hover round-trip — the engine recomputes the anchor's style with
    /// <see cref="SelectorMatchContext.HoveredElement"/> set, and the diff is
    /// applied to the corresponding view. Without it, hover falls back to a
    /// simple opacity hint.
    /// </param>
    /// <param name="onLinkActivated">Tap handler for hyperlinks.</param>
    public static AbsoluteLayout Build(
        BlockBox root,
        StyleEngine? style = null,
        Action<string>? onLinkActivated = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var layout = new AbsoluteLayout
        {
            WidthRequest = Math.Max(1, root.Frame.Width),
            HeightRequest = Math.Max(1, root.Frame.Height),
            BackgroundColor = Colors.White,
        };
        var context = new BuildContext(layout, onLinkActivated, style);
        Emit(root, context, originX: 0, originY: 0);
        return layout;
    }

    private sealed record BuildContext(AbsoluteLayout Layout, Action<string>? OnLink, StyleEngine? Style);

    private static void Emit(Box box, BuildContext ctx, double originX, double originY)
    {
        var layout = ctx.Layout;
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        // Background fill — mirrors DisplayListBuilder's BlockContainer/AnonymousBlock/Inline rule.
        if (HasPaintedBox(box) && box.Style is { } style)
        {
            var bg = style.GetColor(PropertyId.BackgroundColor);
            if (bg.A > 0)
                AddView(layout, new BoxView { Color = ToMauiColor(bg) },
                    new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height));

            EmitBorders(box, frameX, frameY, layout, style);
        }

        switch (box)
        {
            case AnonymousBlockBox ab when IsPureInlineText(ab):
                EmitAnonymousBlockAsParagraph(ab, frameX, frameY, ctx);
                return;
            case TextBox tb:
                EmitTextFragments(tb, frameX, frameY, ctx);
                return;
            case ImageBox img:
                EmitImage(img, frameX, frameY, layout);
                return;
        }

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            Emit(child, ctx, contentX, contentY);
    }

    /// <summary>
    /// True when the anonymous block contains only inline text/anchor content —
    /// no images, no atomic inline-blocks (form controls). Those need
    /// independent absolutely-positioned views and can't ride inside a
    /// FormattedString, so we fall back to per-fragment Labels for them.
    /// </summary>
    private static bool IsPureInlineText(Box box)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextBox: continue;
                case InlineBox ib when ib.Style?.Get(PropertyId.Display) is CssKeyword { Name: "inline-block" }:
                    return false;
                case InlineBox ib:
                    if (!IsPureInlineText(ib)) return false;
                    continue;
                default:
                    return false;
            }
        }
        return true;
    }

    private static void EmitAnonymousBlockAsParagraph(AnonymousBlockBox ab, double x, double y, BuildContext ctx)
    {
        var formatted = new FormattedString();
        var linkSpans = new List<(Span Span, DomElement Anchor, Color BaseColor, TextDecorations BaseDecorations, FontAttributes BaseAttrs)>();
        BuildFormattedString(ab, formatted, ctx, currentAnchor: null, linkSpans);
        if (formatted.Spans.Count == 0) return;

        var fontSize = ResolveFontSize(ab.Style);
        var color = ToMauiColor(ab.Style?.GetColor(PropertyId.Color) ?? CssColor.Black);

        var label = new Label
        {
            FormattedText = formatted,
            TextColor = color,
            FontSize = fontSize,
            LineBreakMode = LineBreakMode.WordWrap,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            VerticalTextAlignment = TextAlignment.Start,
        };

        // If any link spans live inside this paragraph and we have a style
        // engine, hook a pointer recognizer that drives a real :hover round-
        // trip on those anchors. MAUI Spans don't surface per-span pointer
        // events so the highlight applies to all link spans in the paragraph
        // when the pointer is anywhere inside the Label — a known coarse-
        // grained approximation; refining requires platform hit-testing.
        if (linkSpans.Count > 0)
        {
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) =>
            {
                if (ctx.Style is { } style)
                {
                    foreach (var (span, anchor, _, _, baseAttrs) in linkSpans)
                    {
                        var hovered = style.Compute(anchor, new SelectorMatchContext { HoveredElement = anchor });
                        span.TextColor = ToMauiColor(hovered.GetColor(PropertyId.Color));
                        span.TextDecorations = IsUnderlined(hovered) ? TextDecorations.Underline : TextDecorations.None;
                        // Italic toggles cheaply; bold is left out — its wider
                        // metrics would reflow inside the fixed-width Label rect.
                        span.FontAttributes = IsItalic(hovered)
                            ? baseAttrs | FontAttributes.Italic
                            : baseAttrs & ~FontAttributes.Italic;
                    }
                }
                else
                {
                    label.Opacity = 0.7;
                }
            };
            pointer.PointerExited += (_, _) =>
            {
                if (ctx.Style is not null)
                {
                    foreach (var (span, _, baseColor, baseDecorations, baseAttrs) in linkSpans)
                    {
                        span.TextColor = baseColor;
                        span.TextDecorations = baseDecorations;
                        span.FontAttributes = baseAttrs;
                    }
                }
                else
                {
                    label.Opacity = 1.0;
                }
            };
            label.GestureRecognizers.Add(pointer);
        }

        var width = Math.Max(1, ab.Frame.Width);
        var height = Math.Max(1, ab.Frame.Height + 8);
        AddView(ctx.Layout, label, new Rect(x, y, width, height));
    }

    private static void BuildFormattedString(
        Box box,
        FormattedString formatted,
        BuildContext ctx,
        DomElement? currentAnchor,
        List<(Span Span, DomElement Anchor, Color BaseColor, TextDecorations BaseDecorations, FontAttributes BaseAttrs)> linkSpans)
    {
        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextBox tb:
                {
                    var text = string.Concat(tb.Fragments.Select(f => f.Text));
                    if (text.Length == 0) continue;
                    var color = ToMauiColor(tb.Style?.GetColor(PropertyId.Color) ?? CssColor.Black);
                    var span = new Span
                    {
                        Text = text,
                        TextColor = color,
                        FontSize = ResolveFontSize(tb.Style),
                        FontAttributes = (IsBold(tb.Style), IsItalic(tb.Style)) switch
                        {
                            (true, true) => FontAttributes.Bold | FontAttributes.Italic,
                            (true, false) => FontAttributes.Bold,
                            (false, true) => FontAttributes.Italic,
                            _ => FontAttributes.None,
                        },
                        TextDecorations = IsUnderlined(tb.Style) || currentAnchor is not null
                            ? TextDecorations.Underline
                            : TextDecorations.None,
                    };
                    if (currentAnchor is not null)
                    {
                        var href = currentAnchor.GetAttribute("href");
                        if (!string.IsNullOrWhiteSpace(href) && ctx.OnLink is not null)
                        {
                            var capturedHref = href;
                            span.GestureRecognizers.Add(new TapGestureRecognizer
                            {
                                Command = new Command(() => ctx.OnLink(capturedHref)),
                            });
                        }
                        linkSpans.Add((span, currentAnchor, color, span.TextDecorations, span.FontAttributes));
                    }
                    formatted.Spans.Add(span);
                    break;
                }
                case InlineBox ib:
                {
                    var nextAnchor = ib.Element is DomElement { LocalName: "a" } a ? a : currentAnchor;
                    BuildFormattedString(ib, formatted, ctx, nextAnchor, linkSpans);
                    break;
                }
            }
        }
    }

    private static bool HasPaintedBox(Box box)
        => box.Frame.Width > 0
           && box.Frame.Height > 0
           && box.Kind is BoxKind.BlockContainer or BoxKind.AnonymousBlock or BoxKind.Inline;

    private static void EmitBorders(Box box, double x, double y, AbsoluteLayout layout, ComputedStyle style)
    {
        var (top, right, bottom, left) = (box.Border.Top, box.Border.Right, box.Border.Bottom, box.Border.Left);
        if (top + right + bottom + left == 0) return;

        var topColor = style.GetColor(PropertyId.BorderTopColor);
        var rightColor = style.GetColor(PropertyId.BorderRightColor);
        var bottomColor = style.GetColor(PropertyId.BorderBottomColor);
        var leftColor = style.GetColor(PropertyId.BorderLeftColor);

        if (top > 0 && topColor.A > 0)
            AddView(layout, new BoxView { Color = ToMauiColor(topColor) },
                new Rect(x, y, box.Frame.Width, top));
        if (right > 0 && rightColor.A > 0)
            AddView(layout, new BoxView { Color = ToMauiColor(rightColor) },
                new Rect(x + box.Frame.Width - right, y, right, box.Frame.Height));
        if (bottom > 0 && bottomColor.A > 0)
            AddView(layout, new BoxView { Color = ToMauiColor(bottomColor) },
                new Rect(x, y + box.Frame.Height - bottom, box.Frame.Width, bottom));
        if (left > 0 && leftColor.A > 0)
            AddView(layout, new BoxView { Color = ToMauiColor(leftColor) },
                new Rect(x, y, left, box.Frame.Height));
    }

    private static void EmitTextFragments(TextBox text, double originX, double originY, BuildContext ctx)
    {
        if (text.Fragments.Count == 0) return;
        var style = text.Style;
        var baseColor = ToMauiColor(style?.GetColor(PropertyId.Color) ?? CssColor.Black);
        var fontSize = ResolveFontSize(style);
        var bold = IsBold(style);
        var italic = IsItalic(style);
        var underline = IsUnderlined(style);
        var anchor = FindLinkAnchor(text);
        var linkHref = anchor?.GetAttribute("href");

        foreach (var frag in text.Fragments)
        {
            if (string.IsNullOrWhiteSpace(frag.Text)) continue;

            var label = new Label
            {
                Text = frag.Text,
                TextColor = baseColor,
                FontSize = fontSize,
                FontAttributes = (bold, italic) switch
                {
                    (true, true) => FontAttributes.Bold | FontAttributes.Italic,
                    (true, false) => FontAttributes.Bold,
                    (false, true) => FontAttributes.Italic,
                    _ => FontAttributes.None,
                },
                TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None,
                LineBreakMode = LineBreakMode.NoWrap,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
            };

            if (!string.IsNullOrWhiteSpace(linkHref) && ctx.OnLink is not null)
            {
                var capturedHref = linkHref!;
                label.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() => ctx.OnLink(capturedHref)),
                });

                var pointer = new PointerGestureRecognizer();
                if (ctx.Style is { } styleEngine && anchor is { } anchorEl)
                {
                    // Real :hover round-trip: recompute the anchor's style with
                    // hover context set, apply the diff to this label.
                    pointer.PointerEntered += (_, _) =>
                    {
                        var hovered = styleEngine.Compute(anchorEl, new SelectorMatchContext { HoveredElement = anchorEl });
                        label.TextColor = ToMauiColor(hovered.GetColor(PropertyId.Color));
                        if (IsUnderlined(hovered)) label.TextDecorations = TextDecorations.Underline;
                    };
                    pointer.PointerExited += (_, _) =>
                    {
                        label.TextColor = baseColor;
                        label.TextDecorations = underline ? TextDecorations.Underline : TextDecorations.None;
                    };
                }
                else
                {
                    pointer.PointerEntered += (_, _) => label.Opacity = 0.7;
                    pointer.PointerExited += (_, _) => label.Opacity = 1.0;
                }
                label.GestureRecognizers.Add(pointer);
            }

            AddView(ctx.Layout, label,
                new Rect(originX + frag.X, originY + frag.Y, Math.Max(frag.Width + 2, 1), Math.Max(frag.Height + 4, 1)));
        }
    }

    private static void EmitImage(ImageBox img, double x, double y, AbsoluteLayout layout)
    {
        // ImageBox.Source is a backend-neutral DecodedImage (straight RGBA8888).
        // MAUI's image pipeline wants encoded bytes, so re-wrap the pixels in an
        // ImageSharp Image<Rgba32> and encode to in-memory PNG. Encode happens
        // once per render; if this becomes a hot path the encoded bytes should
        // be cached per source image.
        var decoded = img.Source;
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0) return;

        byte[] bytes;
        using (var srcImg = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(
                   decoded.Pixels.Span, decoded.Width, decoded.Height))
        using (var ms = new MemoryStream())
        {
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(srcImg, ms);
            bytes = ms.ToArray();
        }
        var image = new Microsoft.Maui.Controls.Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(bytes)),
            Aspect = Aspect.Fill,
        };
        AddView(layout, image, new Rect(x, y, img.Frame.Width, img.Frame.Height));
    }

    private static void AddView(AbsoluteLayout layout, View view, Rect bounds)
    {
        AbsoluteLayout.SetLayoutBounds(view, bounds);
        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
        layout.Children.Add(view);
    }

    private static Color ToMauiColor(CssColor c)
        => Color.FromRgba(c.R, c.G, c.B, c.A);

    private static double ResolveFontSize(ComputedStyle? style)
    {
        if (style is null) return 16;
        return style.Get(PropertyId.FontSize) switch
        {
            CssLength len => LengthToPx(len),
            CssNumber n => n.Value,
            _ => 16,
        };
    }

    // Local copy of the length→px conversion. The layout engine's version is
    // internal; for visual font sizing the same constants give the same answer.
    private static double LengthToPx(CssLength length) => length.Unit switch
    {
        CssLengthUnit.Px => length.Value,
        CssLengthUnit.Pt => length.Value * 4d / 3d,
        CssLengthUnit.Pc => length.Value * 16d,
        CssLengthUnit.In => length.Value * 96d,
        CssLengthUnit.Cm => length.Value * 96d / 2.54d,
        CssLengthUnit.Mm => length.Value * 96d / 25.4d,
        CssLengthUnit.Em => length.Value * 16d,
        CssLengthUnit.Rem => length.Value * 16d,
        _ => length.Value,
    };

    private static bool IsBold(ComputedStyle? style)
        => style?.Get(PropertyId.FontWeight) switch
        {
            CssKeyword { Name: "bold" } => true,
            CssNumber n => n.Value >= 600,
            _ => false,
        };

    private static bool IsItalic(ComputedStyle? style)
        => style?.Get(PropertyId.FontStyle) is CssKeyword { Name: "italic" or "oblique" };

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

    /// <summary>
    /// Walk a TextBox's ancestors looking for an enclosing &lt;a&gt; element.
    /// The inline formatter flattens span/anchor InlineBox wrappers for layout
    /// purposes, but it preserves the parent chain, so we can recover the
    /// anchor element (and its href) from the box tree without re-walking the
    /// DOM. Returning the element rather than the bare href keeps it usable
    /// for :hover restyling via the style engine.
    /// </summary>
    private static DomElement? FindLinkAnchor(Box box)
    {
        for (var node = box.Parent; node is not null; node = node.Parent)
        {
            if (node is InlineBox ib && ib.Element is DomElement { LocalName: "a" } a)
                return a;
        }
        return null;
    }
}
