using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css;
using Starling.Css.Cascade;
using Starling.Css.Media;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Layout.Tree;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutEngineImpl = Starling.Layout.LayoutEngine;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint;

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
        FontFaceRegistry? webFonts = null,
        ColorScheme colorScheme = ColorScheme.Light)
        => RenderDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null, colorScheme);

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
        double? nowMs,
        ColorScheme colorScheme = ColorScheme.Light,
        LayoutRect? clipViewport = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs, colorScheme);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root, clipViewport, styleOverride: null, images: images);

        using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
        {
            // DisplayList is the renderer-neutral seam. ImageSharp.Drawing 3
            // is the only backend after the Skia shim removal. When a clip
            // viewport is supplied the bitmap is sized to it (and translated by
            // its offset); otherwise the full layout viewport is rendered.
            try
            {
                // Backend construction (font collection load especially) runs
                // inside the raster span but outside any child, so on a cold
                // process it silently widens the raster→command_record gap.
                // Span it so that cost is attributable.
                IPaintBackend backend;
                using (_diag.Span("paint", "raster.backend_init"))
                    backend = PaintBackendSelector.Create(_fonts, webFonts, _diag);
                using (backend)
                    return clipViewport is { } clip
                        ? backend.Render(displayList, clip)
                        : backend.Render(displayList, viewport);
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
    public Starling.Layout.Box.BlockBox LayoutDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize = null,
        IImageResolver? images = null,
        Func<Element, StyleSheet?>? externalStylesheet = null,
        FontFaceRegistry? webFonts = null,
        ColorScheme colorScheme = ColorScheme.Light)
        => LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null, colorScheme);

    /// <summary>Layout at a frame timestamp — see the matching RenderDocument
    /// overload for semantics.</summary>
    public Starling.Layout.Box.BlockBox LayoutDocument(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs,
        ColorScheme colorScheme = ColorScheme.Light)
    {
        var (root, _) = LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs, colorScheme);
        return root;
    }

    /// <summary>
    /// Same as <see cref="LayoutDocument(Document, LayoutSize, float?, IImageResolver?, Func{Element, StyleSheet?}?, FontFaceRegistry?, ColorScheme)"/> but also returns the
    /// <see cref="StyleEngine"/> used for the cascade, so interactive callers
    /// can recompute styles for individual elements when state changes
    /// (<c>:hover</c>, <c>:focus</c>, <c>:active</c>) without re-running layout.
    /// </summary>
    public (Starling.Layout.Box.BlockBox Root, StyleEngine Style) LayoutDocumentWithStyle(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize = null,
        IImageResolver? images = null,
        Func<Element, StyleSheet?>? externalStylesheet = null,
        FontFaceRegistry? webFonts = null,
        ColorScheme colorScheme = ColorScheme.Light)
        => LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null, colorScheme);

    /// <summary>Layout overload that threads a frame timestamp through the
    /// cascade. See the matching RenderDocument overload for semantics.</summary>
    public (Starling.Layout.Box.BlockBox Root, StyleEngine Style) LayoutDocumentWithStyle(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs,
        ColorScheme colorScheme = ColorScheme.Light)
    {
        ArgumentNullException.ThrowIfNull(document);

        Starling.Common.Diagnostics.NativeCallTrace.Mark("layout.begin");

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, viewport, defaultFontSize, externalStylesheet, _diag, colorScheme);

        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag);
            Starling.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport, nowMs);
            Starling.Common.Diagnostics.NativeCallTrace.Mark("layout.end");
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
    /// fresh-engine path of <see cref="RenderDocument(Document, LayoutSize, float?, IImageResolver?, Func{Element, StyleSheet?}?, FontFaceRegistry?, double?, ColorScheme, LayoutRect?)"/>
    /// would reseed every call, restarting animations on each frame).
    /// Callers tick the engines before invoking this.
    /// </summary>
    public RenderedBitmap RenderWithStyle(
        Document document,
        StyleEngine style,
        LayoutSize viewport,
        IImageResolver? images,
        FontFaceRegistry? webFonts,
        double nowMs,
        LayoutRect? clipViewport = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(style);

        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag);
            Starling.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport, nowMs);

            PaintList displayList;
            using (_diag.Span("paint", "display_list"))
                displayList = new DisplayListBuilder().Build(root, clipViewport, styleOverride: null, images: images);

            using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
            {
                IPaintBackend backend;
                using (_diag.Span("paint", "raster.backend_init"))
                    backend = PaintBackendSelector.Create(_fonts, webFonts, _diag);
                using (backend)
                    return clipViewport is { } clip
                        ? backend.Render(displayList, clip)
                        : backend.Render(displayList, viewport);
            }
        }
        finally
        {
            (measurer as IDisposable)?.Dispose();
        }
    }

    private static StyleEngine CreateStyleEngine(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        Func<Element, StyleSheet?>? externalStylesheet,
        IDiagnostics diag,
        ColorScheme colorScheme)
    {
        var style = new StyleEngine(diagnostics: diag);
        // Expose the UA's preferred color scheme through @media
        // (prefers-color-scheme: …) and the real viewport size through both
        // @media width/height/orientation queries and the vw/vh/sv*/lv*/dv*
        // length units. Without the viewport here the cascade falls back to
        // MediaContext.Default (1024×768) and every page lays out for a fixed
        // screen regardless of the actual window size. Evaluated on every
        // Compute call.
        style.MediaContext = style.MediaContext with
        {
            ColorScheme = colorScheme,
            ViewportWidthPx = viewport.Width,
            ViewportHeightPx = viewport.Height,
        };

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
