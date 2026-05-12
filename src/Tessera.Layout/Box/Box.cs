using Tessera.Css.Cascade;
using Tessera.Dom;

namespace Tessera.Layout.Box;

public enum BoxKind : byte
{
    BlockContainer,
    Inline,
    Text,
    Replaced,
    AnonymousBlock,
}

/// <summary>
/// A box in the layout tree. Frame is in the parent's content-edge coordinate
/// space; the painter walks the tree and applies parent translations.
/// </summary>
public abstract class Box
{
    protected Box(BoxKind kind, ComputedStyle? style, Element? element)
    {
        Kind = kind;
        Style = style;
        Element = element;
    }

    public BoxKind Kind { get; }
    public ComputedStyle? Style { get; }
    public Element? Element { get; }
    public Box? Parent { get; internal set; }
    public List<Box> Children { get; } = [];

    public Rect Frame { get; internal set; }
    public Edges Margin { get; internal set; }
    public Edges Padding { get; internal set; }
    public Edges Border { get; internal set; }

    public void AppendChild(Box child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent = this;
        Children.Add(child);
    }
}

public sealed class BlockBox : Box
{
    public BlockBox(ComputedStyle? style, Element? element)
        : base(BoxKind.BlockContainer, style, element) { }
}

public sealed class AnonymousBlockBox : Box
{
    public AnonymousBlockBox(ComputedStyle? parentStyle) : base(BoxKind.AnonymousBlock, parentStyle, element: null) { }
}

public sealed class InlineBox : Box
{
    public InlineBox(ComputedStyle? style, Element? element) : base(BoxKind.Inline, style, element) { }
}

public sealed class TextBox : Box
{
    public TextBox(string text, ComputedStyle? style) : base(BoxKind.Text, style, element: null)
    {
        Text = text;
    }

    public string Text { get; }

    /// <summary>
    /// Populated by the inline formatting context: one entry per line fragment
    /// drawn from this text run. Painter consumes this list.
    /// </summary>
    public List<TextFragment> Fragments { get; } = [];
}

/// <summary>
/// A single line-aligned fragment of text emitted by the inline formatting
/// context. <see cref="X"/> / <see cref="Y"/> are in the enclosing block's
/// content-area coordinate space.
/// </summary>
public readonly record struct TextFragment(string Text, double X, double Y, double Width, double Height, double Baseline);
