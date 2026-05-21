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
    private readonly List<LoadedScript> _moduleScripts = new();
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

    /// <summary>Module scripts (<c>&lt;script type="module"&gt;</c>) collected in
    /// document order. Per HTML §4.12.1 these execute deferred — after the
    /// document is parsed, in document order — so the engine runs them through a
    /// <c>ModuleLoader</c> after the classic scripts. Inline modules carry their
    /// source text directly; external modules carry their fetched <c>src</c> URL
    /// (the loader fetches the body itself so the import graph is resolved
    /// through one mechanism).</summary>
    public IReadOnlyList<LoadedScript> ModuleScripts => _moduleScripts;

    public async Task FetchAllAsync(Document document, StarlingUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var script in document.GetElementsByTagName("script"))
        {
            ct.ThrowIfCancellationRequested();

            // Module scripts take a separate branch: their src/inline source is
            // collected but evaluated via the ModuleLoader, not the classic VM.
            if (IsModuleScript(script))
            {
                await CollectModuleScriptAsync(script, baseUrl, ct).ConfigureAwait(false);
                continue;
            }

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

    /// <summary>HTML §4.12.1: <c>type="module"</c> (case-insensitively) selects
    /// a module script. Module type takes precedence over the classic-script
    /// MIME check.</summary>
    private static bool IsModuleScript(Element script)
    {
        var type = script.GetAttribute("type");
        return type is not null && type.Trim().Equals("module", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Collect a <c>&lt;script type="module"&gt;</c>. Inline modules
    /// keep their source and the document base URL; external modules record
    /// their resolved <c>src</c> URL so the loader can fetch + parse the import
    /// graph rooted there. Module scripts are deferred (run after parse via the
    /// <c>ModuleLoader</c>), so they carry <see cref="ScriptDisposition.Defer"/>.</summary>
    private async Task CollectModuleScriptAsync(Element script, StarlingUrl? baseUrl, CancellationToken ct)
    {
        var src = script.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            var inline = script.TextContent;
            if (string.IsNullOrWhiteSpace(inline)) return;
            _moduleScripts.Add(new LoadedScript(script, inline, baseUrl, IsInline: true, ScriptDisposition.Defer));
            return;
        }

        var absolute = ResolveAbsolute(src, baseUrl);
        if (absolute is null)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <script type=module src='{src}'>");
            return;
        }

        // Pre-warm the fetch cache so the loader's host can reuse the body. We
        // still record the URL (not the body) so the loader treats it as the
        // entry module and resolves its imports relative to it.
        var source = await FetchAsync(absolute, ct).ConfigureAwait(false);
        if (source is null) return;
        _moduleScripts.Add(new LoadedScript(script, source, absolute, IsInline: false, ScriptDisposition.Defer));
    }

    /// <summary>Fetch a module's source through the engine's shared script-fetch
    /// path (file:// + http), reusing the URL cache. Exposed for the engine's
    /// <c>ModuleLoader</c> host so the whole import graph flows through one
    /// mechanism.</summary>
    public Task<string?> FetchModuleSourceAsync(StarlingUrl url, CancellationToken ct) => FetchAsync(url, ct);

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

    /// <summary>Fetch a script's source for the dynamic (src-set-from-JS) path.
    /// Shares the same scheme handling, decode, and per-URL cache as the static
    /// parser-batch path so a bundle requested both ways is only fetched once.
    /// Exposed for <c>DynamicScriptRunner</c>.</summary>
    public Task<string?> FetchSourceAsync(StarlingUrl url, CancellationToken ct)
        => FetchAsync(url, ct);

    private async Task<string?> FetchAsync(StarlingUrl url, CancellationToken ct)
    {
        var key = url.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached;

        using var _ = _diag.Span("engine", "fetch_script");
        Activity.Current?.SetTag("url", key);

        // data: URLs decode locally — no network. The deferred-bundle repro
        // uses `data:text/javascript,…` so the core behavior is testable
        // offline; real loaders point at http(s) bundles handled below.
        if (url.IsData)
        {
            if (DataUrl.TryDecode(url, out var payload))
            {
                var decoded = Decode(payload.MediaType, payload.Bytes);
                _byUrl[key] = decoded;
                _diag.Counter("engine.fetch.script", 1);
                return decoded;
            }
            _diag.Log(DiagLevel.Warn, "engine", $"Malformed data: script URL");
            _diag.Counter("engine.fetch.script.failed", 1);
            return null;
        }

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
        _moduleScripts.Clear();
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
