using System.Diagnostics;
using Tessera.Common.Diagnostics;
using Tessera.Css.FontFace;
using Tessera.Css.Parser;
using Tessera.Net;
using Tessera.Paint;
using Tessera.Url;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

/// <summary>
/// Walks the document's parsed stylesheets, extracts every
/// <c>@font-face</c> rule, fetches each rule's <c>url()</c> sources, and
/// loads them as typefaces into a <see cref="FontFaceRegistry"/>. The
/// registry is then passed to the painter so the cascade's font-family
/// declarations can resolve to web fonts before falling back to system
/// faces. Fail-soft: a missing or unreadable font drops the source and
/// tries the next entry in the same <c>@font-face</c> block.
/// </summary>
internal sealed class FontFaceFetcher : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly Func<TesseraHttpClient> _httpFactory;
    private readonly Dictionary<string, byte[]> _byUrl = new(StringComparer.Ordinal);
    private TesseraHttpClient? _sharedHttp;

    public FontFaceFetcher(IDiagnostics diag, Func<TesseraHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
    }

    /// <summary>
    /// Resolves every <c>@font-face</c> in <paramref name="sheets"/> and registers
    /// the loaded typefaces under <paramref name="registry"/>. Each sheet entry
    /// carries its own base URL so relative <c>url()</c> values resolve against
    /// the sheet that declared them, not the document (per CSS Cascade §4).
    /// </summary>
    public async Task FetchAllAsync(
        IEnumerable<(StyleSheet Sheet, TesseraUrl? BaseUrl)> sheets,
        FontFaceRegistry registry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var (sheet, baseUrl) in sheets)
        {
            foreach (var rule in FontFaceParser.ParseAll(sheet))
            {
                ct.ThrowIfCancellationRequested();
                await RegisterAsync(rule, baseUrl, registry, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RegisterAsync(
        FontFaceRule rule,
        TesseraUrl? baseUrl,
        FontFaceRegistry registry,
        CancellationToken ct)
    {
        // CSS Fonts 3 §4.3: the user agent tries each entry in `src` in order
        // and uses the first that successfully loads. local() sources are
        // resolved by the system font manager; url() sources go through the
        // document loader.
        foreach (var source in rule.Sources)
        {
            switch (source)
            {
                case LocalFontSource local:
                    if (TryRegisterLocal(rule, local.Name, registry)) return;
                    break;
                case UrlFontSource url:
                    if (!IsLikelyReadableFormat(url.Format)) continue;
                    var bytes = await FetchBytesAsync(url.Url, baseUrl, ct).ConfigureAwait(false);
                    if (bytes is null) continue;
                    if (registry.TryAdd(rule.FamilyName, rule.Bold, rule.Italic, bytes, rule.UnicodeRange))
                    {
                        _diag.Counter("engine.fetch.font", 1);
                        return;
                    }
                    break;
            }
        }

        _diag.Log(DiagLevel.Warn, "engine",
            $"@font-face '{rule.FamilyName}' did not resolve to a usable source.");
    }

    private static bool TryRegisterLocal(FontFaceRule rule, string name, FontFaceRegistry registry)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // local() lookups are best handled by the system font manager at
        // resolve time, not by us loading bytes. So we leave them for the
        // FontResolver fallback chain: any system family named in the
        // cascade's font-family list will be found there anyway. Returning
        // false here lets the next src entry try — typically a url().
        return false;
    }

    private static bool IsLikelyReadableFormat(string? format)
    {
        // We unwrap WOFF / WOFF2 in FontFaceRegistry.TryAdd (Brotli is in
        // System.IO.Compression). WOFF2 files using the glyf/loca transform
        // still fail — declare a TTF/OTF fallback in your src list if your
        // visitor base needs the modern format. SVG fonts are skipped: they
        // are obsolete and Skia doesn't take them.
        if (string.IsNullOrEmpty(format)) return true;
        var f = format.Trim().ToLowerInvariant();
        return f is "truetype" or "opentype" or "ttf" or "otf" or "woff" or "woff2";
    }

    private async Task<byte[]?> FetchBytesAsync(string href, TesseraUrl? baseUrl, CancellationToken ct)
    {
        var absolute = ResolveAbsolute(href, baseUrl);
        if (absolute is null)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve @font-face url('{href}')");
            return null;
        }

        var key = absolute.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached;

        using var _ = _diag.Span("engine", "fetch_font");
        Activity.Current?.SetTag("url", key);

        try
        {
            byte[] bytes;
            if (absolute.IsFile)
            {
                var path = absolute.ToFileSystemPath();
                if (!File.Exists(path))
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Missing local font: {path}");
                    _diag.Counter("engine.fetch.font.failed", 1);
                    return null;
                }
                bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            else if (absolute.IsHttp || absolute.IsHttps)
            {
                _sharedHttp ??= _httpFactory();
                var response = await _sharedHttp.GetAsync(absolute, ct).ConfigureAwait(false);
                if (response.IsErr)
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Font fetch failed {absolute}: {response.Error}");
                    _diag.Counter("engine.fetch.font.failed", 1);
                    return null;
                }
                if (response.Value.StatusCode is < 200 or >= 400)
                {
                    _diag.Log(DiagLevel.Warn, "engine",
                        $"Font fetch HTTP {response.Value.StatusCode} from {absolute}");
                    _diag.Counter("engine.fetch.font.failed", 1);
                    return null;
                }
                bytes = response.Value.Body.ToArray();
            }
            else
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Unsupported font scheme '{absolute.Scheme}'");
                _diag.Counter("engine.fetch.font.failed", 1);
                return null;
            }

            Activity.Current?.SetTag("bytes", bytes.Length);
            _byUrl[key] = bytes;
            return bytes;
        }
        catch (IOException ex)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Font read failed {absolute}: {ex.Message}");
            _diag.Counter("engine.fetch.font.failed", 1);
            return null;
        }
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
        _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}
