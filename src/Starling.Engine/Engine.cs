using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using Starling.Common;
using Starling.Common.Diagnostics;
using Starling.Common.Encoding;
using Starling.Bindings;
using Starling.Css;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Loop;
using Starling.Net;
using Starling.Paint;
using Starling.Url;
using LayoutSize = Starling.Layout.Size;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Engine;

/// <summary>
/// Engine façade. One call: load a URL, parse HTML, run the static
/// style/layout/paint pipeline, and write a bitmap. The full Browser / Page /
/// Frame composition per 01_ARCHITECTURE.md §E lands with interactive browsing.
/// </summary>
/// <remarks>
/// As of the M1 static-rendering closure the renderer uses the document-level
/// pipeline in <see cref="Painter.RenderDocument(Document, Starling.Layout.Size, float?, Starling.Layout.Tree.IImageResolver?, System.Func{Element, Starling.Css.Parser.StyleSheet?}?, FontFaceRegistry?, Starling.Css.Media.ColorScheme)"/> for file and network inputs.
/// </remarks>
public sealed class StarlingEngine
{
    private const int MaxRedirects = 10;

    private readonly IDiagnostics _diag;
    private readonly Painter _painter;
    private readonly Func<StarlingHttpClient> _httpFactory;

    static StarlingEngine()
    {
        // Register the BCL CodePages provider once so WHATWG legacy
        // single-byte (windows-1250…1258, ISO-8859-2…16, KOI8-*, mac*)
        // and CJK (Shift_JIS, GBK, gb18030, Big5, EUC-KR, …) labels
        // resolve. CodePages is a pure-managed NuGet package.
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public StarlingEngine(IDiagnostics? diagnostics = null, Painter? painter = null,
        Func<StarlingHttpClient>? httpFactory = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _painter = painter ?? new Painter(diag: _diag);
        _httpFactory = httpFactory ?? (() => new StarlingHttpClient());
    }

    /// <summary>
    /// Render <paramref name="url"/> into a PNG written to <paramref name="outputPath"/>.
    /// Returns <c>true</c> on success.
    /// </summary>
    /// <remarks>
    /// Supports <c>file://</c>, <c>http://</c>, and <c>https://</c> URLs. The
    /// returned <see cref="RenderOutcome.DisplayText"/> is a diagnostic text
    /// summary; the PNG is produced from the full parsed document.
    /// </remarks>
    public Result<RenderOutcome, RenderError> Render(string url, RenderOptions options, string outputPath)
        => RenderAsync(url, options, outputPath, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<Result<RenderOutcome, RenderError>> RenderAsync(
        string url, RenderOptions options, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(outputPath);

        _diag.Counter("engine.page_load", 1);
        using var _ = _diag.Span("engine", $"render {url} -> {outputPath}");
        Activity.Current?.SetTag("http.url", url);
        Activity.Current?.SetTag("viewport.w", options.Viewport.Width);
        Activity.Current?.SetTag("viewport.h", options.Viewport.Height);
        Activity.Current?.SetTag("font_size", options.FontSize);
        Activity.Current?.SetTag("output.path", outputPath);

        var parsed = UrlParser.Parse(url);
        if (parsed.IsErr)
            return Fail($"URL parse failed: {parsed.Error}");

        var u = parsed.Value;
        // One HTTP client (one connection pool) for the whole page load: the
        // document fetch and every resource fetch (css/js/fonts/images) share
        // it, so same-origin requests reuse a keep-alive transport instead of
        // re-doing DNS+TCP+TLS per request. Cheap to construct (no socket until
        // first use), so it is created even for file:// renders.
        using var http = _httpFactory();
        string html;
        try
        {
            if (u.IsFile)
            {
                var path = u.ToFileSystemPath();
                if (!File.Exists(path))
                    return Fail($"File not found: {path}");
                using (_diag.Span("engine", "read_file"))
                {
                    html = File.ReadAllText(path);
                    Activity.Current?.SetTag("file.path", path);
                    Activity.Current?.SetTag("html.bytes", html.Length);
                }
            }
            else if (u.IsHttp || u.IsHttps)
            {
                Result<(string Html, StarlingUrl FinalUrl, Starling.Net.Http.ConnectionSecurity? Security), RenderError> fetched;
                using (_diag.Span("engine", "fetch_html"))
                {
                    fetched = await FetchHtmlAsync(u, http, ct).ConfigureAwait(false);
                }
                if (fetched.IsErr)
                    return Fail(fetched.Error.Message);
                html = fetched.Value.Html;
                // Resolve subsequent relative URLs (images, stylesheets, fonts)
                // against the post-redirect host. Typing `https://google.com`
                // 301s to `https://www.google.com/`, and the page's
                // `<img src="/images/..."` only resolves on the www host.
                u = fetched.Value.FinalUrl;
            }
            else
            {
                return Fail($"Unsupported scheme '{u.Scheme}' for M0.");
            }
        }
        catch (IOException ex)
        {
            return Fail(ex.Message);
        }

        Document doc;
        using (_diag.Span("engine", "parse_html"))
        {
            Activity.Current?.SetTag("html.bytes", html.Length);
            // The engine runs page JavaScript, so HTML parsing uses the
            // scripting flag ENABLED (WHATWG HTML §13.2). This makes
            // <noscript> contents inert raw text instead of parsed elements.
            doc = Html.HtmlParser.Parse(html, _diag, scriptingEnabled: true);
        }

        using var images = new ImageFetcher(_diag, http);
        using var stylesheets = new StylesheetFetcher(_diag, http);
        using var scripts = new ScriptFetcher(_diag, http);

        // Start downloading external scripts now, concurrently with the
        // stylesheet/image fetch below (and the font fetch that follows).
        // Scripts have no data dependency on stylesheets or fonts being
        // *downloaded* — only script *execution* must wait for stylesheets to
        // apply, which is enforced where scripts run below — so serializing the
        // downloads only inflates the critical path. FetchAllAsync enumerates
        // the <script> elements synchronously before its first await, so this
        // reads the DOM before any concurrent fetch can touch it. The task is
        // awaited just before scripts execute.
        var scriptsFetch = scripts.FetchAllAsync(doc, baseUrl: u, ct);

        using var webFonts = new FontFaceRegistry();
        using var fontFaceFetcher = new FontFaceFetcher(_diag, http);

        // Fonts are declared by @font-face in the author stylesheets, so the font
        // fetch depends on the stylesheets being downloaded + parsed — but not on
        // the images. Chain the font fetch onto the stylesheet fetch and run that
        // chain alongside the image fetch, so web fonts start downloading the
        // moment the sheets land instead of waiting out the whole image wave.
        async Task FetchSheetsThenFontsAsync()
        {
            await stylesheets.FetchAllAsync(doc, baseUrl: u, ct).ConfigureAwait(false);
            using (_diag.Span("engine", "fetch_fonts"))
                await fontFaceFetcher
                    .FetchAllAsync(EnumerateAuthorSheets(doc, u, stylesheets), webFonts, ct)
                    .ConfigureAwait(false);
        }

        using (_diag.Span("engine", "fetch_resources"))
        {
            await Task.WhenAll(
                images.FetchAllAsync(doc, baseUrl: u, options.Viewport.Width, options.FontSize, ct),
                FetchSheetsThenFontsAsync()
            ).ConfigureAwait(false);
        }

        // Run page JavaScript. Mutations to the DOM (text content, new
        // elements, <img src=…> writes) land on `doc` and feed into the
        // layout/paint pass below. Best-effort: a script that throws is
        // logged via the realm's console sink and the remaining scripts
        // still run.
        //
        // The host built for JS holds the box tree it last laid out. When a
        // script reads geometry (so a layout was materialized) and nothing
        // layout-affecting changes afterwards, the engine reuses that exact box
        // tree for the final paint instead of running a second full cascade +
        // layout (Win B). `jsLayoutHost` survives past the script block; it is
        // nulled when a late resource invalidates the tree, and reuse is
        // additionally gated on HasLayout + an unchanged mutation version below.
        BoxLayoutHost? jsLayoutHost = null;
        {
            // The script downloads were kicked off above and have been running
            // concurrently with the stylesheet/font fetch; await their
            // completion now, before any script executes.
            using (_diag.Span("engine", "fetch_scripts"))
            {
                await scriptsFetch.ConfigureAwait(false);
            }
            if (scripts.Scripts.Count > 0 || scripts.ModuleScripts.Count > 0)
            {
                // Lay out against the pre-script DOM so JS can call
                // getBoundingClientRect / offsetWidth / getComputedStyle and
                // receive real numbers. The host is handed a recompute
                // delegate keyed on the document's mutation version, so a read
                // issued after a DOM mutation in the same script run lazily
                // re-runs layout and reflects the post-mutation geometry.
                var viewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);
                // Lazy pre-script layout: only run the (expensive) full layout
                // if a script actually reads geometry / computed style. Pages
                // whose scripts never touch layout (analytics beacons, etc.)
                // skip the pass entirely — the render_document layout below is
                // then the only one that runs. The span lives inside the
                // delegate so it is recorded only when layout truly happens.
                (Starling.Layout.Box.BlockBox Root, Starling.Css.Cascade.StyleEngine Style) Relayout()
                {
                    using (_diag.Span("engine", "prelayout_for_js"))
                        return _painter.LayoutDocumentWithStyle(
                            doc, viewport, options.FontSize, images, stylesheets.Resolve, webFonts,
                            colorScheme: options.PreferredColorScheme);
                }
                jsLayoutHost = new BoxLayoutHost(doc, Relayout);

                using (_diag.Span("engine", "run_scripts"))
                {
                    await RunScriptsAsync(doc, u, scripts, jsLayoutHost, ct).ConfigureAwait(false);
                }

                // Snapshot how many images/stylesheets were loaded before the
                // post-script fetch. If that fetch pulls in anything new, a late
                // image intrinsic size or a new sheet's cascade can change
                // layout — so we must NOT reuse the pre-script box tree.
                var imagesBefore = images.LoadedCount;
                var sheetsBefore = stylesheets.LoadedCount;

                // Scripts may have added <img> or new stylesheet links;
                // re-run the resource fetch so the post-script DOM is
                // fully loaded before paint. URL dedupe makes the second
                // pass cheap for unchanged content.
                using (_diag.Span("engine", "fetch_resources_post_js"))
                {
                    await Task.WhenAll(
                        images.FetchAllAsync(doc, baseUrl: u, options.Viewport.Width, options.FontSize, ct),
                        stylesheets.FetchAllAsync(doc, baseUrl: u, ct)
                    ).ConfigureAwait(false);
                }

                if (images.LoadedCount != imagesBefore || stylesheets.LoadedCount != sheetsBefore)
                {
                    // A late resource arrived — its intrinsic size / cascade may
                    // shift layout. Discard the cached tree and re-layout.
                    jsLayoutHost = null;
                }
            }
        }

        // Prefetch CSS-referenced background-image url()s now so the paint
        // pipeline can resolve them synchronously when emitting display items.
        using (_diag.Span("engine", "fetch_backgrounds"))
        {
            await images
                .FetchBackgroundsAsync(EnumerateAuthorSheets(doc, u, stylesheets), u, ct)
                .ConfigureAwait(false);
        }

        // Extract display text from the post-script DOM so JS-driven
        // mutations (innerText/textContent writes, appended children, fetch
        // result rendering) land in RenderOutcome.DisplayText.
        var displayText = ExtractDisplayText(doc);

        var renderViewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);

        // Win B: reuse the layout JS already materialized when it is provably
        // still valid, painting straight from that box tree instead of running a
        // second full cascade + layout. ALL of these must hold:
        //   • a layout was actually materialized for JS — with the lazy host that
        //     means a script read geometry / computed style (HasLayout);
        //   • the DOM has not mutated since that layout
        //     (doc.MutationVersion == host.LaidOutVersion);
        //   • no layout-affecting resource arrived after it (jsLayoutHost is
        //     nulled above when fetch_resources_post_js loaded a new image/sheet);
        //   • viewport / font-size / color-scheme are unchanged within a single
        //     render (the host laid out with these exact options, so identical by
        //     construction).
        // Background images prefetched in fetch_backgrounds above are paint-only
        // (they never change layout) and resolve through the same images resolver
        // during the display-list build, so they do not block reuse.
        var canReuse = jsLayoutHost is { } host
            && host.HasLayout
            && doc.MutationVersion == host.LaidOutVersion;

        Starling.Common.Image.RenderedBitmap bitmap;
        using (_diag.Span("engine", "render_document"))
        {
            if (canReuse)
            {
                _diag.Counter("engine.render.reused_prelayout", 1);
                var (reuseRoot, _) = jsLayoutHost!.Materialized;
                bitmap = _painter.PaintLaidOut(reuseRoot, renderViewport, images, webFonts);
            }
            else
            {
                bitmap = _painter.RenderDocument(
                    doc,
                    renderViewport,
                    options.FontSize,
                    images,
                    stylesheets.Resolve,
                    webFonts,
                    colorScheme: options.PreferredColorScheme);
            }
            Activity.Current?.SetTag("image.w", bitmap.Width);
            Activity.Current?.SetTag("image.h", bitmap.Height);
        }

        try
        {
            try
            {
                using (_diag.Span("engine", "save_png"))
                {
                    EnsureOutputDirectory(outputPath);
                    // PNG encode stays via ImageSharp for now: wrap the
                    // backend-neutral RGBA8888 bytes back into an Image<Rgba32>
                    // purely for the encoder. LoadPixelData copies.
                    using var image = Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                        bitmap.Rgba, bitmap.Width, bitmap.Height);
                    image.SaveAsPng(outputPath);
                }
            }
            catch (IOException ex)
            {
                return Fail($"Save failed: {ex.Message}");
            }

            _diag.Log(DiagLevel.Info, "engine",
                $"Wrote {outputPath} ({bitmap.Width}x{bitmap.Height}, text length={displayText.Length}).");

            return Result<RenderOutcome, RenderError>.Ok(
                new RenderOutcome(outputPath, bitmap.Width, bitmap.Height, displayText));
        }
        finally
        {
            bitmap.Dispose();
        }

