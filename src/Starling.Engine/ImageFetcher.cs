using System.Diagnostics;
using Starling.Codecs;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Dom;
using Starling.Layout.Tree;
using Starling.Net;
using Starling.Paint.Svg;
using Starling.Url;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Engine;

/// <summary>
/// Resolves every <c>&lt;img src&gt;</c> in a <see cref="Document"/> to a
/// backend-neutral <see cref="DecodedImage"/> and exposes the results as an
/// <see cref="IImageResolver"/> for layout to query. Owns the decoded
/// images and disposes them when the fetcher itself is disposed.
/// </summary>
/// <remarks>
/// The actual decode goes through <see cref="NativeImageDecoder"/> — the
/// OS-native codec seam (ImageIO / WIC / libpng+libjpeg+libwebp) — which hands
/// back a backend-neutral <see cref="DecodedImage"/> so nothing downstream
/// names a concrete decoder type.
/// <para>
/// Caches per absolute URL so an image referenced N times decodes once. The
/// fetcher is fail-soft: a network or decode failure leaves the element
/// without a resolved image, which BoxTreeBuilder degrades to its
/// <c>alt</c> text.
/// </para>
/// </remarks>
internal sealed class ImageFetcher : IImageResolver, IDisposable
{
    private readonly Dictionary<Element, ResolvedImage> _byElement = [];
    private readonly Dictionary<string, DecodedImage> _byUrl = new(StringComparer.Ordinal);
    // Inline-<svg> rasters are decoded on demand (during layout) and aren't
    // keyed by URL, so track them separately for disposal.
    private readonly List<DecodedImage> _inlineSvg = [];
    private readonly IDiagnostics _diag;
    private readonly Func<StarlingHttpClient> _httpFactory;
    private StarlingHttpClient? _sharedHttp;
    private readonly bool _ownsHttp;

    public ImageFetcher(IDiagnostics diag, Func<StarlingHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
        _ownsHttp = true;
    }

    /// <summary>
    /// Use a caller-owned <see cref="StarlingHttpClient"/> so resource fetches
    /// share one connection pool — same-origin requests reuse the keep-alive
    /// transport instead of paying a fresh DNS+TCP+TLS handshake each time. The
    /// shared client is owned by the caller and is not disposed by this fetcher.
    /// </summary>
    public ImageFetcher(IDiagnostics diag, StarlingHttpClient sharedHttp)
    {
        _diag = diag;
        _sharedHttp = sharedHttp;
        _httpFactory = () => sharedHttp;
        _ownsHttp = false;
    }

    /// <summary>
    /// Total number of resolved images currently cached — both element-keyed
    /// (<c>&lt;img&gt;</c> intrinsic sizes) and URL-keyed (CSS
    /// <c>background-image</c> / prefetched url()). The engine snapshots this
    /// before and after the post-script resource fetch to decide whether a new
    /// layout-affecting image arrived (a late intrinsic size changes layout); if
    /// the count is unchanged it can safely reuse the pre-script layout.
    /// </summary>
    public int LoadedCount => _byElement.Count + _byUrl.Count;

    public bool TryResolve(Element element, out ResolvedImage image)
        => _byElement.TryGetValue(element, out image);

