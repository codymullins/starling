using System.Diagnostics;
using Tessera.Codecs;
using Tessera.Common.Diagnostics;
using Tessera.Common.Image;
using Tessera.Dom;
using Tessera.Layout.Tree;
using Tessera.Net;
using Tessera.Url;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

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
    private readonly IDiagnostics _diag;
    private readonly Func<TesseraHttpClient> _httpFactory;
    private TesseraHttpClient? _sharedHttp;

    public ImageFetcher(IDiagnostics diag, Func<TesseraHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
    }

    public bool TryResolve(Element element, out ResolvedImage image)
        => _byElement.TryGetValue(element, out image);

    public async Task FetchAllAsync(Document document, TesseraUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var img in document.GetElementsByTagName("img"))
        {
            ct.ThrowIfCancellationRequested();

            var src = img.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src)) continue;

            var absolute = ResolveAbsolute(src, baseUrl);
            if (absolute is null)
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <img src='{src}'>");
                continue;
            }

            var decoded = await FetchAndDecodeAsync(absolute, ct).ConfigureAwait(false);
            if (decoded is null) continue;

            _byElement[img] = new ResolvedImage(decoded.Width, decoded.Height, decoded);
        }
    }

    private async Task<DecodedImage?> FetchAndDecodeAsync(TesseraUrl url, CancellationToken ct)
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
            // NativeImageDecoder sniffs PNG/JPEG/WebP/GIF/BMP and decodes via
            // the OS-native codec, returning a backend-neutral DecodedImage
            // (straight RGBA8888, top-down, tightly packed) so nothing
            // downstream names a concrete decoder type.
            var decoded = NativeImageDecoder.Decode(bytes);
            Activity.Current?.SetTag("image.w", decoded.Width);
            Activity.Current?.SetTag("image.h", decoded.Height);
            _byUrl[key] = decoded;
            _diag.Counter("engine.fetch.image", 1);
            return decoded;
        }
        catch (Exception ex) when (ex is IOException or ImageDecodeException)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Image decode failed {url}: {ex.Message}");
            _diag.Counter("engine.fetch.image.failed", 1);
            return null;
        }
    }

    private static TesseraUrl? ResolveAbsolute(string src, TesseraUrl? baseUrl)
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
        _byElement.Clear();
        _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}
