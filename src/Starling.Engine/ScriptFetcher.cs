using System.Diagnostics;
using System.Text;
using Starling.Common.Diagnostics;
using Starling.Common.Encoding;
using Starling.Dom;
using Starling.Net;
using Starling.Url;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Engine;

/// <summary>
/// Collects every classic <c>&lt;script&gt;</c> element in <see cref="Document"/>
/// order, resolves its source (inline <c>TextContent</c> or a
/// fetched <c>src</c>), and exposes the result list. The engine's
/// <see cref="StarlingEngine"/> then feeds each entry through a single
/// <c>JsRuntime</c> so DOM mutations from earlier scripts are visible to later
/// ones.
/// </summary>
/// <remarks>
/// Scope-down for first wiring:
/// <list type="bullet">
///   <item>Only classic scripts run. <c>type="module"</c>, <c>importmap</c>,
///   and anything with a non-empty unrecognised <c>type</c> attribute is
///   skipped (HTML §4.12.1 "script type").</item>
///   <item><c>async</c> and <c>defer</c> are honoured per HTML §4.12.1
///   "execute the script element": a <c>defer</c> (no <c>async</c>) script runs
///   after parsing in document order; an <c>async</c> script runs as soon as it
///   is available, order-independent; a script with neither attribute runs in
///   document order. (An external <c>async</c> <i>and</i> <c>defer</c> script is
///   treated as <c>async</c>, matching the spec precedence.)</item>
///   <item>Fetch failures are fail-soft (mirroring <see cref="StylesheetFetcher"/>):
///   the script is dropped with a diag warning and the rest of the run
///   continues.</item>
/// </list>
/// </remarks>
internal sealed class ScriptFetcher : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly Func<StarlingHttpClient> _httpFactory;
    private readonly Dictionary<string, string> _byUrl = new(StringComparer.Ordinal);
    private readonly List<LoadedScript> _scripts = new();
    private StarlingHttpClient? _sharedHttp;

    public ScriptFetcher(IDiagnostics diag, Func<StarlingHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
    }

    /// <summary>Scripts collected in document order. Inline entries carry the
    /// document URL as their base; external entries carry the URL they were
    /// fetched from (so syntax errors can be located precisely).</summary>
    public IReadOnlyList<LoadedScript> Scripts => _scripts;

    public async Task FetchAllAsync(Document document, StarlingUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var script in document.GetElementsByTagName("script"))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsClassicJavascript(script)) continue;

            var loaded = await LoadAsync(script, baseUrl, ct).ConfigureAwait(false);
            if (loaded is not null) _scripts.Add(loaded);
        }
    }

    /// <summary>
    /// Resolve and (for an external <c>src</c>) fetch a single classic
    /// <c>&lt;script&gt;</c> into a <see cref="LoadedScript"/>, or return
    /// <c>null</c> if it is not runnable (non-classic type, empty inline, or a
    /// fetch failure). Shared by the document-order collection pass and the
    /// runtime-injection path so both flow through the same resolve/fetch logic.
    /// </summary>
    public async Task<LoadedScript?> LoadAsync(Element script, StarlingUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!IsClassicJavascript(script)) return null;

        var disposition = ClassifyDisposition(script);
        var src = script.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            var inline = script.TextContent;
            if (string.IsNullOrWhiteSpace(inline)) return null;
            // Inline scripts have no fetch, so async/defer never delay them.
            return new LoadedScript(script, inline, baseUrl, IsInline: true, ScriptDisposition.None);
        }

        var absolute = ResolveAbsolute(src, baseUrl);
        if (absolute is null)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <script src='{src}'>");
            return null;
        }

        var source = await FetchAsync(absolute, ct).ConfigureAwait(false);
        if (source is null) return null;
        return new LoadedScript(script, source, absolute, IsInline: false, disposition);
    }

    /// <summary>HTML §4.12.1: <c>async</c> takes precedence over <c>defer</c>;
    /// an external script with only <c>defer</c> runs after parse in document
    /// order. Both flags are ignored for inline scripts (no fetch to gate on),
    /// which is handled by <see cref="LoadAsync"/>.</summary>
    private static ScriptDisposition ClassifyDisposition(Element script)
    {
        if (script.HasAttribute("async")) return ScriptDisposition.Async;
        if (script.HasAttribute("defer")) return ScriptDisposition.Defer;
        return ScriptDisposition.None;
    }

    /// <summary>HTML §4.12.1: an empty / absent <c>type</c> (or <c>"text/javascript"</c>
    /// case-insensitively, plus the historical aliases) is a classic script.
    /// Anything else — including <c>"module"</c>, MIME types we don't know,
    /// and explicit non-JS like <c>"application/ld+json"</c> — is a non-running
    /// data block. Returns <c>true</c> only for classic JS.</summary>
    private static bool IsClassicJavascript(Element script)
    {
        var type = script.GetAttribute("type");
        if (string.IsNullOrWhiteSpace(type)) return true;
        var trimmed = type.Trim();
        // Strip MIME parameters (e.g. "text/javascript; charset=utf-8").
        var semi = trimmed.IndexOf(';');
        if (semi >= 0) trimmed = trimmed[..semi].Trim();
        return trimmed.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("application/ecmascript", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("text/ecmascript", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("application/x-javascript", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> FetchAsync(StarlingUrl url, CancellationToken ct)
    {
        var key = url.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached;

        using var _ = _diag.Span("engine", "fetch_script");
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
                    _diag.Log(DiagLevel.Warn, "engine", $"Missing local script: {path}");
                    _diag.Counter("engine.fetch.script.failed", 1);
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
                    _diag.Log(DiagLevel.Warn, "engine", $"Script fetch failed {url}: {response.Error}");
                    _diag.Counter("engine.fetch.script.failed", 1);
                    return null;
                }
                Activity.Current?.SetTag("http.status_code", response.Value.StatusCode);
                if (response.Value.StatusCode is < 200 or >= 400)
                {
                    _diag.Log(DiagLevel.Warn, "engine",
                        $"Script fetch HTTP {response.Value.StatusCode} from {url}");
                    _diag.Counter("engine.fetch.script.failed", 1);
                    return null;
                }
                bytes = response.Value.Body.ToArray();
                contentType = response.Value.Headers.GetFirst("Content-Type");
            }
            else
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Unsupported script scheme '{url.Scheme}' for {url}");
                _diag.Counter("engine.fetch.script.failed", 1);
                return null;
            }

            Activity.Current?.SetTag("bytes", bytes.Length);
            var source = Decode(contentType, bytes);
            _byUrl[key] = source;
            _diag.Counter("engine.fetch.script", 1);
            return source;
        }
        catch (IOException ex)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Script read failed {url}: {ex.Message}");
            _diag.Counter("engine.fetch.script.failed", 1);
            return null;
        }
    }

    private static string Decode(string? contentType, byte[] bytes)
    {
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

    private static StarlingUrl? ResolveAbsolute(string href, StarlingUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? UrlParser.Parse(href)
            : UrlParser.Parse(href, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }

    public void Dispose()
    {
        _byUrl.Clear();
        _scripts.Clear();
        _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}

/// <summary>One <c>&lt;script&gt;</c>'s source text plus the URL syntax errors
/// should be reported against, and how its execution is ordered relative to
/// document parsing (<see cref="ScriptDisposition"/>).</summary>
internal sealed record LoadedScript(
    Element Element, string Source, StarlingUrl? BaseUrl, bool IsInline, ScriptDisposition Disposition);

/// <summary>HTML §4.12.1 classic-script execution ordering.</summary>
internal enum ScriptDisposition
{
    /// <summary>Neither <c>async</c> nor <c>defer</c>: runs in document order.</summary>
    None,

    /// <summary><c>async</c>: runs as soon as it is fetched, order-independent.</summary>
    Async,

    /// <summary><c>defer</c> (without <c>async</c>): runs after parsing
    /// completes, in document order, before <c>DOMContentLoaded</c>.</summary>
    Defer,
}
