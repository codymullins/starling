using Tessera.Css.Cascade;
using Tessera.Dom;
using Tessera.Layout.Block;
using Tessera.Layout.Box;
using Tessera.Layout.Text;
using Tessera.Layout.Tree;

namespace Tessera.Layout;

/// <summary>
/// Top-level layout façade. Consumes a parsed <see cref="Document"/>, runs the
/// style engine, builds a box tree, then performs block + inline formatting.
/// </summary>
/// <remarks>
/// v1 scope is intentionally narrow: block stacking, inline text with
/// word-wrap, and simple adjacent-sibling margin collapse. Floats, positioning,
/// flex, grid, and tables are deferred (wp:M5+). The box tree's <c>Frame</c>
/// values are CSS px in the document's coordinate space.
/// </remarks>
public sealed class LayoutEngine
{
    private readonly StyleEngine _style;
    private readonly ITextMeasurer _measurer;

    public LayoutEngine(StyleEngine style, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _measurer = measurer ?? DefaultTextMeasurer.Instance;
    }

    public BlockBox LayoutDocument(Document document, Size viewport)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new BoxTreeBuilder(_style);
        var root = builder.Build(document);

        var block = new BlockLayout(_measurer, viewport);
        block.Layout(root);
        return root;
    }
}
