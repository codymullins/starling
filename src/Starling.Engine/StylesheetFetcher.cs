using System.Diagnostics;
using System.Text;
using Tessera.Common.Diagnostics;
using Tessera.Common.Encoding;
using Tessera.Css;
using Tessera.Css.Parser;
using Tessera.Dom;
using Tessera.Net;
using Tessera.Url;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

/// <summary>
/// Fetches every <c>&lt;link rel="stylesheet" href="..."&gt;</c> referenced by
/// a <see cref="Document"/>, parses each response as CSS, and exposes the
/// result keyed by <c>&lt;link&gt;</c> element so the painter can drop the
/// parsed sheet into the cascade at the correct document-order position.
/// </summary>
/// <remarks>
/// Fail-soft like <see cref="ImageFetcher"/>: a network/parse failure leaves
/// the <c>&lt;link&gt;</c> with no associated sheet — the cascade proceeds
/// without it. URLs are de-duplicated; the same href is fetched at most once.
///
/// Cross-origin / CSP are not enforced yet (M5+ work). The HTTP charset, BOM,
/// and <c>@charset</c> rules are all best-effort: we honour the
/// <c>Content-Type</c> charset, then a UTF-16 BOM, then the leading
/// <c>@charset</c> rule, else UTF-8 per [CSS Syntax 3 §3.2].
/// </remarks>
internal sealed class StylesheetFetcher : IDisposable
{
    private readonly Dictionary<Element, StyleSheet> _byElement = [];
    private readonly Dictionary<string, (StyleSheet Sheet, TesseraUrl Url)> _byUrl = new(StringComparer.Ordinal);
    private readonly IDiagnostics _diag;
    private readonly Func<TesseraHttpClient> _httpFactory;
    private TesseraHttpClient? _sharedHttp;

    public StylesheetFetcher(IDiagnostics diag, Func<TesseraHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
    }

    public StyleSheet? Resolve(Element element)
        => _byElement.TryGetValue(element, out var sheet) ? sheet : null;

    /// <summary>
    /// Enumerates every loaded sheet paired with the absolute URL it was
    /// fetched from. Subresource fetchers (currently <see cref="FontFaceFetcher"/>)
    /// use this so relative <c>url()</c> values resolve against the sheet's
    /// origin, not the document's.
    /// </summary>
    public IEnumerable<(StyleSheet Sheet, TesseraUrl BaseUrl)> EnumerateLoaded()
    {
        foreach (var entry in _byUrl.Values)
            yield return (entry.Sheet, entry.Url);
    }

    public async Task FetchAllAsync(Document document, TesseraUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var link in document.GetElementsByTagName("link"))
        {
            ct.ThrowIfCancellationRequested();

            if (!IsStylesheetLink(link)) continue;

            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href)) continue;

            var absolute = ResolveAbsolute(href, baseUrl);
            if (absolute is null)
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <link href='{href}'>");
                continue;
            }

            var sheet = await FetchAndParseAsync(absolute, ct).ConfigureAwait(false);
            if (sheet is null) continue;

            _byElement[link] = sheet;
        }
    }

    private static bool IsStylesheetLink(Element link)
    {
        var rel = link.GetAttribute("rel");
        if (string.IsNullOrWhiteSpace(rel)) return false;

        // rel is a space-separated set of tokens per HTML spec; "stylesheet"
        // anywhere in the set counts.
        foreach (var token in rel.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Equals("stylesheet", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async Task<StyleSheet?> FetchAndParseAsync(TesseraUrl url, CancellationToken ct)
    {
        var key = url.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached.Sheet;

        using var _ = _diag.Span("engine", "fetch_stylesheet");
        Activity.Current?.SetTag("url", key);

        try
        {
            byte[] bytes;
            string? contentType = null;
            if (url.IsFile)
            {
                var path = url.ToFileSystemPath();
                if (!File.Exists(path))
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Missing local stylesheet: {path}");
                    _diag.Counter("engine.fetch.stylesheet.failed", 1);
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
                    _diag.Log(DiagLevel.Warn, "engine", $"Stylesheet fetch failed {url}: {response.Error}");
                    _diag.Counter("engine.fetch.stylesheet.failed", 1);
                    return null;
                }
                Activity.Current?.SetTag("http.status_code", response.Value.StatusCode);
                if (response.Value.StatusCode is < 200 or >= 400)
                {
                    _diag.Log(DiagLevel.Warn, "engine",
                        $"Stylesheet fetch HTTP {response.Value.StatusCode} from {url}");
                    _diag.Counter("engine.fetch.stylesheet.failed", 1);
                    return null;
                }
                bytes = response.Value.Body.ToArray();
                contentType = response.Value.Headers.GetFirst("Content-Type");
            }
            else
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Unsupported stylesheet scheme '{url.Scheme}' for {url}");
                _diag.Counter("engine.fetch.stylesheet.failed", 1);
                return null;
            }

            Activity.Current?.SetTag("bytes", bytes.Length);
            var text = DecodeCss(contentType, bytes);
            var sheet = CssParser.ParseStyleSheet(text, StyleOrigin.Author, _diag);
            _byUrl[key] = (sheet, url);
            _diag.Counter("engine.fetch.stylesheet", 1);
            return sheet;
        }
        catch (IOException ex)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Stylesheet read failed {url}: {ex.Message}");
            _diag.Counter("engine.fetch.stylesheet.failed", 1);
            return null;
        }
    }

    private static string DecodeCss(string? contentType, byte[] bytes)
    {
        // Honour BOM first (CSS Syntax 3 §3.2), then HTTP charset, else UTF-8.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        if (contentType is { Length: > 0 })
        {
            var charset = ExtractCharset(contentType);
            if (charset is not null && TryResolveEncoding(charset) is { } encoding)
                return encoding.GetString(bytes);
        }

        return Encoding.UTF8.GetString(bytes);
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
        var canonical = WhatwgEncodingLabels.TryGetCanonicalName(name);
        if (canonical is null) return null;
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

    private static TesseraUrl? ResolveAbsolute(string href, TesseraUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? UrlParser.Parse(href)
            : UrlParser.Parse(href, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }

    public void Dispose()
    {
        _byUrl.Clear();
        _byElement.Clear();
        _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}
