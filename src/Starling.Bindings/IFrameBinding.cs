using System.Runtime.CompilerServices;
using System.Text;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Html;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Net;
using Starling.Net.Http;
using StarlingUrlParser = global::Starling.Url.UrlParser;

namespace Starling.Bindings;

/// <summary>
/// WPT-07 — minimal HTMLIFrameElement / browsing-context surface. Each
/// <c>&lt;iframe&gt;</c> gets a <see cref="BrowsingContext"/>: a nested
/// <see cref="Document"/>, optionally a nested <see cref="JsRuntime"/> with its
/// own Window bindings. Lifetime is tied to the element via a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> — when the iframe goes away,
/// so does its context.
/// </summary>
/// <remarks>
/// <para>Scope: just what WPT's iframe-driven tests touch.
/// <c>contentDocument</c>/<c>contentWindow</c> work; <c>src</c> fetches +
/// parses + runs scripts in a fresh realm and fires <c>load</c> on the
/// iframe. No same-origin checks, no postMessage, no frame history, no
/// rendering of subframe contents.</para>
/// <para>The loader uses the parent's <see cref="StarlingHttpClient"/> so
/// loopback test servers (WPT corpus) are reachable from inside frames
/// without each frame standing up its own client.</para>
/// </remarks>
public static class IFrameBinding
{
    private static readonly ConditionalWeakTable<Element, BrowsingContext> Contexts = new();
    private static readonly ConditionalWeakTable<JsRealm, ParentEnv> Parents = new();

    /// <summary>Per-iframe nested browsing context. Holds the nested document
    /// (always) and the nested runtime + window object (lazily, on first
    /// <c>contentWindow</c> read or src load).</summary>
    public sealed class BrowsingContext
    {
        public Element Frame { get; }
        public Document Document { get; internal set; }
        public JsRuntime? Runtime { get; internal set; }
        public JsObject? WindowObject { get; internal set; }
        public string DocumentUrl { get; internal set; } = "about:blank";

        internal BrowsingContext(Element frame, Document document)
        {
            Frame = frame;
            Document = document;
        }
    }

    private sealed record ParentEnv(
        StarlingHttpClient? Http,
        string DocumentUrl,
        IDiagnostics Diag);

    /// <summary>Register the parent realm's environment so subframes can
    /// resolve relative URLs and reuse the parent's HTTP client.</summary>
    public static void RegisterParent(
        JsRealm parentRealm, string documentUrl,
        StarlingHttpClient? http, IDiagnostics? diag = null)
    {
        ArgumentNullException.ThrowIfNull(parentRealm);
        var env = new ParentEnv(http, documentUrl ?? "about:blank", diag ?? NoopDiagnostics.Instance);
        Parents.AddOrUpdate(parentRealm, env);
    }

    /// <summary>True when <paramref name="e"/> is a frame element (iframe or
    /// frame).</summary>
    public static bool IsFrameElement(Element e) =>
        e.LocalName.Equals("iframe", StringComparison.OrdinalIgnoreCase)
        || e.LocalName.Equals("frame", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolve or create the browsing context for <paramref name="frame"/>.
    /// The context's document is initialized as a fresh about:blank
    /// (<c>&lt;html&gt;&lt;head&gt;&lt;/head&gt;&lt;body&gt;&lt;/body&gt;&lt;/html&gt;</c>).
    /// </summary>
    public static BrowsingContext EnsureContext(Element frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (Contexts.TryGetValue(frame, out var existing)) return existing;
        var ctx = new BrowsingContext(frame, BuildBlankDocument());
        Contexts.Add(frame, ctx);
        return ctx;
    }

    /// <summary>Get the existing context for <paramref name="frame"/>, or null
    /// if none has been created yet (no contentDocument/contentWindow read,
    /// no src load).</summary>
    public static BrowsingContext? TryGetContext(Element frame)
        => Contexts.TryGetValue(frame, out var c) ? c : null;

    /// <summary>Replace the iframe's document (used by the loader when a
    /// new src lands). Disposes the previous nested runtime, if any.</summary>
    private static void AssignDocument(BrowsingContext ctx, Document doc, string url)
    {
        ctx.Document = doc;
        ctx.DocumentUrl = url;
        ctx.Runtime = null;
        ctx.WindowObject = null;
    }

    /// <summary>Return the JS object that backs <c>iframe.contentWindow</c>.
    /// Lazily creates the nested runtime + Window bindings on first read so
    /// cross-realm property access (<c>iframe.contentWindow.foo = …</c>)
    /// works even for srcless iframes.</summary>
    public static JsObject EnsureContentWindow(JsRealm parentRealm, BrowsingContext ctx)
    {
        if (ctx.WindowObject is not null) return ctx.WindowObject;
        var parent = Parents.TryGetValue(parentRealm, out var p) ? p : new ParentEnv(null, "about:blank", NoopDiagnostics.Instance);
        EnsureRuntime(ctx, parent);
        return ctx.WindowObject!;
    }

    private static void EnsureRuntime(BrowsingContext ctx, ParentEnv parent)
    {
        if (ctx.Runtime is not null && ctx.WindowObject is not null) return;
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, ctx.Document, new WindowInstallOptions(
            DocumentUrl: ctx.DocumentUrl,
            HttpClient: parent.Http));
        ctx.Runtime = runtime;
        ctx.WindowObject = runtime.Global;
        // Propagate the same parent-env onto the child realm so an iframe
        // nested inside an iframe (rare in WPT but the spec allows it)
        // resolves its src and reuses the same HTTP client. The child's own
        // document URL is the resolution base, so callers up the tree don't
        // bleed.
        RegisterParent(runtime.Realm, ctx.DocumentUrl, parent.Http, parent.Diag);
    }