        Result<RenderOutcome, RenderError> Fail(string message)
        {
            _diag.Counter("engine.page_load.failed", 1);
            _diag.Log(DiagLevel.Error, "engine", message);
            return Result<RenderOutcome, RenderError>.Err(new RenderError(message));
        }
    }

    /// <summary>
    /// Load <paramref name="url"/>, parse it, style it, and lay it out — but do
    /// not rasterize. Returns the box tree wrapped in a <see cref="LaidOutPage"/>
    /// the caller owns; disposing the page releases fetched image bitmaps and
    /// parsed stylesheets. Interactive shells use this to walk the structure
    /// and emit native views rather than displaying a flat bitmap.
    /// </summary>
    // sharedHttp: optional session-scoped HTTP client. When supplied (the
    // interactive browsing path), the document and all resource fetches reuse it,
    // so warm HTTP/2 connections, pooled keep-alive transports, and the DNS cache
    // survive *across navigations* — revisiting an origin skips the DNS+TCP+TLS
    // round-trips. The caller owns its lifetime; this method neither disposes it
    // nor hands it to the returned page. When null, a throwaway client is created
    // for this page load and bound to the returned LaidOutPage (disposed with it).
    public async Task<Result<LaidOutPage, RenderError>> LayoutPageAsync(
        string url, RenderOptions options, CancellationToken ct = default,
        StarlingHttpClient? sharedHttp = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(options);

        _diag.Counter("engine.page_layout", 1);
        using var _ = _diag.Span("engine", $"layout {url}");
        Activity.Current?.SetTag("http.url", url);
        Activity.Current?.SetTag("viewport.w", options.Viewport.Width);
        Activity.Current?.SetTag("viewport.h", options.Viewport.Height);

        var parsed = UrlParser.Parse(url);
        if (parsed.IsErr)
            return Result<LaidOutPage, RenderError>.Err(new RenderError($"URL parse failed: {parsed.Error}"));

        var u = parsed.Value;

        // One HTTP client (one connection pool / H2 manager / DNS cache) for the
        // whole page load: the document fetch and every resource fetch
        // (css/js/fonts/images) share it, so same-origin requests reuse a live
        // HTTP/2 connection (or keep-alive H1 transport) instead of re-doing
        // DNS+TCP+TLS per resource. A caller-supplied sharedHttp (interactive
        // session) is reused across navigations and is NOT owned here; otherwise
        // we mint a throwaway client, hand it to the returned LaidOutPage, and
        // the finally below disposes it on every path that doesn't hand it over.
        var http = sharedHttp ?? _httpFactory();
        var ownsHttp = sharedHttp is null;
        var httpHandedToPage = false;
        try
        {
            string html;
            Starling.Net.Http.ConnectionSecurity? security = null;
            try
            {
                if (u.IsFile)
                {
                    var path = u.ToFileSystemPath();
                    if (!File.Exists(path))
                        return Result<LaidOutPage, RenderError>.Err(new RenderError($"File not found: {path}"));
                    html = File.ReadAllText(path);
                }
                else if (u.IsHttp || u.IsHttps)
                {
                    Result<(string Html, StarlingUrl FinalUrl, Starling.Net.Http.ConnectionSecurity? Security), RenderError> fetched;
                    fetched = await FetchHtmlAsync(u, http, ct).ConfigureAwait(false);
                    if (fetched.IsErr)
                        return Result<LaidOutPage, RenderError>.Err(fetched.Error);
                    html = fetched.Value.Html;
                    security = fetched.Value.Security;
                    // Use the post-redirect URL as the base for relative resource
                    // resolution. See FetchHtmlAsync docstring.
                    u = fetched.Value.FinalUrl;
                }
                else
                {
                    return Result<LaidOutPage, RenderError>.Err(new RenderError($"Unsupported scheme '{u.Scheme}'."));
                }
            }
            catch (IOException ex)
            {
                return Result<LaidOutPage, RenderError>.Err(new RenderError(ex.Message));
            }

            // Scripting flag ENABLED — the engine executes page JS, so <noscript>
            // contents must parse as inert raw text (WHATWG HTML §13.2.6.4.4).
            var doc = Html.HtmlParser.Parse(html, _diag, scriptingEnabled: true);

            // Page resources outlive this method — the caller's LaidOutPage owns
            // and disposes them (and the shared http client). On any path that
            // doesn't return Ok we dispose here so callers don't have to.
            var images = new ImageFetcher(_diag, http);
            var stylesheets = new StylesheetFetcher(_diag, http);
            var webFonts = new FontFaceRegistry();
            try
            {
                using (var scripts = new ScriptFetcher(_diag, http))
                {
                    // Start external script downloads now, concurrently with the
                    // stylesheet/image fetch below (and the font fetch that
                    // follows). Scripts have no data dependency on stylesheets or
                    // fonts being *downloaded* — only script *execution* must wait
                    // for stylesheets to apply, which is enforced where scripts run
                    // below — so serializing the downloads only inflates the
                    // critical path. FetchAllAsync enumerates the <script> elements
                    // synchronously before its first await, so this reads the DOM
                    // before any concurrent fetch can touch it. The task is awaited
                    // just before scripts execute. (Mirrors RenderAsync.)
                    var scriptsFetch = scripts.FetchAllAsync(doc, baseUrl: u, ct);

                    // Fonts depend on the @font-face rules in the stylesheets, not
                    // on the images — chain the font fetch onto the stylesheet
                    // fetch and run it alongside the image fetch so web fonts
                    // start as soon as the sheets land. (Mirrors RenderAsync.)
                    using var fontFaceFetcher = new FontFaceFetcher(_diag, http);
                    async Task FetchSheetsThenFontsAsync()
                    {
                        await stylesheets.FetchAllAsync(doc, baseUrl: u, ct).ConfigureAwait(false);
                        await fontFaceFetcher
                            .FetchAllAsync(EnumerateAuthorSheets(doc, u, stylesheets), webFonts, ct)
                            .ConfigureAwait(false);
                    }

                    await Task.WhenAll(
                        images.FetchAllAsync(doc, baseUrl: u, options.Viewport.Width, options.FontSize, ct),
                        FetchSheetsThenFontsAsync()
                    ).ConfigureAwait(false);

                    // The script downloads were kicked off above and have been
                    // running concurrently with the stylesheet/font fetch; await
                    // their completion now, before any script executes.
                    await scriptsFetch.ConfigureAwait(false);
                    if (scripts.Scripts.Count > 0 || scripts.ModuleScripts.Count > 0)
                    {
                        var preViewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);
                        // Lazy pre-script layout — see RenderAsync for rationale.
                        (Starling.Layout.Box.BlockBox Root, Starling.Css.Cascade.StyleEngine Style) Relayout()
                        {
                            using (_diag.Span("engine", "prelayout_for_js"))
                                return _painter.LayoutDocumentWithStyle(
                                    doc, preViewport, options.FontSize, images, stylesheets.Resolve, webFonts,
                                    colorScheme: options.PreferredColorScheme);
                        }
                        var layoutHost = new BoxLayoutHost(doc, Relayout);
                        await RunScriptsAsync(doc, u, scripts, layoutHost, ct).ConfigureAwait(false);
                        await Task.WhenAll(
                            images.FetchAllAsync(doc, baseUrl: u, options.Viewport.Width, options.FontSize, ct),
                            stylesheets.FetchAllAsync(doc, baseUrl: u, ct)
                        ).ConfigureAwait(false);
                    }
                }

                await images
                    .FetchBackgroundsAsync(EnumerateAuthorSheets(doc, u, stylesheets), u, ct)
                    .ConfigureAwait(false);

                var viewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);
                var (root, style) = _painter.LayoutDocumentWithStyle(
                    doc, viewport, options.FontSize, images, stylesheets.Resolve, webFonts,
                    colorScheme: options.PreferredColorScheme);

                var title = ExtractTitle(doc);
                // Only hand the client to the page when we own it; a shared
                // session client outlives the page and is disposed by the caller.
                var page = new LaidOutPage(
                    root, doc, style, viewport, url, title, images, stylesheets, webFonts,
                    options.FontSize, security, ownsHttp ? http : null);
                httpHandedToPage = ownsHttp;
                return Result<LaidOutPage, RenderError>.Ok(page);
            }
            catch
            {
                images.Dispose();
                stylesheets.Dispose();
                webFonts.Dispose();
                throw;
            }
        }
        finally
        {
            // Dispose only a client we minted here and didn't hand to the page.
            // A caller-supplied shared client is never disposed here.
            if (ownsHttp && !httpHandedToPage)
                http.Dispose();
        }
    }

    /// <summary>
    /// Re-lays-out an existing <paramref name="page"/> at a new viewport size
    /// (e.g. after a window resize) <em>without</em> re-fetching: the page's
    /// already-parsed <see cref="LaidOutPage.Document"/> and resource resolvers
    /// are reused, only the box tree and cascade are rebuilt against the new
    /// <paramref name="options"/> viewport. Returns a fresh <see cref="LaidOutPage"/>
    /// that owns the shared resources; the caller must show it and dispose the
    /// old one. Synchronous — runs on the caller's thread.
    /// </summary>
    /// <remarks>
    /// This reflows the post-script DOM as it currently stands; it does not
    /// re-run page scripts, fire <c>resize</c> events, or re-evaluate
    /// <c>srcset</c> against the new width. Those need a full navigation.
    /// </remarks>
    public LaidOutPage RelayoutPage(LaidOutPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);

        _diag.Counter("engine.page_relayout", 1);
        using var _ = _diag.Span("engine", $"relayout {page.Url}");
        Activity.Current?.SetTag("viewport.w", options.Viewport.Width);
        Activity.Current?.SetTag("viewport.h", options.Viewport.Height);

        var viewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);
        var (root, style) = _painter.LayoutDocumentWithStyle(
            page.Document, viewport, page.DefaultFontSize, page.Images, page.Stylesheets.Resolve,
            page.WebFonts, colorScheme: options.PreferredColorScheme);
        return page.Relayout(root, style, viewport);
    }

    /// <summary>
    /// Re-paint an already-laid-out page at frame timestamp <paramref name="nowMs"/>.
    /// Ticks the page's animation and transition engines forward, then runs the
    /// painter with the timestamp threaded through the cascade so any in-flight
    /// CSS animations and transitions sample their current value into the
    /// returned bitmap. The page's box tree is rebuilt each call (cheap when
    /// only animated properties changed; the cascade cache short-circuits the
    /// static side), so the same <see cref="LaidOutPage"/> can drive an arbitrary
    /// frame sequence. Callers typically loop calling this once per rAF tick.
    /// </summary>
    public Starling.Common.Image.RenderedBitmap RenderFrame(LaidOutPage page, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.Style.AnimationEngine.Tick(nowMs);
        page.Style.TransitionEngine.Tick(nowMs);

        return _painter.RenderWithStyle(
            page.Document,
            page.Style,
            new LayoutSize(page.Viewport.Width, page.Viewport.Height),
            page.Images,
            page.WebFonts,
            nowMs: (double)nowMs);
    }

    /// <summary>
    /// Install <c>window</c> / <c>document</c> / <c>fetch</c> / observers on a
    /// fresh <see cref="JsRuntime"/>, run every collected script against the
    /// shared <paramref name="document"/>, then drain microtasks and fire
    /// <c>DOMContentLoaded</c> + <c>load</c>. Script errors are logged through
    /// the realm's console sink rather than bubbled — one bad bundle should
    /// not abort the render.
    /// </summary>
    /// <remarks>
    /// First-cut wiring scope: classic scripts only (modules and other
    /// <c>type</c> values are filtered out by <see cref="ScriptFetcher"/>).
    /// <c>requestAnimationFrame</c> / <c>cancelAnimationFrame</c> are
    /// installed and ride the same simulated <see cref="WebEventLoop"/>
    /// clock as the timers, so a page that bootstraps via rAF settles
    /// during <see cref="PumpPendingAsync"/> alongside <c>setTimeout</c>
    /// chains. The interactive shell still drives presentation through
    /// <c>RenderFrame</c>; in the headless renderer the loop is purely
    /// simulated.
    /// <see cref="PumpPendingAsync"/> drives both the JS microtask queue
    /// and the <see cref="WebEventLoop"/> simulated clock so chained
    /// <c>setTimeout</c> bootstrappers settle within a wall-clock budget.
    /// </remarks>
    private async Task RunScriptsAsync(
        Document document, StarlingUrl baseUrl, ScriptFetcher scriptFetcher,
        ILayoutHost? layoutHost, CancellationToken ct)
    {
        var scripts = scriptFetcher.Scripts;
        var runtime = new JsRuntime();
        var consoleErrors = 0;
        var previousSink = runtime.Realm.ConsoleSink;
        runtime.Realm.ConsoleSink = (level, message) =>
        {
            previousSink(level, message);
            var diagLevel = level switch
            {
                ConsoleLevel.Error => DiagLevel.Warn,
                ConsoleLevel.Warn => DiagLevel.Warn,
                _ => DiagLevel.Info,
            };
            _diag.Log(diagLevel, "engine.js", $"[{level}] {message}");
            if (level == ConsoleLevel.Error) consoleErrors++;
        };

        using var http = _httpFactory();
        WindowBinding.Install(runtime, document, new WindowInstallOptions(
            DocumentUrl: baseUrl.ToString(),
            HttpClient: http,
            LayoutHost: layoutHost));

        // setTimeout / setInterval ride on a simulated WebEventLoop clock.
        // PumpPendingAsync advances it in small steps after each microtask
        // drain — that fires due timers, whose callbacks land back on the JS
        // realm's microtask queue for the next pump tick. rAF callbacks
        // ride the same clock; `AdvanceBy` routes through `RunFrame`, so a
        // page that bootstraps via `requestAnimationFrame` instead of
        // `setTimeout` settles on the same pump.
        var loop = new WebEventLoop();
        TimersBinding.Install(runtime, loop);
        AnimationFrameBinding.Install(runtime, loop);

        // Track which <script> elements have already executed so the
        // runtime-injection hook never double-runs a parser-found script and
        // an injected script can't be re-run if it is moved within the tree.
        var executed = new HashSet<Element>(ReferenceEqualityComparer.Instance);

        // Wire the runtime-injection hook: when JS appends a freshly created
        // <script> to the connected DOM, fetch (for src) + execute it through
        // the same compile+run path. Parser-inserted vs script-inserted async
        // defaults: a <script> created via createElement and connected at
        // runtime is "non-parser-inserted", so the spec defaults it to async;
        // the headless engine has no streaming parser to overlap with, so it
        // honours the element's async/defer attributes verbatim and otherwise
        // runs the injected script synchronously on insertion (an acceptable
        // approximation — see browser-plan note in the test).
        document.NodeConnected = node =>
            OnNodeConnected(node, runtime, scriptFetcher, baseUrl, executed, ct);

        // Dynamic <script src=…> path (HTML §4.12.1 "prepare a script"): when a
        // running script sets src on a not-yet-started <script>, queue it for
        // fetch+execute and fire load/error. Deferred-bundle loaders depend on
        // this. The runner shares the ScriptFetcher cache + scheme handling.
        var dynamicRunner = new DynamicScriptRunner(
            _diag, runtime, baseUrl,
            (url, token) => scriptFetcher.FetchSourceAsync(url, token));
        ScriptSrcHook.Register(runtime.Realm, dynamicRunner.OnSrcSet);

        try
        {
            // §0: ordered scripts (neither async nor defer, then defer) run in
            // document order; async scripts run order-independently. We run the
            // ordered batch first, then the async batch — both before
            // DOMContentLoaded. See ScriptFetcher remarks (HTML §4.12.1).
            RunOrderedScripts(runtime, scriptFetcher.Scripts, executed, ct);
            RunAsyncScripts(runtime, scriptFetcher.Scripts, executed, ct);

            // Module scripts (<script type="module">) run after the classic
            // scripts, deferred and in document order (HTML §4.12.1). Each is the
            // entry of its own import graph, loaded + linked + evaluated through
            // the shared ModuleLoader, before DOMContentLoaded.
            RunModuleScripts(runtime, baseUrl, scriptFetcher, ct);

            // Mark every parser-batch script "already started" in the dynamic
            // runner so a deferred loader's later `src` write never re-runs a
            // script that already executed. Empty deferred placeholders (no src,
            // empty body) are not collected into these batches (ScriptFetcher
            // drops them), so they stay eligible for the src-set path above.
            foreach (var s in scriptFetcher.Scripts) dynamicRunner.MarkStarted(s.Element);
            foreach (var s in scriptFetcher.ModuleScripts) dynamicRunner.MarkStarted(s.Element);

            // §1: DOMContentLoaded — synchronous handlers see the parsed DOM. A
            // deferred loader runs here and copies data-deferred-src onto src,
            // which the hook turns into queued dynamic-script work.
            runtime.WithActiveVm(() => WindowBinding.FireDomContentLoaded(runtime));

            // §2: pump in-flight async work (fetch / XHR completions, chained
            // microtasks, simulated timers/rAF) AND src-triggered dynamic script
            // fetches, re-pumping after each settles. Sequential bundle loaders
            // chain off `load`, so this loop is what lets bundle #2..N run after #1.
            await PumpWithDynamicScriptsAsync(runtime, loop, dynamicRunner, ct).ConfigureAwait(false);

            // §3: load event after subresources have settled. Listeners that
            // schedule more work (including more dynamic scripts) get one more
            // drain pass before we return.
            runtime.WithActiveVm(() => WindowBinding.FireLoad(runtime));
            await PumpWithDynamicScriptsAsync(runtime, loop, dynamicRunner, ct).ConfigureAwait(false);
        }
        finally
        {
            // Detach the hooks so the document is inert again once scripting is
            // done — a later layout/paint mutation must not re-enter the VM.
            document.NodeConnected = null;
            ScriptSrcHook.Register(runtime.Realm, null);
        }

        if (consoleErrors > 0)
            _diag.Counter("engine.script.console_errors", consoleErrors);
    }

    /// <summary>Run the non-<c>async</c> classic scripts in document order: the
    /// scripts with neither attribute first, then the <c>defer</c> scripts, both
    /// in source order (HTML §4.12.1). Inline scripts are always
    /// <see cref="ScriptDisposition.None"/>.</summary>
    private void RunOrderedScripts(
        JsRuntime runtime, IReadOnlyList<LoadedScript> scripts, HashSet<Element> executed, CancellationToken ct)
    {
        foreach (var script in scripts)
        {
            if (script.Disposition == ScriptDisposition.None)
                ExecuteScript(runtime, script, executed, ct);
        }
        foreach (var script in scripts)
        {
            if (script.Disposition == ScriptDisposition.Defer)
                ExecuteScript(runtime, script, executed, ct);
        }
    }

    /// <summary>Run the <c>async</c> classic scripts. Order is unspecified
    /// (HTML §4.12.1 "as soon as it is available"); the headless engine has
    /// already fetched them, so it runs them in source order, which is one
    /// permitted ordering.</summary>
    private void RunAsyncScripts(
        JsRuntime runtime, IReadOnlyList<LoadedScript> scripts, HashSet<Element> executed, CancellationToken ct)
    {
        foreach (var script in scripts)
        {
            if (script.Disposition == ScriptDisposition.Async)
                ExecuteScript(runtime, script, executed, ct);
        }
    }

    /// <summary>Compile and run a single classic script against
    /// <paramref name="runtime"/>, deduping on the source <see cref="Element"/>
    /// so a script can never execute twice. Failures are fail-soft (logged via
    /// diagnostics), matching the document-order batch behaviour.</summary>
    private void ExecuteScript(
        JsRuntime runtime, LoadedScript script, HashSet<Element> executed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!executed.Add(script.Element)) return;

        var label = script.IsInline ? "<inline>" : (script.BaseUrl?.ToString() ?? "<unknown>");
        try
        {
            var program = new JsParser(script.Source).ParseProgram();
            var chunk = JsCompiler.Compile(program);
            new JsVm(runtime).Run(chunk);
            _diag.Counter("engine.script.ok", 1);
        }
        catch (JsThrow ex)
        {
            _diag.Counter("engine.script.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js", $"Uncaught script error ({label}): {JsValue.ToStringValue(ex.Value)}");
        }
        catch (Exception ex)
        {
            _diag.Counter("engine.script.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js", $"Script compile/run failure ({label}): {ex.Message}");
        }
    }

    /// <summary>
    /// Tree-mutation hook: when JS connects a node to the document, fetch and
    /// execute any classic <c>&lt;script&gt;</c> it brings in. Runs synchronously
    /// on the insertion (we are inside a VM frame), so the injected script's
    /// side effects are visible to the code that appended it once control
    /// returns. An external <c>src</c> is fetched here; the wait is bounded by
    /// the fetcher's per-URL cache and the engine's overall cancellation token.
    /// </summary>
    private void OnNodeConnected(
        Node node, JsRuntime runtime, ScriptFetcher fetcher, StarlingUrl baseUrl,
        HashSet<Element> executed, CancellationToken ct)
    {
        if (node is not Element { LocalName: "script" } script) return;
        if (executed.Contains(script)) return;

        // Resolve relative src against the document's base URL. Block on the
        // fetch: the headless engine has no streaming parser to overlap with,
        // and the surrounding pump tolerates the synchronous wait.
        LoadedScript? loaded;
        try
        {
            loaded = fetcher.LoadAsync(script, baseUrl, ct).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _diag.Counter("engine.script.failed", 1);
            _diag.Log(DiagLevel.Warn, "engine.js", $"Injected script load failure: {ex.Message}");
            return;
        }

        if (loaded is null) return;
        ExecuteScript(runtime, loaded, executed, ct);
    }

    /// <summary>
    /// Evaluate each <c>&lt;script type="module"&gt;</c> as the entry of its own
    /// ES module graph. One <see cref="ModuleLoader"/> is shared across all
    /// module scripts on the page so a module imported by two different entry
    /// scripts is fetched, linked and evaluated once. Errors are logged through
    /// the console sink (one bad module must not abort the render).
    /// </summary>
    private void RunModuleScripts(
        JsRuntime runtime, StarlingUrl baseUrl, ScriptFetcher scriptFetcher, CancellationToken ct)
    {
        var moduleScripts = scriptFetcher.ModuleScripts;
        if (moduleScripts.Count == 0) return;

        var host = new EngineModuleHost(scriptFetcher, baseUrl, ct);
        var loader = new ModuleLoader(runtime, host);

        // Synthesize one inline-module entry per inline <script type=module>;
        // external module scripts load from their src URL.
        runtime.WithActiveVm(() =>
        {
            foreach (var script in moduleScripts)
            {
                ct.ThrowIfCancellationRequested();
                var label = script.IsInline ? "<inline module>" : (script.BaseUrl?.ToString() ?? "<unknown>");
                try
                {
                    if (script.IsInline)
                    {
                        // Inline modules have no URL of their own; register a
                        // synthetic key whose source is the inline body and
                        // whose import base is the document URL.
                        var key = host.RegisterInlineModule(script.Source);
                        loader.LoadAndEvaluate(key);
                    }
                    else
                    {
                        loader.LoadAndEvaluate(script.BaseUrl!.ToString());
                    }
                    _diag.Counter("engine.module.ok", 1);
                }
                catch (JsThrow ex)
                {
                    _diag.Counter("engine.module.failed", 1);
                    _diag.Log(DiagLevel.Warn, "engine.js",
                        $"Uncaught module error ({label}): {JsValue.ToStringValue(ex.Value)}");
                }
                catch (Exception ex)
                {
                    _diag.Counter("engine.module.failed", 1);
                    _diag.Log(DiagLevel.Warn, "engine.js",
                        $"Module compile/run failure ({label}): {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Drive the realm + simulated loop to quiescence the way
    /// <see cref="PumpPendingAsync"/> does, but also service src-triggered
    /// dynamic scripts: whenever the microtask/timer/rAF queues go quiet, drain
    /// any queued dynamic-script fetches (each can enqueue more work or, via its
    /// <c>load</c> handler, queue the next bundle), then resume pumping. Exits
    /// only when nothing is pending on any of the three fronts — microtasks,
    /// the simulated loop, and the dynamic-script queue — within budget. A
    /// generous wall-clock cap (a few seconds) accommodates sequential network
    /// bundle chains without hanging a stuck page.
    /// </summary>
    private async Task PumpWithDynamicScriptsAsync(
        JsRuntime runtime, WebEventLoop loop, DynamicScriptRunner dynamicRunner, CancellationToken ct)
    {
        const int MaxMs = 8000;          // hard wall-clock cap for the whole settle
        var wall = System.Diagnostics.Stopwatch.StartNew();

        while (wall.ElapsedMilliseconds < MaxMs)
        {
            ct.ThrowIfCancellationRequested();

            // Settle JS-side async work first (microtasks/timers/rAF/fetch).
            // Per-iteration budget is bounded so we cycle back to the dynamic
            // queue promptly; the outer wall clock is the real ceiling.
            await PumpPendingAsync(runtime, loop, ct, idleMs: 60, maxMs: 1500).ConfigureAwait(false);

            if (!dynamicRunner.HasPending)
                return; // fully quiescent — nothing left on any front

            // Fetch + execute the queued dynamic scripts. Each fires load/error;
            // a chained loader's load handler may queue the next script (lands
            // back on HasPending) and/or kick more microtasks (next loop pump).
            await dynamicRunner.DrainAsync(ct).ConfigureAwait(false);
        }

        _diag.Log(DiagLevel.Warn, "engine",
            "Script pump hit the wall-clock cap before quiescence; rendering current DOM.");
    }

    /// <summary>
    /// Drive the JS realm and the host <see cref="WebEventLoop"/> to a quiet
    /// point. Each iteration drains realm microtasks if any are pending,
    /// otherwise advances the simulated clock by <c>SimulatedStepMs</c> so
    /// the next batch of due timers fires (their callbacks may enqueue more
    /// microtasks via <c>WithActiveVm</c>, picked up on the next iteration).
    /// When there's nothing pending and nothing scheduled, we sleep briefly
    /// on wall-clock — that's the slot off-thread fetch / XHR completions
    /// use to enqueue their resolve jobs. Exits when no work has been
    /// observed for <paramref name="idleMs"/> consecutive milliseconds, the
    /// simulated clock hits <paramref name="maxSimulatedMs"/> without
    /// progress, or the wall-clock <paramref name="maxMs"/> budget runs out.
    /// </summary>
    private static async Task PumpPendingAsync(
        JsRuntime runtime,
        WebEventLoop loop,
        CancellationToken ct,
        int idleMs = 100,
        int maxMs = 5000,
        int maxSimulatedMs = 5000)
    {
        const int SimulatedStepMs = 50;
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var idle = System.Diagnostics.Stopwatch.StartNew();
        var simulated = 0;
        while (wall.ElapsedMilliseconds < maxMs)
        {
            ct.ThrowIfCancellationRequested();

            if (runtime.Realm.Microtasks.PendingCount > 0)
            {
                runtime.WithActiveVm(() => { });
                idle.Restart();
                continue;
            }

            if ((loop.PendingTimerCount > 0 || loop.PendingAnimationFrameCount > 0) && simulated < maxSimulatedMs)
            {
                var step = Math.Min(SimulatedStepMs, maxSimulatedMs - simulated);
                // AdvanceBy fires every timer due ≤ new now, then runs one
                // rAF frame at that timestamp. Each callback runs
                // synchronously and drains realm microtasks via
                // WithActiveVm — chained setTimeout(fn, 0)s and
                // requestAnimationFrame(f) loops settle inline.
                loop.AdvanceBy(step);
                simulated += step;
                idle.Restart();
                continue;
            }

            if (idle.ElapsedMilliseconds >= idleMs)
                return;
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Yields every author stylesheet visible on the document — inline
    /// <c>&lt;style&gt;</c> blocks (whose base URL is the document URL) and
    /// every fetched <c>&lt;link rel="stylesheet"&gt;</c> (whose base URL is
    /// the absolute URL it was fetched from). The font-face fetcher needs the
    /// per-sheet base URL so a <c>url("foo.woff2")</c> declared in a remote
    /// CSS resolves against that CSS's origin, not the document's.
    /// </summary>
    private IEnumerable<(StyleSheet Sheet, StarlingUrl? BaseUrl)> EnumerateAuthorSheets(
        Document doc, StarlingUrl? docUrl, StylesheetFetcher stylesheets)
    {
        foreach (var styleElement in doc.GetElementsByTagName("style"))
        {
            var source = styleElement.TextContent;
            if (string.IsNullOrWhiteSpace(source)) continue;
            yield return (CssParser.ParseStyleSheet(source, StyleOrigin.Author, _diag), docUrl);
        }
        foreach (var entry in stylesheets.EnumerateLoaded())
            yield return (entry.Sheet, entry.BaseUrl);
    }

    private static string? ExtractTitle(Document doc)
    {
        foreach (var el in doc.GetElementsByTagName("title"))
        {
            var text = el.TextContent.Trim();
            if (text.Length > 0) return text;
        }
        return null;
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Fetch an HTML page, following redirects. Returns both the body and the
    /// final post-redirect URL — callers need the latter as the base for
    /// resolving relative resource URLs (images, stylesheets, fonts). When the
    /// user types `https://google.com`, the 301 lands on `https://www.google.com/`
    /// and the page's `&lt;img src="/images/..."&gt;` must resolve against
    /// the www host; the original (pre-redirect) URL would 404.
    /// </summary>
    private async Task<Result<(string Html, StarlingUrl FinalUrl, Starling.Net.Http.ConnectionSecurity? Security), RenderError>> FetchHtmlAsync(
        StarlingUrl url, StarlingHttpClient http, CancellationToken ct)
    {
        var current = url;

        for (var redirects = 0; redirects <= MaxRedirects; redirects++)
        {
            NativeCallTrace.Mark("http.get", $"{current} redirect#{redirects}");
            var response = await http.GetAsync(current, ct).ConfigureAwait(false);
            NativeCallTrace.Mark("http.get.done", response.IsErr ? "err" : "ok");
            if (response.IsErr)
                return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Err(new RenderError(
                    $"Network error fetching {current}: {response.Error}"));

            var resp = response.Value;
            if (IsRedirect(resp.StatusCode))
            {
                if (redirects == MaxRedirects)
                    return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Err(new RenderError(
                        $"Too many redirects fetching {url}"));

                var redirected = ResolveRedirect(current, resp);
                if (redirected.IsErr)
                    return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Err(redirected.Error);

                current = redirected.Value;
                continue;
            }

            if (resp.StatusCode is < 200 or >= 400)
                return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Err(new RenderError(
                    $"HTTP {resp.StatusCode} {resp.ReasonPhrase} from {current}"));

            var contentType = resp.Headers.GetFirst("Content-Type");
            var encoding = ResolveEncoding(contentType, resp.Body.Span);
            return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Ok(
                (encoding.GetString(resp.Body.Span), current, resp.Security));
        }

        return Result<(string, StarlingUrl, Starling.Net.Http.ConnectionSecurity?), RenderError>.Err(new RenderError(
            $"Too many redirects fetching {url}"));
    }

    private static bool IsRedirect(int statusCode)
        => statusCode is 301 or 302 or 303 or 307 or 308;

    private static Result<StarlingUrl, RenderError> ResolveRedirect(StarlingUrl current, Net.Http.HttpResponse response)
    {
        var location = response.Headers.GetFirst("Location");
        if (string.IsNullOrWhiteSpace(location))
            return Result<StarlingUrl, RenderError>.Err(new RenderError(
                $"HTTP {response.StatusCode} redirect from {current} did not include a Location header"));

        var redirectUrl = ExpandRedirectLocation(location, current);
        var parsed = UrlParser.Parse(redirectUrl, current);
        if (parsed.IsErr)
            return Result<StarlingUrl, RenderError>.Err(new RenderError(
                $"Redirect Location parse failed from {current}: {parsed.Error}"));

        var next = parsed.Value;
        if (!next.IsHttp && !next.IsHttps)
            return Result<StarlingUrl, RenderError>.Err(new RenderError(
                $"Unsupported redirect scheme '{next.Scheme}' from {current}"));

        return Result<StarlingUrl, RenderError>.Ok(next);
    }

    private static string ExpandRedirectLocation(string location, StarlingUrl current)
    {
        var trimmed = location.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return current.Scheme + ":" + trimmed;

        var authority = current.Host is null
            ? ""
            : current.Port is int port
                ? $"{current.Host}:{port}"
                : current.Host;
        var prefix = $"{current.Scheme}://{authority}";

        if (trimmed.StartsWith("/", StringComparison.Ordinal))
            return prefix + trimmed;

        if (trimmed.StartsWith("?", StringComparison.Ordinal))
            return prefix + current.Path + trimmed;

        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            var query = current.Query is null ? "" : "?" + current.Query;
            return prefix + current.Path + query + trimmed;
        }

        var basePath = current.Path;
        var slash = basePath.LastIndexOf('/');
        basePath = slash >= 0 ? basePath[..(slash + 1)] : "/";
        return prefix + basePath + trimmed;
    }

    /// <summary>
    /// Charset sniff: prefer a recognised BOM, then the HTTP
    /// <c>Content-Type</c>'s <c>charset=</c> parameter, then common HTML
    /// <c>&lt;meta charset&gt;</c> / pragma forms in the first bytes, else UTF-8.
    /// </summary>
    internal static Encoding ResolveEncoding(string? contentType, ReadOnlySpan<byte> body)
    {
        if (body.Length >= 3 && body[0] == 0xEF && body[1] == 0xBB && body[2] == 0xBF)
            return Encoding.UTF8;
        if (body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE)
            return Encoding.Unicode;
        if (body.Length >= 2 && body[0] == 0xFE && body[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        if (contentType is { Length: > 0 })
        {
            var charset = ExtractCharset(contentType);
            if (charset is not null && TryResolveEncoding(charset) is { } httpEncoding)
                return httpEncoding;
        }

        var metaCharset = SniffMetaCharset(body);
        if (metaCharset is not null && TryResolveEncoding(metaCharset) is { } metaEncoding)
            return metaEncoding;

        return Encoding.UTF8;
    }

    private static string? SniffMetaCharset(ReadOnlySpan<byte> body)
    {
        var length = Math.Min(body.Length, 4096);
        if (length == 0) return null;

        var prefix = Encoding.Latin1.GetString(body[..length]);
        var direct = Regex.Match(
            prefix,
            @"<meta\s+[^>]*charset\s*=\s*[""']?\s*([A-Za-z0-9._:-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (direct.Success)
            return direct.Groups[1].Value;

        var pragma = Regex.Match(
            prefix,
            @"<meta\s+[^>]*http-equiv\s*=\s*[""']?\s*content-type[^>]*content\s*=\s*[""'][^""']*charset\s*=\s*([A-Za-z0-9._:-]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return pragma.Success ? pragma.Groups[1].Value : null;
    }

    private static string? ExtractCharset(string headerValue)
    {
        foreach (var raw in headerValue.Split(';'))
        {
            var part = raw.Trim();
            const string prefix = "charset=";
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part[prefix.Length..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static Encoding? TryResolveEncoding(string name)
    {
        // Strip surrounding quotes (we already split charset=... but the
        // header value may itself be quoted).
        var trimmed = name.Trim().Trim('"', '\'');
        if (trimmed.Length == 0) return null;

        // WHATWG Encoding Standard "names and labels" lookup is the single
        // source of truth (src/Starling.Common/Encoding/WhatwgEncodingLabels.cs).
        // The WHATWG canonical "windows-1252" maps the entire ISO-8859-1 /
        // US-ASCII family, matching real-world browser behaviour: bytes
        // 0x80..0x9F are mapped to their windows-1252 glyphs (e.g. 0x92 →
        // U+2019 right single quote) rather than C1 controls.
        var canonical = WhatwgEncodingLabels.TryGetCanonicalName(trimmed);
        if (canonical is null) return null;

        // Hot-path the BCL singletons; fall back to GetEncoding(name) for
        // CodePages-backed encodings (registered in the static ctor).
        return canonical switch
        {
            "UTF-8" => Encoding.UTF8,
            "UTF-16LE" => Encoding.Unicode,
            "UTF-16BE" => Encoding.BigEndianUnicode,
            _ => TryGetEncodingByName(canonical),
        };
    }

    private static Encoding? TryGetEncodingByName(string name)
    {
        try { return Encoding.GetEncoding(name); }
        catch (ArgumentException) { return null; }
    }

    internal static string ExtractDisplayText(Document doc)
    {
        // Prefer the body; fall back to the whole document so single-line input
        // fragments still render.
        var source = (Node?)doc.Body ?? doc;
        var raw = new StringBuilder();
        AppendDisplayText(source, raw);
        return NormalizeDisplayText(raw.ToString());
    }

    private static void AppendDisplayText(Node node, StringBuilder buffer)
    {
        switch (node)
        {
            case Text text:
                buffer.Append(text.Data);
                return;
            case CData cdata:
                buffer.Append(cdata.Data);
                return;
            // These elements are `display: none` in the UA stylesheet so they
            // contribute no rendered text. `noscript` is hidden when scripting
            // is enabled (WHATWG HTML §15.3.1) — the engine always runs JS, so
            // its contents (parsed as inert raw text) must not surface here.
            case Element { LocalName: "script" or "style" or "head" or "noscript" }:
                return;
        }

        var isBlock = node is Element element && IsTextBoundaryElement(element.LocalName);
        if (isBlock && buffer.Length > 0) buffer.Append(' ');
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            AppendDisplayText(child, buffer);
        if (isBlock && buffer.Length > 0) buffer.Append(' ');
    }

    private static bool IsTextBoundaryElement(string localName) => localName.ToLowerInvariant() switch
    {
        "address" or "article" or "aside" or "blockquote" or "body" or "br"
            or "dd" or "details" or "dialog" or "div" or "dl" or "dt"
            or "figcaption" or "figure" or "footer" or "form" or "h1"
            or "h2" or "h3" or "h4" or "h5" or "h6" or "header"
            or "hr" or "li" or "main" or "nav" or "ol" or "p"
            or "pre" or "section" or "summary" or "table" or "tbody"
            or "td" or "tfoot" or "th" or "thead" or "tr" or "ul" => true,
        _ => false,
    };

    private static string NormalizeDisplayText(string raw)
    {
        if (raw.Length == 0) return string.Empty;
        var buf = new StringBuilder(raw.Length);
        var prevWs = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevWs && buf.Length > 0) buf.Append(' ');
                prevWs = true;
            }
            else
            {
                buf.Append(ch);
                prevWs = false;
            }
        }
        return buf.ToString().TrimEnd();
    }
}

/// <summary>Options for a single engine render: viewport and default font size.</summary>
/// <param name="Viewport">The render viewport size in CSS px.</param>
/// <param name="FontSize">The document's default font size in CSS px.</param>
public sealed record RenderOptions(Size Viewport, float FontSize = 32f)
{
    public static RenderOptions Default { get; } = new(new Size(800, 600));

    /// <summary>
    /// The UA's preferred <c>color-scheme</c>, surfaced to the page through
    /// <c>@media (prefers-color-scheme: …)</c>. The interactive shell binds
    /// this to its light/dark theme toggle so sites with dark-mode rules
    /// re-cascade when the user flips the theme.
    /// </summary>
    public Starling.Css.Media.ColorScheme PreferredColorScheme { get; init; } = Starling.Css.Media.ColorScheme.Light;
}

public sealed record RenderOutcome(string OutputPath, int Width, int Height, string DisplayText);

public sealed record RenderError(string Message);
