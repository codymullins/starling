using System.Diagnostics;
using Tessera.Common.Diagnostics;
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
    private readonly IImageResolver _images;
    private readonly IDiagnostics _diag;

    public LayoutEngine(
        StyleEngine style,
        ITextMeasurer? measurer = null,
        IImageResolver? images = null,
        IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _measurer = measurer ?? DefaultTextMeasurer.Instance;
        _images = images ?? NullImageResolver.Instance;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
    }

    public BlockBox LayoutDocument(Document document, Size viewport)
        => LayoutDocument(document, viewport, nowMs: null);

    /// <summary>
    /// Layout overload that threads a monotonic frame clock into the cascade.
    /// When <paramref name="nowMs"/> is non-null, each element is computed
    /// via <see cref="StyleEngine.ComputeWithAnimations"/> so the box tree
    /// reflects any in-flight CSS animations + transitions at that instant.
    /// </summary>
    public BlockBox LayoutDocument(Document document, Size viewport, double? nowMs)
    {
        ArgumentNullException.ThrowIfNull(document);

        _diag.Counter("layout.runs", 1);
        using var span = _diag.Span("layout", "run");
        Activity.Current?.SetTag("layout.viewport_width", viewport.Width);
        Activity.Current?.SetTag("layout.viewport_height", viewport.Height);

        BlockBox root;
        using (_diag.Span("layout", "box_tree_build"))
        {
            var builder = new BoxTreeBuilder(_style, _images, nowMs);
            root = builder.Build(document);
        }

        BlockLayout block;
        using (_diag.Span("layout", "block"))
        {
            block = new BlockLayout(_measurer, viewport, _diag);
            block.Layout(root);
        }

        // Second pass: place position:absolute / fixed descendants and apply
        // position:relative offsets. The viewport rect doubles as the
        // initial containing block and as the fixed-positioning anchor.
        using (_diag.Span("layout", "position"))
        {
            var positioning = new Tessera.Layout.Position.PositionLayout(block, viewport);
            positioning.LayoutPositioned(root);
        }

        Activity.Current?.SetTag("layout.boxes", CountBoxes(root));
        return root;
    }

    private static int CountBoxes(Box.Box box)
    {
        var n = 1;
        foreach (var child in box.Children)
            n += CountBoxes(child);
        return n;
    }
}
