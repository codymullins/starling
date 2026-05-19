using System.Runtime.CompilerServices;
using Tessera.Dom;
using Tessera.Dom.Events;
using Tessera.Js.Runtime;
using Tessera.Net;
using Tessera.Net.Http.Cookies;

namespace Tessera.Bindings;

/// <summary>
/// B5-1 — install <c>window</c> / <c>document</c> / <c>location</c> /
/// <c>navigator</c> on a <see cref="JsRuntime"/>. Replaces the
/// <c>DomBindingHost</c> placeholder.
/// </summary>
/// <remarks>
/// <para><b>Window-as-global:</b> the realm's <see cref="JsRealm.GlobalObject"/>
/// is reused as the window object. Its <c>[[Prototype]]</c> is rewired to a
/// fresh <see cref="JsRealm.WindowPrototype"/> that inherits from
/// <see cref="JsRealm.EventTargetPrototype"/>, so an unqualified
/// <c>addEventListener('load', fn)</c> walks the chain to the EventTarget
/// implementation.</para>
/// <para><b>Footprints (simplifications):</b></para>
/// <list type="bullet">
///   <item><c>location</c> setters (assigning to <c>href</c> /
///   <c>location.href = ...</c>) currently log and no-op. Navigation is wired
///   in B5-3+.</item>
///   <item><c>DOMContentLoaded</c> / <c>load</c> are not fired by this code.
///   The host engine calls <see cref="FireDomContentLoaded"/> /
///   <see cref="FireLoad"/> when document loading completes — wiring lives in
///   the layout-engine driver, not here.</item>
///   <item><c>innerWidth</c> / <c>innerHeight</c> default to 0 unless the host
///   supplies a viewport via <see cref="WindowInstallOptions"/>.</item>
/// </list>
/// </remarks>
public static class WindowBinding
{
    // realm → runtime: needed by EventTargetBinding to synthesize VMs when
    // host code dispatches events outside a JS-driven entry.
    private static readonly ConditionalWeakTable<JsRealm, JsRuntime> RealmToRuntime = new();
    // realm → document URL (string). The Document type itself doesn't carry a
    // URL, so the binding stashes it here.
    private static readonly ConditionalWeakTable<Document, DocumentMeta> DocMeta = new();
    // Cached Location object per Document — keeps identity stable.
    private static readonly ConditionalWeakTable<Document, JsObject> LocationCache = new();
    // Track the Window so we can fire load events on it later.
    private static readonly ConditionalWeakTable<JsRealm, Document> RealmToDocument = new();

