using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
/// Paint façade. Pre-M1 callers used the <see cref="RenderText"/> path; the
/// full pipeline (parse → style → layout → display list → raster) lives on
/// <see cref="RenderDocument"/>.
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
    /// Run the full M1 pipeline: build a box tree, lay it out, build a paint
    /// display list, replay it onto an ImageSharp surface. The caller supplies
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
        PaintBackend? backend = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = LayoutDocument(document, viewport, defaultFontSize, images, externalStylesheet);

        PaintList displayList;
        using (_diag.Span("paint", "display_list"))
            displayList = new DisplayListBuilder().Build(root);

        var selected = backend ?? SelectBackend();
        using (_diag.Span("paint", $"raster:{selected}".ToLowerInvariant()))
        {
            // DisplayList / DisplayItem are the backend-neutral seam: both
            // backends consume the exact same list. Skia Graphite is now the
            // default — layout uses Skia's shaped text metrics, so painting
            // with ImageSharp (SixLabors metrics) would mismatch layout.
            // ImageSharp stays fully reachable via TESSERA_PAINT_BACKEND=imagesharp.
            if (selected == PaintBackend.SkiaGraphite)
            {
                using var skia = new SkiaGraphiteBackend();
                return skia.Render(displayList, viewport);
            }

            return new ImageSharpBackend(_fonts).RenderToBitmap(displayList, viewport);
        }
    }

    /// <summary>
    /// Resolves the active paint backend. Honors the <c>TESSERA_PAINT_BACKEND</c>
    /// environment variable (<c>skia</c> | <c>imagesharp</c>); defaults to
    /// <see cref="PaintBackend.SkiaGraphite"/> since layout runs on Skia's
    /// shaped text metrics — painting with ImageSharp would mismatch layout.
    /// Set <c>TESSERA_PAINT_BACKEND=imagesharp</c> to force the legacy backend.
    /// </summary>
    public static PaintBackend SelectBackend()
    {
        var flag = Environment.GetEnvironmentVariable("TESSERA_PAINT_BACKEND");
        return flag?.Trim().ToLowerInvariant() switch
        {
            "imagesharp" or "image-sharp" or "sixlabors" => PaintBackend.ImageSharp,
            _ => PaintBackend.SkiaGraphite,
        };
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

        StyleEngine style;
        using (_diag.Span("paint", "style_cascade"))
            style = CreateStyleEngine(document, defaultFontSize, externalStylesheet);

        // Layout now uses Skia's real shaped metrics (SkiaTextMeasurer) instead
        // of DefaultTextMeasurer's ~0.5em heuristic — line breaks, widths, and
        // baselines all match what the Skia paint backend will draw. The
        // measurer caches sized SkFont handles, so it is created per layout
        // call and disposed when done; the typeface stays cached on the
        // FontResolver. DefaultTextMeasurer is kept (it is LayoutEngine's
        // default) so paint-free layout unit tests stay paint-free.
        using var measurer = new SkiaTextMeasurer(_fonts);
        var layoutEngine = new LayoutEngineImpl(style, measurer, images);
        Tessera.Layout.Box.BlockBox root;
        using (_diag.Span("paint", "layout"))
            root = layoutEngine.LayoutDocument(document, viewport);
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

    /// <summary>Legacy M0 path: draw a fixed string onto a viewport-sized canvas.</summary>
    public Image<Rgba32> RenderHelloWorld(string text, Size viewport)
        => RenderText(text, viewport, fontSize: 32f);

    /// <summary>
    /// Renders <paramref name="text"/> in a sans-serif font near the top-left of
    /// a viewport-sized white canvas. Splits on '\n'; no word-wrap. Kept for
    /// the M0 headless smoke path; new callers should prefer <see cref="RenderDocument"/>.
    /// </summary>
    public Image<Rgba32> RenderText(string text, Size viewport, float fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));

        var font = _fonts.GetSansSerifFont(fontSize);

        var image = new Image<Rgba32>(viewport.Width, viewport.Height, new Rgba32(255, 255, 255, 255));
        const float Margin = 16f;
        var lineHeight = font.Size * 1.4f;

        image.Mutate(ctx =>
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                            .Replace('\r', '\n')
                            .Split('\n', StringSplitOptions.None);
            var y = Margin;
            foreach (var line in lines)
            {
                if (line.Length > 0)
                    ctx.DrawText(line, font, Color.Black, new PointF(Margin, y));
                y += lineHeight;
            }
        });

        return image;
    }
}
