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
/// raster. ImageSharp.Drawing 3.0 is the sole paint backend after the
/// Skia/Graphite native shim was removed; the engine is once again pure
/// managed end-to-end.
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
    /// display list, and rasterize it with ImageSharp.Drawing 3. The caller
    /// supplies a parsed <see cref="Document"/> and the viewport size in CSS
    /// px. Pass an <paramref name="images"/> resolver to render
    /// <c>&lt;img&gt;</c> elements; without one, every <c>&lt;img&gt;</c>
    /// degrades to its <c>alt</c> text.
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
        => RenderDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null);

    /// <summary>
    /// Render at a specific frame timestamp — drives CSS animations and
    /// transitions through the cascade so the produced bitmap reflects the
    /// engine state at <paramref name="nowMs"/>. Callers must Tick the
    /// underlying <see cref="StyleEngine.AnimationEngine"/> and
    /// <see cref="StyleEngine.TransitionEngine"/> to <paramref name="nowMs"/>
    /// before calling this — the painter does not own the engines and so
    /// cannot advance them itself.
    /// </summary>
    public RenderedBitmap RenderDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root);

        using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
        {
            // DisplayList is the renderer-neutral seam. ImageSharp.Drawing 3
            // is the only backend after the Skia shim removal.
            try
            {
                using var backend = PaintBackendSelector.Create(_fonts, webFonts, _diag);
                return backend.Render(displayList, viewport);
            }
            catch (Exception ex)
            {
                // Surface failures (backend construction, WebGPU init, native
                // shim missing, etc.) through diagnostics before unwinding so
                // Aspire shows the full exception on the raster span instead
                // of just a silently failed activity.
                _diag.LogException("paint", ex, $"raster backend '{PaintBackendSelector.Selected}' failed");
                throw;
            }
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
        => LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null);

    /// <summary>Layout at a frame timestamp — see the matching RenderDocument
    /// overload for semantics.</summary>
    public Tessera.Layout.Box.BlockBox LayoutDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs)
    {
        var (root, _) = LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs);
        return root;
    }

    /// <summary>
    /// Same as <see cref="LayoutDocument(Document, LayoutSize, float?, IImageResolver?, Func{Element, StyleSheet?}?, FontFaceRegistry?)"/> but also returns the
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
        => LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null);

    /// <summary>Layout overload that threads a frame timestamp through the
    /// cascade. See the matching RenderDocument overload for semantics.</summary>
    public (Tessera.Layout.Box.BlockBox Root, StyleEngine Style) LayoutDocumentWithStyle(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs)
    {
        ArgumentNullException.ThrowIfNull(document);

        Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.begin");

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, defaultFontSize, externalStylesheet, _diag);

        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag);
            Tessera.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport, nowMs);
            Tessera.Common.Diagnostics.NativeCallTrace.Mark("layout.end");
            return (root, style);
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Re-layout and paint <paramref name="document"/> using the caller's
    /// pre-built <see cref="StyleEngine"/> at frame timestamp
    /// <paramref name="nowMs"/>. Used by the frame loop so the
    /// <see cref="StyleEngine.AnimationEngine"/> / <see cref="StyleEngine.TransitionEngine"/>
    /// state seeded on a retained engine survives across paints (the
    /// fresh-engine path of <see cref="RenderDocument(Document, LayoutSize, float?, IImageResolver?, Func{Element, StyleSheet?}?, FontFaceRegistry?, double?)"/>
    /// would reseed every call, restarting animations on each frame).
    /// Callers tick the engines before invoking this.
    /// </summary>
    public RenderedBitmap RenderWithStyle(
        Document document,
        StyleEngine style,
        LayoutSize viewport,
        IImageResolver? images,
        FontFaceRegistry? webFonts,
        double nowMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);

        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag);
            Tessera.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport, nowMs);

            PaintList displayList;
            using (_diag.Span("paint", "display_list"))
                displayList = new DisplayListBuilder().Build(root);

            using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
            {
                using var backend = PaintBackendSelector.Create(_fonts, webFonts, _diag);
                return backend.Render(displayList, viewport);
            }
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
