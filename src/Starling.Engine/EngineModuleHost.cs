using Starling.Js.Modules;
using Starling.Url;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Engine;

/// <summary>
/// <see cref="IModuleHost"/> backed by the engine's existing script-fetch path
/// (<see cref="ScriptFetcher"/>). Resolves bare/relative/absolute specifiers via
/// <see cref="Starling.Url"/> and fetches module bodies through the same
/// file:// + http mechanism classic scripts use, so an ES module import graph
/// resolves through one network/file seam.
/// </summary>
/// <remarks>
/// The <see cref="ModuleLoader"/> contract is synchronous, while the engine's
/// fetch is async. <c>file://</c> resolves to a direct synchronous read; HTTP(S)
/// blocks on the shared fetcher's async path. That trade-off is acceptable here
/// because module evaluation already runs inside the deferred post-parse phase,
/// off the render hot path; a fully-async module pipeline is a follow-up.
/// Inline <c>&lt;script type="module"&gt;</c> bodies have no URL of their own, so
/// they are registered under a synthetic <c>about:inline-N</c> key whose import
/// base is the document URL.
/// </remarks>
internal sealed class EngineModuleHost : IModuleHost
{
    private readonly ScriptFetcher _fetcher;
    private readonly StarlingUrl _documentUrl;
    private readonly CancellationToken _ct;

    /// <summary>Synthetic-key → inline source text, for inline module entries.</summary>
    private readonly Dictionary<string, string> _inlineSources = new(StringComparer.Ordinal);

    /// <summary>Synthetic-key → import base URL (the document URL) so relative
    /// imports inside an inline module resolve correctly.</summary>
    private readonly Dictionary<string, StarlingUrl> _inlineBases = new(StringComparer.Ordinal);

    private int _inlineCounter;

    public EngineModuleHost(ScriptFetcher fetcher, StarlingUrl documentUrl, CancellationToken ct)
    {
        _fetcher = fetcher;
        _documentUrl = documentUrl;
        _ct = ct;
    }

    /// <summary>Register an inline module body and return its synthetic module-map
    /// key. The loader treats the key as an entry-module specifier.</summary>
    public string RegisterInlineModule(string source)
    {
        var key = $"about:inline-{_inlineCounter++}";
        _inlineSources[key] = source;
        _inlineBases[key] = _documentUrl;
        return key;
    }

    public string? Resolve(string specifier, string? referrer)
    {
        // Synthetic inline keys resolve to themselves.
        if (_inlineSources.ContainsKey(specifier)) return specifier;

        // Determine the base: the referrer module's URL, or — when the referrer
        // is an inline module — the document URL, falling back to the document
        // URL for the entry resolution.
        StarlingUrl baseUrl = _documentUrl;
        if (referrer is not null)
        {
            if (_inlineBases.TryGetValue(referrer, out var inlineBase))
                baseUrl = inlineBase;
            else
            {
                var parsedReferrer = UrlParser.Parse(referrer);
                if (parsedReferrer.IsOk) baseUrl = parsedReferrer.Value;
            }
        }

        var parsed = UrlParser.Parse(specifier, baseUrl);
        return parsed.IsOk ? parsed.Value.ToString() : null;
    }

    public string? FetchSource(string resolvedUrl)
    {
        if (_inlineSources.TryGetValue(resolvedUrl, out var inline)) return inline;

        var parsed = UrlParser.Parse(resolvedUrl);
        if (parsed.IsErr) return null;
        var url = parsed.Value;

        // file:// → direct synchronous read; HTTP(S) → block on the shared
        // async fetch path (see remarks on synchronous trade-off).
        return _fetcher.FetchModuleSourceAsync(url, _ct).GetAwaiter().GetResult();
    }
}