    /// <summary>Install the full Window / EventTarget / Node / Element /
    /// Document surface on <paramref name="runtime"/>'s realm and bind it to
    /// <paramref name="document"/>. Idempotent per realm.</summary>
    public static void Install(JsRuntime runtime, Document document, WindowInstallOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(document);
        var realm = runtime.Realm;
        RealmToRuntime.AddOrUpdate(realm, runtime);
        RealmToDocument.AddOrUpdate(realm, document);
        DocMeta.AddOrUpdate(document, new DocumentMeta(options.DocumentUrl ?? "about:blank", options));

        // 1) EventTarget + Event + Node/Element/Document prototypes.
        EventTargetBinding.Install(realm);
        NodeBindings.Install(realm);

        // 2) Window prototype: inherits from EventTarget so `addEventListener`
        //    resolves both as `window.addEventListener(...)` and unqualified.
        var windowProto = new JsObject(realm.EventTargetPrototype);
        realm.WindowPrototype = windowProto;

        var global = realm.GlobalObject;
        global.SetPrototypeOf(windowProto);
        // Bind the global to the same host EventTarget so listener bookkeeping
        // is per-realm.
        var hostWindowTarget = new InMemoryEventTarget();
        EventTargetBinding.BindWrapper(global, hostWindowTarget);

        // 3) Window-shaped own properties on the global.
        var docWrapper = DomWrappers.Wrap(realm, document);
        global.DefineOwnProperty("window",
            PropertyDescriptor.Data(JsValue.Object(global), writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("self",
            PropertyDescriptor.Data(JsValue.Object(global), writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("document",
            PropertyDescriptor.Data(JsValue.Object(docWrapper), writable: true, enumerable: true, configurable: true));
        // Force a stable Location object. Stored as a data property so bare
        // `location` (Opcode.LoadGlobal, which goes through the data-only
        // JsObject.Get fast path) sees the object — accessor descriptors on
        // the global return undefined under that opcode. Cross-document
        // navigation via assignment is wired in B5-3+.
        global.DefineOwnProperty("location",
            PropertyDescriptor.Data(JsValue.Object(LocationObjectFor(realm, document)),
                writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(""), writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("navigator",
            PropertyDescriptor.Data(JsValue.Object(BuildNavigator(realm, options.UserAgent)),
                writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("innerWidth",
            PropertyDescriptor.Data(JsValue.Number(options.InnerWidth), writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("innerHeight",
            PropertyDescriptor.Data(JsValue.Number(options.InnerHeight), writable: true, enumerable: true, configurable: true));
        global.DefineOwnProperty("devicePixelRatio",
            PropertyDescriptor.Data(JsValue.Number(1), writable: true, enumerable: true, configurable: true));

        // 4) Window-as-EventTarget convenience: `addEventListener` already
        //    resolves through the prototype chain. Nothing more to do —
        //    `window.addEventListener('load', fn)` and unqualified
        //    `addEventListener('load', fn)` go to the same EventTarget impl.

        // 5) B5-3: optional fetch + XHR install when an HTTP client is supplied.
        if (options.HttpClient is { } httpClient)
        {
            FetchBinding.Install(runtime, httpClient, document);
            XhrBinding.Install(runtime, httpClient, document);
        }

        // 6) B5-5: history, storage, cookies, performance. HistoryBinding
        //    installs first so WindowBinding.UrlFor consults its current entry
        //    when other bindings read the document URL during their own setup.
        var initialUrl = options.DocumentUrl ?? "about:blank";
        HistoryBinding.Install(runtime, document, initialUrl);
        StorageBinding.Install(runtime, document, initialUrl);
        CookieBinding.Install(runtime, document, options.CookieJar);
        PerformanceBinding.Install(runtime);
    }

    /// <summary>Resolve the runtime that backs the given realm. Returns null
    /// if <see cref="Install"/> was never called.</summary>
    internal static JsRuntime? RuntimeForRealm(JsRealm realm)
        => RealmToRuntime.TryGetValue(realm, out var r) ? r : null;

    /// <summary>Build (or return cached) Location object for a document. The
    /// object is identity-stable per document so
    /// <c>location === location</c> holds.</summary>
    internal static JsObject LocationObjectFor(JsRealm realm, Document doc)
    {
        if (LocationCache.TryGetValue(doc, out var existing)) return existing;
        var loc = new JsObject(realm.ObjectPrototype);
        EventTargetBinding.DefineAccessor(realm, loc, "href",
            (_, _) => JsValue.String(UrlFor(realm, doc)),
            (_, args) =>
            {
                var target = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                realm.ConsoleSink(ConsoleLevel.Warn,
                    $"location.href assignment ignored (navigation not yet wired): {target}");
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, loc, "protocol", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.Scheme + ":")));
        EventTargetBinding.DefineAccessor(realm, loc, "host", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.Authority)));
        EventTargetBinding.DefineAccessor(realm, loc, "hostname", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.Host)));
        EventTargetBinding.DefineAccessor(realm, loc, "port", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.IsDefaultPort ? "" : p.Port.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        EventTargetBinding.DefineAccessor(realm, loc, "pathname", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.AbsolutePath)));
        EventTargetBinding.DefineAccessor(realm, loc, "search", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.Query)));
        EventTargetBinding.DefineAccessor(realm, loc, "hash", (_, _) => JsValue.String(ParsedPart(realm, doc, p => p.Fragment)));
        EventTargetBinding.DefineAccessor(realm, loc, "origin", (_, _) => JsValue.String(ParsedPart(realm, doc, p =>
            $"{p.Scheme}://{p.Authority}")));
        EventTargetBinding.DefineMethod(realm, loc, "toString",
            (_, _) => JsValue.String(UrlFor(realm, doc)), length: 0);
        EventTargetBinding.DefineMethod(realm, loc, "assign", (_, args) =>
        {
            var target = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            realm.ConsoleSink(ConsoleLevel.Warn, $"location.assign ignored (navigation not yet wired): {target}");
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, loc, "replace", (_, args) =>
        {
            var target = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            realm.ConsoleSink(ConsoleLevel.Warn, $"location.replace ignored (navigation not yet wired): {target}");
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, loc, "reload",
            (_, _) => { realm.ConsoleSink(ConsoleLevel.Warn, "location.reload ignored (navigation not yet wired)"); return JsValue.Undefined; },
            length: 0);
        LocationCache.Add(doc, loc);
        return loc;
    }

    /// <summary>Resolve the URL string assigned to a document on install.
    /// Defaults to <c>about:blank</c> when none was specified. When the realm
    /// has a session history installed, its current entry takes precedence so
    /// <c>history.pushState</c> is reflected in <c>location.href</c>.</summary>
    internal static string UrlFor(JsRealm realm, Document doc)
    {
        if (HistoryBinding.HistoryForRealm(realm) is { } hist) return hist.CurrentUrl;
        return DocMeta.TryGetValue(doc, out var m) ? m.Url : "about:blank";
    }

    /// <summary>Get the document's base URL (without falling back to
    /// about:blank). Returns null when the document was installed without one;
    /// the fetch binding uses this for relative-URL resolution.</summary>
    public static string? UrlForDocumentOrNull(Document doc)
        => DocMeta.TryGetValue(doc, out var m) ? m.Url : null;

    private static string ParsedPart(JsRealm realm, Document doc, Func<Uri, string> select)
    {
        var url = UrlFor(realm, doc);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        try { return select(uri); } catch { return ""; }
    }

    private static JsObject BuildNavigator(JsRealm realm, string? userAgent)
    {
        var nav = new JsObject(realm.ObjectPrototype);
        var ua = userAgent ?? "Mozilla/5.0 (Starling) StarlingBrowser/0.1";
        EventTargetBinding.DefineAccessor(realm, nav, "userAgent", (_, _) => JsValue.String(ua));
        EventTargetBinding.DefineAccessor(realm, nav, "appName", (_, _) => JsValue.String("Netscape"));
        EventTargetBinding.DefineAccessor(realm, nav, "appVersion", (_, _) => JsValue.String("5.0 (Starling)"));
        EventTargetBinding.DefineAccessor(realm, nav, "platform", (_, _) => JsValue.String(Environment.OSVersion.Platform.ToString()));
        EventTargetBinding.DefineAccessor(realm, nav, "language", (_, _) => JsValue.String("en-US"));
        EventTargetBinding.DefineAccessor(realm, nav, "onLine", (_, _) => JsValue.True);
        return nav;
    }

    /// <summary>TODO callable for the engine: dispatches <c>DOMContentLoaded</c>
    /// on the document and bubbles to window. Wire this from the layout engine
    /// when DOM parsing completes.</summary>
    public static void FireDomContentLoaded(JsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!RealmToDocument.TryGetValue(runtime.Realm, out var doc)) return;
        var ev = new Event("DOMContentLoaded", new EventInit(Bubbles: true, Cancelable: false));
        doc.DispatchEvent(ev);
    }

    /// <summary>TODO callable for the engine: dispatches <c>load</c> on the
    /// window. Wire from the layout engine when all subresources have loaded.</summary>
    public static void FireLoad(JsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        // Dispatch on the host target backing the global window.
        var hostTarget = EventTargetBinding.ResolveHost(JsValue.Object(runtime.Realm.GlobalObject));
        if (hostTarget is null) return;
        var ev = new Event("load");
        hostTarget.DispatchEvent(ev);
    }

    private sealed record DocumentMeta(string Url, WindowInstallOptions Options);
}

/// <summary>Optional knobs for <see cref="WindowBinding.Install"/>.</summary>
public readonly record struct WindowInstallOptions(
    string? DocumentUrl = null,
    string? UserAgent = null,
    double InnerWidth = 0,
    double InnerHeight = 0,
    TesseraHttpClient? HttpClient = null,
    CookieJar? CookieJar = null);

internal static class ConditionalWeakTableExtensions
{
    public static void AddOrUpdate<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
        where TKey : class where TValue : class
    {
        if (table.TryGetValue(key, out _)) table.Remove(key);
        table.Add(key, value);
    }
}
