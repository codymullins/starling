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
    /// Paint an <em>already-laid-out</em> box tree: build the display list and
    /// rasterize it, skipping cascade + layout entirely. The caller owns the
    /// <paramref name="root"/> (typically produced by an earlier layout that is
    /// still valid). Used by the engine to reuse the pre-script layout for the
    /// final render when nothing layout-affecting changed after scripts ran —
    /// collapsing two full layouts into one. Emits the same <c>display_list</c>
    /// and <c>raster:*</c> spans as <see cref="RenderDocument(Document, LayoutSize, float?, IImageResolver?, Func{Element, StyleSheet?}?, FontFaceRegistry?, double?, ColorScheme, LayoutRect?)"/>
    /// (but no <c>layout</c> / <c>style_cascade</c> span), so telemetry stays
    /// consistent and a span-counting harness can verify exactly one layout ran.
    /// </summary>
    public RenderedBitmap PaintLaidOut(
        Starling.Layout.Box.BlockBox root,
        LayoutSize viewport,
        IImageResolver? images = null,
        FontFaceRegistry? webFonts = null,
        LayoutRect? clipViewport = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root, clipViewport, styleOverride: null, images: images);

        using (_diag.Span("paint", $"raster:{PaintBackendSelector.Selected.ToString().ToLowerInvariant()}"))
        {
            try
            {
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
        ColorScheme colorScheme = ColorScheme.Light,
        CancellationToken ct = default)
        => LayoutDocumentWithStyle(document, viewport, defaultFontSize, images, externalStylesheet, webFonts, nowMs: null, colorScheme, ct);

    /// <summary>Layout overload that threads a frame timestamp through the
    /// cascade. See the matching RenderDocument overload for semantics. The
    /// optional <paramref name="ct"/> is observed by the block-layout pass so a
    /// host's Stop signal interrupts a heavy reflow between sibling boxes.</summary>
    public (Starling.Layout.Box.BlockBox Root, StyleEngine Style) LayoutDocumentWithStyle(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        IImageResolver? images,
        Func<Element, StyleSheet?>? externalStylesheet,
        FontFaceRegistry? webFonts,
        double? nowMs,
        ColorScheme colorScheme = ColorScheme.Light,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, viewport, defaultFontSize, externalStylesheet, _diag, colorScheme);

        var measurer = PaintBackendSelector.CreateMeasurer(_fonts, webFonts);
        try
        {
            var layoutEngine = new LayoutEngineImpl(style, measurer, images, _diag, ct);
            Starling.Layout.Box.BlockBox root;
            using (_diag.Span("paint", "layout"))
                root = layoutEngine.LayoutDocument(document, viewport, nowMs);
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

    /// <summary>Build only the cascade — no layout, no display list. Used by
    /// <c>BoxLayoutHost</c> (in <c>Starling.Engine</c>) to answer
    /// <c>getComputedStyle</c> reads for purely-cascaded properties
    /// (<c>visibility</c>, <c>opacity</c>, <c>display</c>, …) without paying the
    /// full layout pass. On google.com this saves ~210 ms — the trace shows
    /// the page's startup script reads <c>getComputedStyle(el).visibility</c>
    /// before any geometry is needed, and the engine previously forced layout
    /// to answer that.</summary>
    public StyleEngine BuildStyleEngine(
        Document document,
        LayoutSize viewport,
        float? defaultFontSize,
        Func<Element, StyleSheet?>? externalStylesheet,
        ColorScheme colorScheme = ColorScheme.Light)
    {
        ArgumentNullException.ThrowIfNull(document);
        using (_diag.Span("paint", "style_cascade.standalone"))
            return CreateStyleEngine(document, viewport, defaultFontSize, externalStylesheet, _diag, colorScheme);
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
            // The user-stylesheet override is content-addressable by font size.
            // Parse once per distinct size and reuse across layouts so repeated
            // re-layouts during script execution don't re-parse a constant sheet.
            var sheet = s_userFontSizeSheetCache.GetOrAdd(
                defaultFontSize.Value,
                size => CssParser.ParseStyleSheet(
                    FormattableString.Invariant($"body {{ font-size: {size}px; }}"),
                    StyleOrigin.User,
                    diag));
            style.AddStyleSheet(sheet);
        }

        // Walk the tree in document order so `<style>` and `<link rel=stylesheet>`
        // contribute to the cascade in source order — required by [CSS Cascade
        // 4 §6.3]: tree order is the tiebreaker after origin/importance/specificity.
        AddAuthorStylesheets(document, externalStylesheet, style, diag);

        return style;
    }

    // Inline-<style> parse cache. Layout runs every time a script reads
    // geometry/computed style after a DOM mutation; each layout currently
    // walks the document and re-parses every <style> block. Google's
    // homepage has ~50 inline <style>s, so without the cache each layout
    // pays for ~50 ParseStyleSheet + selector-index builds. Weak-keyed on
    // the Element so the entry GCs with the document.
    private sealed class CachedSheet
    {
        public string Source { get; init; } = string.Empty;
        public StyleSheet Sheet { get; init; } = null!;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Element, CachedSheet> s_inlineSheetCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<float, StyleSheet> s_userFontSizeSheetCache = new();

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
                {
                    StyleSheet sheet;
                    // `Element.TextContent` allocates a fresh string each
                    // access; ReferenceEquals would always miss. Value compare
                    // — bounded by total CSS bytes per layout (small).
                    if (s_inlineSheetCache.TryGetValue(element, out var cached)
                        && string.Equals(cached.Source, source, StringComparison.Ordinal))
                    {
                        sheet = cached.Sheet;
                    }
                    else
                    {
                        sheet = CssParser.ParseStyleSheet(source, StyleOrigin.Author, diag);
                        s_inlineSheetCache.Remove(element);
                        s_inlineSheetCache.Add(element, new CachedSheet { Source = source, Sheet = sheet });
                    }
                    style.AddStyleSheet(sheet);
                }
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