    /// <summary>
    /// Rasterize an inline <c>&lt;svg&gt;</c> on demand: serialize the subtree
    /// to an SVG document and run the managed rasterizer, resolving
    /// <c>currentColor</c> against the element's computed color. Cached per
    /// element so repeated layout passes decode once. Fail-soft — a malformed or
    /// empty SVG returns false and layout falls back to the accessible name.
    /// </summary>
    public bool TryResolveInlineSvg(Element svg, Starling.Css.Values.CssColor currentColor, out ResolvedImage image)
    {
        if (_byElement.TryGetValue(svg, out image)) return true;
        try
        {
            var doc = InlineSvgSerializer.Serialize(svg);
            var srgb = currentColor.ToSrgb();
            var color = SixLabors.ImageSharp.Color.FromPixel(
                new SixLabors.ImageSharp.PixelFormats.Rgba32(srgb.R, srgb.G, srgb.B, srgb.A));
            var decoded = SvgImageDecoder.DecodeText(doc, color);
            image = new ResolvedImage(decoded.Width, decoded.Height, decoded);
            _byElement[svg] = image;
            _inlineSvg.Add(decoded);
            _diag.Counter("engine.inline_svg", 1);
            return true;
        }
        catch (Exception ex) when (ex is SvgDecodeException or System.Xml.XmlException)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Inline <svg> decode failed: {ex.Message}");
            _diag.Counter("engine.inline_svg.failed", 1);
            image = default;
            return false;
        }
    }

    public bool TryResolveUrl(string url, out DecodedImage image)
    {
        // The fetcher's prefetch pass keys the cache by absolute URL string;
        // the paint pipeline calls this with the verbatim CSS url() value, so
        // we look up by the same key the resolver code stored.
        if (_byUrl.TryGetValue(url, out var cached))
        {
            image = cached;
            return true;
        }
        image = null!;
        return false;
    }

    public async Task FetchAllAsync(Document document, StarlingUrl? baseUrl, CancellationToken ct)
        => await FetchAllAsync(document, baseUrl, viewportWidthCssPx: 0, fontSizeCssPx: 16, ct).ConfigureAwait(false);

    /// <summary>
    /// Fetch every <c>&lt;img&gt;</c> in the document. When an element has
    /// <c>srcset</c> (and optionally <c>sizes</c>), pick the best candidate
    /// for the supplied viewport and record the
    /// <see href="https://html.spec.whatwg.org/multipage/images.html#density-corrected-intrinsic-width-and-height">
    /// density-corrected intrinsic dimensions</see> so layout sizes the image
    /// against the sizes-derived CSS width instead of the source bitmap's
    /// pixel dimensions.
    /// </summary>
    public async Task FetchAllAsync(
        Document document, StarlingUrl? baseUrl,
        double viewportWidthCssPx, double fontSizeCssPx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var img in document.GetElementsByTagName("img"))
        {
            ct.ThrowIfCancellationRequested();

            var src = img.GetAttribute("src");
            var srcset = img.GetAttribute("srcset");
            var sizes = img.GetAttribute("sizes");

            var (selectedUrl, correctedW, correctedH) = Srcset.Select(
                srcset, sizes, src, viewportWidthCssPx, fontSizeCssPx);

            if (string.IsNullOrWhiteSpace(selectedUrl)) continue;

            var absolute = ResolveAbsolute(selectedUrl, baseUrl);
            if (absolute is null)
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <img src='{selectedUrl}'>");
                continue;
            }

            var decoded = await FetchAndDecodeAsync(absolute, ct).ConfigureAwait(false);
            if (decoded is null) continue;

            // density-corrected intrinsic: if sizes gave us a CSS-px width,
            // scale height proportionally to preserve the source aspect
            // ratio. Otherwise fall back to source pixel dims.
            double intrinsicW, intrinsicH;
            if (correctedW > 0 && decoded.Width > 0 && decoded.Height > 0)
            {
                intrinsicW = correctedW;
                intrinsicH = correctedH > 0
                    ? correctedH
                    : correctedW * decoded.Height / decoded.Width;
            }
            else
            {
                intrinsicW = decoded.Width;
                intrinsicH = decoded.Height;
            }
            _byElement[img] = new ResolvedImage(intrinsicW, intrinsicH, decoded);
        }
    }

    /// <summary>
    /// Walk a parsed stylesheet for <c>url(...)</c> references in
    /// background-image declarations and prefetch each one so paint can resolve
    /// the URL synchronously when emitting display items. The CSS url() value
    /// is also indexed verbatim so the paint pipeline's lookup matches without
    /// the caller needing to re-resolve URLs.
    /// </summary>
    public async Task FetchBackgroundsAsync(IEnumerable<(Starling.Css.Parser.StyleSheet Sheet, StarlingUrl? BaseUrl)> stylesheets, StarlingUrl? documentBaseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stylesheets);

        // (raw url, base url used to resolve relative references). External
        // stylesheets carry their own base URL so relative paths inside them
        // resolve against the sheet, not the document.
        var rawByBase = new Dictionary<string, StarlingUrl?>(StringComparer.Ordinal);
        foreach (var (sheet, sheetBase) in stylesheets)
        {
            var perSheet = new HashSet<string>(StringComparer.Ordinal);
            CollectBackgroundUrls(sheet, perSheet);
            foreach (var raw in perSheet)
                rawByBase.TryAdd(raw, sheetBase ?? documentBaseUrl);
        }

        foreach (var (raw, baseUrl) in rawByBase)
        {
            ct.ThrowIfCancellationRequested();
            if (_byUrl.ContainsKey(raw)) continue;

            var absolute = ResolveAbsolute(raw, baseUrl);
            if (absolute is null) continue;
            var decoded = await FetchAndDecodeAsync(absolute, ct).ConfigureAwait(false);
            if (decoded is null) continue;
            // Index under the *raw* CSS url() string so the paint-time lookup,
            // which only sees the CSS value, finds the prefetched bitmap.
            _byUrl[raw] = decoded;
        }
    }

    private static void CollectBackgroundUrls(Starling.Css.Parser.StyleSheet sheet, HashSet<string> urls)
    {
        foreach (var rule in sheet.Rules)
            CollectFromRule(rule, urls);
    }

    private static void CollectFromRule(Starling.Css.Parser.CssRule rule, HashSet<string> urls)
    {
        if (rule is Starling.Css.Parser.StyleRule sr)
        {
            foreach (var decl in sr.Declarations)
            {
                if (!decl.Name.Contains("background", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var v in Starling.Css.Values.CssValueParser.ParseList(decl.Value))
                    if (v is Starling.Css.Values.CssUrl u && !string.IsNullOrEmpty(u.Value))
                        urls.Add(u.Value);
            }
        }
        else if (rule is Starling.Css.Parser.AtRule at)
        {
            // Walk inner rules (e.g. @media wraps StyleRule children) and any
            // declarations carried directly on the at-rule (rare, but harmless).
            foreach (var inner in at.Rules)
                CollectFromRule(inner, urls);
            foreach (var decl in at.Declarations)
            {
                if (!decl.Name.Contains("background", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var v in Starling.Css.Values.CssValueParser.ParseList(decl.Value))
                    if (v is Starling.Css.Values.CssUrl u && !string.IsNullOrEmpty(u.Value))
                        urls.Add(u.Value);
            }
        }
    }

    private async Task<DecodedImage?> FetchAndDecodeAsync(StarlingUrl url, CancellationToken ct)
    {
        var key = url.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached;

        using var _ = _diag.Span("engine", "fetch_image");
        Activity.Current?.SetTag("url", key);

        try
        {
            byte[] bytes;
            if (url.IsFile)
            {
                var path = url.ToFileSystemPath();
                if (!File.Exists(path))
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Missing local image: {path}");
                    _diag.Counter("engine.fetch.image.failed", 1);
                    return null;
                }
                bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            else if (url.IsHttp || url.IsHttps)
            {
                _sharedHttp ??= _httpFactory();
                var response = await _sharedHttp.GetAsync(url, ct).ConfigureAwait(false);
                if (response.IsErr)
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Image fetch failed {url}: {response.Error}");
                    _diag.Counter("engine.fetch.image.failed", 1);
                    return null;
                }
                Activity.Current?.SetTag("http.status_code", response.Value.StatusCode);
                if (response.Value.StatusCode is < 200 or >= 400)
                {
                    _diag.Log(DiagLevel.Warn, "engine",
                        $"Image fetch HTTP {response.Value.StatusCode} from {url}");
                    _diag.Counter("engine.fetch.image.failed", 1);
                    return null;
                }
                bytes = response.Value.Body.ToArray();
            }
            else if (url.IsData)
            {
                // RFC 2397 data: URLs are decoded locally — never hit the
                // network. Image-bearing data URIs are very common (Google,
                // GitHub, many SPAs inline icons), so without this branch
                // every such <img> degrades to its alt-text fallback.
                if (!DataUrl.TryDecode(url, out var payload))
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Malformed data: URL for image");
                    _diag.Counter("engine.fetch.image.failed", 1);
                    return null;
                }
                bytes = payload.Bytes;
            }
            else
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Unsupported image scheme '{url.Scheme}' for {url}");
                _diag.Counter("engine.fetch.image.failed", 1);
                return null;
            }

            Activity.Current?.SetTag("bytes", bytes.Length);
            // SVG is a vector (XML) format the OS-native raster codecs cannot
            // touch. Sniff it first and route to the pure-managed rasterizer in
            // Starling.Paint (ImageSharp.Drawing); everything else goes through
            // NativeImageDecoder, which sniffs PNG/JPEG/WebP/GIF/BMP and decodes
            // via the OS-native codec. Both return a backend-neutral DecodedImage
            // (straight RGBA8888, top-down, tightly packed) so nothing
            // downstream names a concrete decoder type.
            DecodedImage decoded = NativeImageDecoder.IsSvg(bytes)
                ? SvgImageDecoder.Decode(bytes)
                : NativeImageDecoder.Decode(bytes);
            Activity.Current?.SetTag("image.w", decoded.Width);
            Activity.Current?.SetTag("image.h", decoded.Height);
            _byUrl[key] = decoded;
            _diag.Counter("engine.fetch.image", 1);
            return decoded;
        }
        catch (Exception ex) when (ex is IOException or ImageDecodeException or SvgDecodeException)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Image decode failed {url}: {ex.Message}");
            _diag.Counter("engine.fetch.image.failed", 1);
            return null;
        }
    }

    private static StarlingUrl? ResolveAbsolute(string src, StarlingUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? UrlParser.Parse(src)
            : UrlParser.Parse(src, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }

    public void Dispose()
    {
        foreach (var decoded in _byUrl.Values)
            decoded.Dispose();
        _byUrl.Clear();
        foreach (var decoded in _inlineSvg)
            decoded.Dispose();
        _inlineSvg.Clear();
        _byElement.Clear();
        if (_ownsHttp) _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}