    /// <summary>HTML §iframe — "process the iframe attributes" for <c>src</c>.
    /// Resolves the iframe's src attribute against the parent's document URL,
    /// fetches the resource, parses it into the iframe's nested document,
    /// runs any scripts, then fires <c>load</c> on the iframe element. All
    /// asynchronous — the setter is non-blocking, and the load event lands
    /// on the parent realm's microtask queue when ready.</summary>
    public static void OnSrcSet(JsRealm parentRealm, Element frame)
    {
        ArgumentNullException.ThrowIfNull(parentRealm);
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsFrameElement(frame)) return;
        var src = frame.GetAttribute("src");
        if (string.IsNullOrEmpty(src)) return;
        var parentEnv = Parents.TryGetValue(parentRealm, out var p) ? p : new ParentEnv(null, "about:blank", NoopDiagnostics.Instance);
        var ctx = EnsureContext(frame);
        var resolved = ResolveUrl(parentEnv.DocumentUrl, src);
        var parentRuntime = WindowBinding.RuntimeForRealm(parentRealm);

        // Fetch on a worker; settle through the parent's microtask queue so
        // the load event ordering matches what the test driver expects.
        _ = Task.Run(async () =>
        {
            string? body = null;
            string contentType = "text/html";
            try
            {
                (body, contentType) = await FetchAsync(parentEnv.Http, resolved).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                parentEnv.Diag.LogException("iframe", ex, $"subframe fetch failed: {resolved}");
            }

            void Settle()
            {
                try
                {
                    if (body is not null)
                        LoadIntoFrame(ctx, parentEnv, resolved, body, contentType);
                    FireLoad(parentRealm, frame);
                }
                catch (Exception ex)
                {
                    parentEnv.Diag.LogException("iframe", ex, $"subframe load failed: {resolved}");
                    FireLoad(parentRealm, frame); // still fire so test driver can move on
                }
            }

            if (parentRuntime is { } pr)
                parentRealm.Microtasks.Enqueue(() => pr.WithActiveVm(Settle));
            else
                Settle();
        });
    }

    private static void LoadIntoFrame(
        BrowsingContext ctx, ParentEnv parent, string url, string body, string contentType)
    {
        Document doc;
        var ct = contentType.ToLowerInvariant();
        if (ct.Contains("xml") || ct.Contains("xhtml"))
            doc = ParseXmlIntoDocument(body);
        else
            doc = HtmlParser.Parse(body, parent.Diag, scriptingEnabled: true);
        AssignDocument(ctx, doc, url);
        EnsureRuntime(ctx, parent);
        ExecuteFrameScripts(ctx, parent, url);
    }

    /// <summary>Walk the parsed iframe document for <c>&lt;script&gt;</c>
    /// elements and execute them in document order. External scripts are
    /// fetched synchronously through the parent's HTTP client; inline
    /// scripts run directly. Modules, defer, and async aren't honoured —
    /// the WPT iframe pattern (a couple of inline + external classic
    /// scripts) doesn't need them.</summary>
    private static void ExecuteFrameScripts(BrowsingContext ctx, ParentEnv parent, string frameUrl)
    {
        if (ctx.Runtime is null) return;
        foreach (var node in ctx.Document.Descendants())
        {
            if (node is not Element { LocalName: "script" } sc) continue;
            // Skip script type other than classic JS.
            var type = sc.GetAttribute("type");
            if (!string.IsNullOrEmpty(type)
                && !type.Equals("text/javascript", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("application/javascript", StringComparison.OrdinalIgnoreCase))
                continue;

            string source;
            string label;
            var src = sc.GetAttribute("src");
            if (!string.IsNullOrEmpty(src))
            {
                var resolved = ResolveUrl(frameUrl, src);
                try
                {
                    var (body, _) = FetchAsync(parent.Http, resolved).GetAwaiter().GetResult();
                    source = body;
                    label = resolved;
                }
                catch (Exception ex)
                {
                    parent.Diag.LogException("iframe", ex, $"subframe script fetch failed: {resolved}");
                    continue;
                }
            }
            else
            {
                source = sc.TextContent;
                label = "<inline:" + frameUrl + ">";
            }

            try
            {
                var program = new JsParser(source).ParseProgram();
                var chunk = JsCompiler.Compile(program);
                ctx.Runtime.WithActiveVm(() =>
                {
                    var vm = ctx.Runtime.Realm.ActiveVm!;
                    vm.Run(chunk);
                });
            }
            catch (Exception ex)
            {
                parent.Diag.LogException("iframe", ex, $"subframe script error in {label}");
            }
        }
    }

    private static void FireLoad(JsRealm parentRealm, Element frame)
    {
        // load events do not bubble; deliver to listeners attached on the
        // iframe element (via DOM EventTarget) and to the wrapper's on-load
        // handler if any.
        var ev = new Event("load", new EventInit(Bubbles: false, Cancelable: false));
        try { frame.DispatchEvent(ev); } catch { /* fail-soft */ }

        // on{event} handler attached via `iframe.onload = fn` lives on the
        // JS wrapper, not the host EventTarget. Invoke it explicitly.
        var parentRuntime = WindowBinding.RuntimeForRealm(parentRealm);
        if (parentRuntime is null) return;
        var wrapper = DomWrappers.Wrap(parentRealm, frame);
        var handler = wrapper.Get("onload");
        if (AbstractOperations.IsCallable(handler))
        {
            try
            {
                AbstractOperations.Call(parentRealm.ActiveVm, handler,
                    JsValue.Object(wrapper), Array.Empty<JsValue>());
            }
            catch (JsThrow jt)
            {
                parentRealm.ConsoleSink(ConsoleLevel.Error,
                    $"Uncaught (in iframe onload) {JsValue.ToStringValue(jt.Value)}");
            }
        }
    }

    // ---- URL + fetch helpers ----

    private static string ResolveUrl(string baseUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var abs)) return abs.ToString();
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var bu)
            && Uri.TryCreate(bu, href, out var combined))
            return combined.ToString();
        return href;
    }

    private static async Task<(string Body, string ContentType)> FetchAsync(
        StarlingHttpClient? http, string url)
    {
        // file:// URLs read directly from disk.
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "file")
        {
            var path = u.LocalPath;
            return (await File.ReadAllTextAsync(path).ConfigureAwait(false), GuessTypeFromExt(path));
        }
        if (http is null)
            throw new InvalidOperationException("subframe fetch requires an HTTP client on the parent realm");
        var parsed = StarlingUrlParser.Parse(url);
        if (parsed.IsErr) throw new IOException($"subframe URL parse failed: {parsed.Error}");
        var req = HttpRequest.Get(parsed.Value);
        var res = await http.SendAsync(req, CancellationToken.None).ConfigureAwait(false);
        if (res.IsErr) throw new IOException($"subframe fetch failed: {res.Error}");
        var resp = res.Value;
        var body = Encoding.UTF8.GetString(resp.Body.Span);
        string ct = "text/html";
        foreach (var kv in resp.Headers)
            if (kv.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)) { ct = kv.Value; break; }
        return (body, ct);
    }

    private static string GuessTypeFromExt(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xml" => "application/xml",
        ".xhtml" or ".xht" => "application/xhtml+xml",
        _ => "text/html",
    };

    // ---- Document factories ----

    /// <summary>Build the about:blank skeleton — an HTML document with an
    /// empty head and body. Matches what real browsers expose on a freshly
    /// minted iframe without src.</summary>
    public static Document BuildBlankDocument()
    {
        var doc = new Document { IsHtml = true };
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var body = doc.CreateElement("body");
        html.AppendChild(head);
        html.AppendChild(body);
        doc.AppendChild(html);
        return doc;
    }

    /// <summary>Trivial XML parse for the WPT XML/XHTML iframe pattern.
    /// We don't have an XML parser yet; fall back to the HTML parser, then
    /// flip the document's <see cref="Document.IsHtml"/> off so attribute
    /// case behaviour matches the WHATWG XML branch.</summary>
    private static Document ParseXmlIntoDocument(string body)
    {
        var doc = HtmlParser.Parse(body, null, scriptingEnabled: false);
        doc.IsHtml = false;
        return doc;
    }
}
