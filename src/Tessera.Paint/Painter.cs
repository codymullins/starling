using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Css;
using Tessera.Css.Cascade;
using Tessera.Css.Parser;
using Tessera.Dom;
using Tessera.Layout.Tree;
using Tessera.Paint.Backend;
using Tessera.Paint.DisplayList;
using LayoutEngineImpl = Tessera.Layout.LayoutEngine;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint;

/// <summary>
/// Paint façade for the full pipeline: parse → style → layout → display list →
/// Skia Graphite raster. Skia is the engine's sole rasterizer; there is no
/// managed fallback. The native shim (<c>libtessera_skia</c>) is a hard
/// requirement — see <c>native/build-skia.sh</c>.
/// </summary>
public sealed class Painter
{
    private readonly FontResolver _fonts;
    private readonly IDiagnostics _diag;

    public Painter(FontResolver? fonts = null, IDiagnostics? diag = null)
    {
        _fonts = fonts ?? FontResolver.Default;
        _diag = diag ?? NoopDiagnostics.Instance;
    }

    /// <summary>
    /// Run the full pipeline: build a box tree, lay it out, build a paint
    /// display list, and rasterize it with Skia Graphite. The caller supplies
    /// a parsed <see cref="Document"/> and the viewport size in CSS px. Pass an
    /// <paramref name="images"/> resolver to render <c>&lt;img&gt;</c> elements;
    /// without one, every <c>&lt;img&gt;</c> degrades to its <c>alt</c> text.
    /// <paramref name="externalStylesheet"/> supplies parsed CSS for each
    /// <c>&lt;link rel="stylesheet"&gt;</c> element — the painter inserts the
    /// returned sheet at that element's position in document order so the
    /// cascade matches what a browser sees. Without it, only inline
    /// <c>&lt;style&gt;</c> blocks contribute.
    /// </summary>
    public RenderedBitmap RenderDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize = null,
        IImageResolver? images = null,
        Func<Element, StyleSheet?>? externalStylesheet = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root);

        using (_diag.Span("paint", "raster:skiagraphite"))
        {
            // DisplayList / DisplayItem is the renderer-neutral seam. Skia
            // Graphite is the engine's sole rasterizer — layout (above) measures
            // with Skia's shaped metrics, so paint and layout always agree.
            using var skia = new SkiaGraphiteBackend();
            return skia.Render(displayList, viewport);
        }
    }

    /// <summary>
    /// Run cascade + layout without rasterizing. Returns the laid-out box tree
    /// for callers that want to walk it themselves — interactive shells (a real
    /// browser frame) consume this so taps, selection, and Cmd-F can resolve
    /// against structure instead of pixels.
    /// </summary>
    public Tessera.Layout.Box.BlockBox LayoutDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize = null,
        IImageResolver? images = null,
        Func<Element, StyleSheet?>? externalStylesheet = null)
    {
        var (root, _) = LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet);
        return root;
    }

    /// <summary>
    /// Same as <see cref="LayoutDocument"/> but also returns the
    /// <see cref="StyleEngine"/> used for the cascade, so interactive callers
    /// can recompute styles for individual elements when state changes
    /// (<c>:hover</c>, <c>:focus</c>, <c>:active</c>) without re-running layout.
    /// </summary>
    public (Tessera.Layout.Box.BlockBox Root, StyleEngine Style) LayoutDocumentWithStyle(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize = null,
        IImageResolver? images = null,
        Func<Element, StyleSheet?>? externalStylesheet = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.begin");

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, defaultFontSize, externalStylesheet);

        // Layout measures with Skia's real shaped metrics (SkiaTextMeasurer) so
        // line breaks, widths, and baselines match exactly what the Skia
        // Graphite backend draws. The measurer caches sized SkFont handles, so
        // it is created per layout call and disposed when done. (The layout
        // engine's own DefaultTextMeasurer remains for paint-free layout unit
        // tests, but the Painter pipeline is always Skia.)
        using var measurer = new SkiaTextMeasurer(_fonts);
        var layoutEngine = new LayoutEngineImpl(style, measurer, images);
        Tessera.Layout.Box.BlockBox root;
        using (_diag.Span("paint", "layout"))
            root = layoutEngine.LayoutDocument(document, viewport);
        Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.end");
        return (root, style);
    }

    private static StyleEngine CreateStyleEngine(
        Document document,
        float? defaultFontSize,
        Func<Element, StyleSheet?>? externalStylesheet)
    {
        var style = new StyleEngine();

        if (defaultFontSize is > 0)
        {
            style.AddStyleSheet(CssParser.ParseStyleSheet(
                FormattableString.Invariant($"body {{ font-size: {defaultFontSize.Value}px; }}"),
                StyleOrigin.User));
        }

        // Walk the tree in document order so `<style>` and `<link rel=stylesheet>`
        // contribute to the cascade in source order — required by [CSS Cascade
        // 4 §6.3]: tree order is the tiebreaker after origin/importance/specificity.
        AddAuthorStylesheets(document, externalStylesheet, style);

        return style;
    }

    private static void AddAuthorStylesheets(
        Node node,
        Func<Element, StyleSheet?>? externalStylesheet,
        StyleEngine style)
    {
        if (node is Element element)
        {
            if (element.LocalName == "style")
            {
                var source = element.TextContent;
                if (!string.IsNullOrWhiteSpace(source))
                    style.AddStyleSheet(CssParser.ParseStyleSheet(source, StyleOrigin.Author));
            }
            else if (element.LocalName == "link" && externalStylesheet is not null)
            {
                var sheet = externalStylesheet(element);
                if (sheet is not null)
                    style.AddStyleSheet(sheet);
            }
        }

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            AddAuthorStylesheets(child, externalStylesheet, style);
    }
}
