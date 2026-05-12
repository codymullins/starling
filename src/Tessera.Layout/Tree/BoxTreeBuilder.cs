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

    public BoxTreeBuilder(StyleEngine style)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
    }

    public BlockBox Build(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.DocumentElement;
        if (root is null)
            return new BlockBox(style: null, element: null);

        var rootStyle = _style.Compute(root);
        var rootBox = new BlockBox(rootStyle, root);
        BuildChildren(root, rootStyle, rootBox);
        WrapInlinesInAnonymousBlocks(rootBox);
        return rootBox;
    }

    private void BuildChildren(Node parentNode, ComputedStyle parentStyle, Box.Box parentBox)
    {
        for (var child = parentNode.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case Element element:
                    var elementStyle = _style.Compute(element);
                    var display = DisplayKeyword(elementStyle);
                    if (display == "none") continue;
                    if (display == "contents")
                    {
                        BuildChildren(element, elementStyle, parentBox);
                        continue;
                    }
                    Box.Box box = display == "inline" || display == "inline-block"
                        ? new InlineBox(elementStyle, element)
                        : new BlockBox(elementStyle, element);
                    parentBox.AppendChild(box);
                    BuildChildren(element, elementStyle, box);
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
    private static void WrapInlinesInAnonymousBlocks(Box.Box parent)
    {
        // AnonymousBlocks are the wrappers; don't re-wrap their (inline) children.
        if (parent.Kind != BoxKind.BlockContainer)
            return;

        // Always wrap inline runs in an anonymous block — even when the block
        // contains only inlines — so the block formatting context sees a
        // uniform list of block-level children.
        var newChildren = new List<Box.Box>();
        AnonymousBlockBox? bucket = null;
        foreach (var child in parent.Children)
        {
            var isInline = child.Kind is BoxKind.Inline or BoxKind.Text;
            if (isInline)
            {
                bucket ??= new AnonymousBlockBox(parent.Style);
                child.Parent = bucket;
                bucket.Children.Add(child);
            }
            else
            {
                if (bucket is not null && bucket.Children.Count > 0)
                {
                    newChildren.Add(bucket);
                    bucket.Parent = parent;
                    bucket = null;
                }
                newChildren.Add(child);
                child.Parent = parent;
            }
        }

        if (bucket is not null && bucket.Children.Count > 0)
        {
            bucket.Parent = parent;
            newChildren.Add(bucket);
        }

        parent.Children.Clear();
        parent.Children.AddRange(newChildren);

        foreach (var child in parent.Children) WrapInlinesInAnonymousBlocks(child);
    }

    private static string DisplayKeyword(ComputedStyle style)
        => style.Get(PropertyId.Display) is CssKeyword k ? k.Name.ToLowerInvariant() : "inline";
}
