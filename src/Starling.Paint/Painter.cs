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
/// raster. Skia Graphite is the default rasterizer; ImageSharp.Drawing 3.0 is
/// selectable via the <c>TESSERA_PAINT_BACKEND</c> env var on builds compiled
/// with <c>EnableImageSharpDrawing3=true</c>.
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
        Func<Element, StyleSheet?>? externalStylesheet = null,
        FontFaceRegistry? webFonts = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root);

        using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
        {
            // DisplayList is the renderer-neutral seam. Skia Graphite is the
            // default; setting TESSERA_PAINT_BACKEND=imagesharp swaps in the
            // ImageSharp.Drawing 3.0 backend on builds that enabled it.
            using var backend = PaintBackendSelector.Create(_fonts, webFonts, _diag);
            return backend.Render(displayList, viewport);
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
        Func<Element, StyleSheet?>? externalStylesheet = null,
        FontFaceRegistry? webFonts = null)
    {
        var (root, _) = LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts);
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
        Func<Element, StyleSheet?>? externalStylesheet = null,
        FontFaceRegistry? webFonts = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.begin");

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, defaultFontSize, externalStylesheet, _diag);

        // Layout measures with Skia's real shaped metrics (SkiaTextMeasurer) so
        // line breaks, widths, and baselines match exactly what the Skia
        // Graphite backend draws. The measurer caches sized SkFont handles, so
        // it is created per layout call and disposed when done. (The layout
        // engine's own DefaultTextMeasurer remains for paint-free layout unit
        // tests, but the Painter pipeline is always Skia.)
        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag);
            Tessera.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport);
            Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.end");
            return (root, style);
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    private static StyleEngine CreateStyleEngine(
        Document document,
        float? defaultFontSize,
        Func<Element, StyleSheet?>? externalStylesheet,
        IDiagnostics diag)
    {
        var style = new StyleEngine(diagnostics: diag);

        if (defaultFontSize is > 0)
        {
            style.AddStyleSheet(CssParser.ParseStyleSheet(
                FormattableString.Invariant($"body {{ font-size: {defaultFontSize.Value}px; }}"),
                StyleOrigin.User,
                diag));
        }

        // Walk the tree in document order so `<style>` and `<link rel=stylesheet>`
        // contribute to the cascade in source order — required by [CSS Cascade
        // 4 §6.3]: tree order is the tiebreaker after origin/importance/specificity.
        AddAuthorStylesheets(document, externalStylesheet, style, diag);

        return style;
    }

    private static void AddAuthorStylesheets(
        Node node,
        Func<Element, StyleSheet?>? externalStylesheet,
        StyleEngine style,
        IDiagnostics diag)
    {
        if (node is Element element)
        {
            if (element.LocalName == "style")
            {
                var source = element.TextContent;
                if (!string.IsNullOrWhiteSpace(source))
                    style.AddStyleSheet(CssParser.ParseStyleSheet(source, StyleOrigin.Author, diag));
            }
            else if (element.LocalName == "link" && externalStylesheet is not null)
            {
                var sheet = externalStylesheet(element);
                if (sheet is not null)
                    style.AddStyleSheet(sheet);
            }
        }

        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            AddAuthorStylesheets(child, externalStylesheet, style, diag);
    }
}
