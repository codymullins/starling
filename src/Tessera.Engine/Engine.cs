using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Common.Encoding;
using Tessera.Css;
using Tessera.Css.Parser;
using Tessera.Dom;
using Tessera.Net;
using Tessera.Paint;
using Tessera.Url;
using LayoutSize = Tessera.Layout.Size;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

/// <summary>
/// Engine façade. One call: load a URL, parse HTML, run the static
/// style/layout/paint pipeline, and write a bitmap. The full Browser / Page /
/// Frame composition per 01_ARCHITECTURE.md §E lands with interactive browsing.
/// </summary>
/// <remarks>
/// As of the M1 static-rendering closure the renderer uses the document-level
/// pipeline in <see cref="Painter.RenderDocument"/> for file and network inputs.
/// </remarks>
public sealed class TesseraEngine
{
    private const int MaxRedirects = 10;

    private readonly IDiagnostics _diag;
    private readonly Painter _painter;
    private readonly Func<TesseraHttpClient> _httpFactory;

    static TesseraEngine()
    {
        // Register the BCL CodePages provider once so WHATWG legacy
        // single-byte (windows-1250…1258, ISO-8859-2…16, KOI8-*, mac*)
        // and CJK (Shift_JIS, GBK, gb18030, Big5, EUC-KR, …) labels
        // resolve. CodePages is a pure-managed NuGet package.
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TesseraEngine(IDiagnostics? diagnostics = null, Painter? painter = null,
        Func<TesseraHttpClient>? httpFactory = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _painter = painter ?? new Painter(diag: _diag);
        _httpFactory = httpFactory ?? (() => new TesseraHttpClient());
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
                Result<(string Html, TesseraUrl FinalUrl), RenderError> fetched;
                using (_diag.Span("engine", "fetch_html"))
                {
                    fetched = await FetchHtmlAsync(u, ct).ConfigureAwait(false);
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
            doc = Html.HtmlParser.Parse(html);
            Activity.Current?.SetTag("html.bytes", html.Length);
        }
        var displayText = ExtractDisplayText(doc);

        using var images = new ImageFetcher(_diag, _httpFactory);
        using var stylesheets = new StylesheetFetcher(_diag, _httpFactory);
        using (_diag.Span("engine", "fetch_resources"))
        {
            await Task.WhenAll(
                images.FetchAllAsync(doc, baseUrl: u, ct),
                stylesheets.FetchAllAsync(doc, baseUrl: u, ct)
            ).ConfigureAwait(false);
        }

        using var webFonts = new FontFaceRegistry();
        using (var fontFaceFetcher = new FontFaceFetcher(_diag, _httpFactory))
        using (_diag.Span("engine", "fetch_fonts"))
        {
            await fontFaceFetcher
                .FetchAllAsync(EnumerateAuthorSheets(doc, u, stylesheets), webFonts, ct)
                .ConfigureAwait(false);
        }

        Tessera.Common.Image.RenderedBitmap bitmap;
        using (_diag.Span("engine", "render_document"))
        {
            bitmap = _painter.RenderDocument(
                doc,
                new LayoutSize(options.Viewport.Width, options.Viewport.Height),
                options.FontSize,
                images,
                stylesheets.Resolve,
                webFonts);
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
    public async Task<Result<LaidOutPage, RenderError>> LayoutPageAsync(
        string url, RenderOptions options, CancellationToken ct = default)
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
        string html;
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
                var fetched = await FetchHtmlAsync(u, ct).ConfigureAwait(false);
                if (fetched.IsErr)
                    return Result<LaidOutPage, RenderError>.Err(fetched.Error);
                html = fetched.Value.Html;
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

        var doc = Html.HtmlParser.Parse(html);

        // Page resources outlive this method — the caller's LaidOutPage owns
        // and disposes them. On any path that doesn't return Ok we dispose
        // here so callers don't have to.
        var images = new ImageFetcher(_diag, _httpFactory);
        var stylesheets = new StylesheetFetcher(_diag, _httpFactory);
        var webFonts = new FontFaceRegistry();
        try
        {
            await Task.WhenAll(
                images.FetchAllAsync(doc, baseUrl: u, ct),
                stylesheets.FetchAllAsync(doc, baseUrl: u, ct)
            ).ConfigureAwait(false);

            using (var fontFaceFetcher = new FontFaceFetcher(_diag, _httpFactory))
            {
                await fontFaceFetcher
                    .FetchAllAsync(EnumerateAuthorSheets(doc, u, stylesheets), webFonts, ct)
                    .ConfigureAwait(false);
            }

            var viewport = new LayoutSize(options.Viewport.Width, options.Viewport.Height);
            var (root, style) = _painter.LayoutDocumentWithStyle(
                doc, viewport, options.FontSize, images, stylesheets.Resolve, webFonts);

            var title = ExtractTitle(doc);
            return Result<LaidOutPage, RenderError>.Ok(
                new LaidOutPage(root, doc, style, viewport, url, title, images, stylesheets, webFonts));
        }
        catch
        {
            images.Dispose();
            stylesheets.Dispose();
            webFonts.Dispose();
            throw;
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
    private static IEnumerable<(StyleSheet Sheet, TesseraUrl? BaseUrl)> EnumerateAuthorSheets(
        Document doc, TesseraUrl? docUrl, StylesheetFetcher stylesheets)
    {
        foreach (var styleElement in doc.GetElementsByTagName("style"))
        {
            var source = styleElement.TextContent;
            if (string.IsNullOrWhiteSpace(source)) continue;
            yield return (CssParser.ParseStyleSheet(source, StyleOrigin.Author), docUrl);
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
    private async Task<Result<(string Html, TesseraUrl FinalUrl), RenderError>> FetchHtmlAsync(TesseraUrl url, CancellationToken ct)
    {
        using var http = _httpFactory();
        var current = url;

        for (var redirects = 0; redirects <= MaxRedirects; redirects++)
        {
            NativeCallTrace.Mark("http.get", $"{current} redirect#{redirects}");
            var response = await http.GetAsync(current, ct).ConfigureAwait(false);
            NativeCallTrace.Mark("http.get.done", response.IsErr ? "err" : "ok");
            if (response.IsErr)
                return Result<(string, TesseraUrl), RenderError>.Err(new RenderError(
                    $"Network error fetching {current}: {response.Error}"));

            var resp = response.Value;
            if (IsRedirect(resp.StatusCode))
            {
                if (redirects == MaxRedirects)
                    return Result<(string, TesseraUrl), RenderError>.Err(new RenderError(
                        $"Too many redirects fetching {url}"));

                var redirected = ResolveRedirect(current, resp);
                if (redirected.IsErr)
                    return Result<(string, TesseraUrl), RenderError>.Err(redirected.Error);

                current = redirected.Value;
                continue;
            }

            if (resp.StatusCode is < 200 or >= 400)
                return Result<(string, TesseraUrl), RenderError>.Err(new RenderError(
                    $"HTTP {resp.StatusCode} {resp.ReasonPhrase} from {current}"));

            var contentType = resp.Headers.GetFirst("Content-Type");
            var encoding = ResolveEncoding(contentType, resp.Body.Span);
            return Result<(string, TesseraUrl), RenderError>.Ok((encoding.GetString(resp.Body.Span), current));
        }

        return Result<(string, TesseraUrl), RenderError>.Err(new RenderError(
            $"Too many redirects fetching {url}"));
    }

    private static bool IsRedirect(int statusCode)
        => statusCode is 301 or 302 or 303 or 307 or 308;

    private static Result<TesseraUrl, RenderError> ResolveRedirect(TesseraUrl current, Net.Http.HttpResponse response)
    {
        var location = response.Headers.GetFirst("Location");
        if (string.IsNullOrWhiteSpace(location))
            return Result<TesseraUrl, RenderError>.Err(new RenderError(
                $"HTTP {response.StatusCode} redirect from {current} did not include a Location header"));

        var redirectUrl = ExpandRedirectLocation(location, current);
        var parsed = UrlParser.Parse(redirectUrl, current);
        if (parsed.IsErr)
            return Result<TesseraUrl, RenderError>.Err(new RenderError(
                $"Redirect Location parse failed from {current}: {parsed.Error}"));

        var next = parsed.Value;
        if (!next.IsHttp && !next.IsHttps)
            return Result<TesseraUrl, RenderError>.Err(new RenderError(
                $"Unsupported redirect scheme '{next.Scheme}' from {current}"));

        return Result<TesseraUrl, RenderError>.Ok(next);
    }

    private static string ExpandRedirectLocation(string location, TesseraUrl current)
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
        // source of truth (src/Tessera.Common/Encoding/WhatwgEncodingLabels.cs).
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
            case Element { LocalName: "script" or "style" or "head" }:
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
}

public sealed record RenderOutcome(string OutputPath, int Width, int Height, string DisplayText);

public sealed record RenderError(string Message);
