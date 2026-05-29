using System.Globalization;
using Jint;
using Jint.Native;

namespace Starling.Bindings.Jint;

/// <summary>
/// J2d — Window / global surface (Jint backend).
/// Mirrors <c>Starling.Bindings/WindowBinding.cs</c>: rewires the realm's
/// global so its <c>[[Prototype]]</c> is a Window prototype that inherits
/// EventTarget.prototype (so unqualified <c>addEventListener('load', fn)</c>
/// walks up to the EventTarget implementation), binds the global to a host
/// <see cref="InMemoryEventTarget"/> so the JintScriptSession can fire
/// DOMContentLoaded / load on it, and installs <c>window</c>/<c>self</c>/
/// <c>document</c>/<c>location</c>/<c>navigator</c>/<c>innerWidth</c>/
/// <c>innerHeight</c>/<c>devicePixelRatio</c>/<c>scroll*</c> own properties.
/// </summary>
/// <remarks>
/// Like the Starling backend's WindowBinding: location setters log + no-op
/// (cross-document navigation isn't wired through the seam yet);
/// <c>innerWidth</c>/<c>innerHeight</c> default to 0 because the seam carries
/// no viewport hint; <c>getComputedStyle</c> returns an object whose property
/// reads are empty strings until a typed ILayoutHost is plumbed through the
/// seam (today it's <c>object?</c>).
/// </remarks>
internal static class WindowBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;
        var global = engine.Global;
        var doc = ctx.Document;

        if (ctx.Wrappers.WindowPrototype is not null) return; // idempotent

        // 1) WindowPrototype inherits EventTarget.prototype (or, if J2c hasn't
        //    populated that slot yet, falls back to the default object proto).
        var windowProto = new JsObject(engine);
        if (ctx.Wrappers.EventTargetPrototype is { } etp) windowProto.Prototype = etp;
        ctx.Wrappers.WindowPrototype = windowProto;

        // 2) Rewire the realm's global to the window prototype, and bind it to
        //    a host EventTarget so window.addEventListener routes through
        //    EventTargetBinding.ResolveHost via the wrapper registry.
        global.Prototype = windowProto;
        var windowHost = new InMemoryEventTarget();
        ctx.Wrappers.BindExisting(windowHost, global);

        // 3) Window-shaped own properties on the global.
        JintInterop.DefineDataProp(global, "window", global, writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "self", global, writable: true, enumerable: true, configurable: true);
        // No frame tree behind the seam: top/parent are the window itself,
        // frameElement is null, and frames is the window (length 0). Scripts that
        // feature-detect framing (`if (window.top !== window.self)`) take the
        // top-level branch, which is correct for a standalone document.
        JintInterop.DefineDataProp(global, "top", global, writable: false, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "parent", global, writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "frames", global, writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "frameElement", JsValue.Null, writable: false, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "length", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        // globalThis is already provided by Jint per ECMA.

        var docWrapper = ctx.Wrappers.Wrap(doc);
        JintInterop.DefineDataProp(global, "document", docWrapper, writable: true, enumerable: true, configurable: true);

        JintInterop.DefineDataProp(global, "name", new JsString(""), writable: true, enumerable: true, configurable: true);
        // innerWidth/innerHeight reflect the layout viewport supplied by the
        // engine. Real pages branch on these (responsive grids, column-fit math),
        // so 0 — the old default — silently broke content sizing.
        var vw = ctx.ViewportWidth;
        var vh = ctx.ViewportHeight;
        JintInterop.DefineDataProp(global, "innerWidth", new JsNumber(vw), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "innerHeight", new JsNumber(vh), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "outerWidth", new JsNumber(vw), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "outerHeight", new JsNumber(vh), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "screen", BuildScreen(engine, vw, vh), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "devicePixelRatio", new JsNumber(1), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "scrollX", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "scrollY", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "pageXOffset", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "pageYOffset", new JsNumber(0), writable: true, enumerable: true, configurable: true);

        // navigator (data prop carrying a frozen-shape object).
        JintInterop.DefineDataProp(global, "navigator", BuildNavigator(engine), writable: true, enumerable: true, configurable: true);

        // location is an accessor so subsequent history.pushState reads are
        // reflected (HistoryBinding overrides via UrlFor). Shared with
        // document.location so `window.location === document.location` holds.
        var locationObj = LocationObjectFor(ctx);
        JintInterop.DefineAccessor(engine, global, "location",
            (_, _) => locationObj,
            (_, args) =>
            {
                var target = args.Length > 0 ? args[0].ToString() : "";
                if (!HistoryBinding.NavigateSameDocument(ctx, target, replace: false))
                    ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                        $"location assignment ignored (cross-document navigation not yet wired): {target}");
                return JsValue.Undefined;
            });

        // getComputedStyle(element, pseudo?) — returns a CSSStyleDeclaration-
        // shaped object whose getPropertyValue(name) + camel/kebab accessors
        // resolve against the session's layout host (cast from ctx.LayoutHost,
        // stashed as object? by J2d), exactly like the Starling backend. The
        // host resolves values from the pre-script cascade snapshot, triggering
        // the lazy pre-script layout. With no host installed every property
        // reads as the empty string (matches an un-styled / never-laid-out doc).
        JintInterop.DefineMethod(engine, global, "getComputedStyle", (_, args) =>
        {
            var el = args.Length > 0 ? ctx.Wrappers.UnwrapElement(args[0]) : null;
            if (el is null)
                throw new global::Jint.Runtime.JavaScriptException(engine.Intrinsics.TypeError,
                    "getComputedStyle requires an Element argument");
            return BuildComputedStyleDeclaration(ctx, el);
        }, length: 1);

        // matchMedia(query) — CSSOM View §4.2. Returns a MediaQueryList whose
        // `matches` is evaluated against the layout host's media context
        // (viewport size, color scheme). No host → matches is false. We have no
        // resize signal behind the seam, so the list never changes after creation
        // and the listener registration methods are accepted but inert.
        JintInterop.DefineMethod(engine, global, "matchMedia", (_, args) =>
        {
            var query = args.Length > 0 && !args[0].IsUndefined() ? args[0].ToString() : "";
            return BuildMediaQueryList(ctx, query);
        }, length: 1);
    }

    /// <summary>Build a MediaQueryList object for <paramref name="query"/>. The
    /// <c>matches</c> flag is resolved once via the layout host; <c>media</c>
    /// echoes the (normalized-as-given) query. Listener methods are accepted as
    /// no-ops because a static render has no viewport-change events to fire.</summary>
    private static JsObject BuildMediaQueryList(JintBackendContext ctx, string query)
    {
        var engine = ctx.Engine;
        var mql = new JsObject(engine);
        var matches = ctx.LayoutHost?.MatchMedia(query) ?? false;
        JintInterop.DefineAccessor(engine, mql, "matches", (_, _) => JintInterop.Bool(matches));
        JintInterop.DefineAccessor(engine, mql, "media", (_, _) => JintInterop.Str(query));
        JintInterop.DefineDataProp(mql, "onchange", JsValue.Null);
        // Legacy (addListener/removeListener) + modern EventTarget surface. All
        // inert: there is no media-change event source behind the seam.
        foreach (var m in new[] { "addListener", "removeListener", "addEventListener", "removeEventListener" })
            JintInterop.DefineMethod(engine, mql, m, (_, _) => JsValue.Undefined, length: 1);
        JintInterop.DefineMethod(engine, mql, "dispatchEvent", (_, _) => JsBoolean.False, length: 1);
        return mql;
    }

    /// <summary>Build a <c>window.screen</c> object. We have no display device
    /// behind the seam, so screen dimensions track the layout viewport (pages
    /// read <c>screen.availHeight</c>/<c>availWidth</c>/<c>width</c>/<c>height</c>
    /// for sizing). When no viewport was supplied (0), fall back to a common
    /// desktop size so the math stays non-degenerate.</summary>
    private static JsObject BuildScreen(global::Jint.Engine engine, int vw, int vh)
    {
        var w = vw > 0 ? vw : 1280;
        var h = vh > 0 ? vh : 1024;
        var screen = new JsObject(engine);
        JintInterop.DefineAccessor(engine, screen, "width", (_, _) => JintInterop.Num(w));
        JintInterop.DefineAccessor(engine, screen, "height", (_, _) => JintInterop.Num(h));
        JintInterop.DefineAccessor(engine, screen, "availWidth", (_, _) => JintInterop.Num(w));
        JintInterop.DefineAccessor(engine, screen, "availHeight", (_, _) => JintInterop.Num(h));
        JintInterop.DefineAccessor(engine, screen, "availLeft", (_, _) => JintInterop.Num(0));
        JintInterop.DefineAccessor(engine, screen, "availTop", (_, _) => JintInterop.Num(0));
        JintInterop.DefineAccessor(engine, screen, "colorDepth", (_, _) => JintInterop.Num(24));
        JintInterop.DefineAccessor(engine, screen, "pixelDepth", (_, _) => JintInterop.Num(24));
        var orientation = new JsObject(engine);
        JintInterop.DefineAccessor(engine, orientation, "type", (_, _) => JintInterop.Str("landscape-primary"));
        JintInterop.DefineAccessor(engine, orientation, "angle", (_, _) => JintInterop.Num(0));
        JintInterop.DefineDataProp(screen, "orientation", orientation, writable: true, enumerable: true, configurable: true);
        return screen;
    }

    private static JsObject BuildNavigator(global::Jint.Engine engine)
    {
        var nav = new JsObject(engine);
        // Present a mainstream UA so the UA-sniffing libraries real sites ship
        // (browser/OS/version detection) recognise us. Our own honest UA
        // ("StarlingBrowser/0.1") matches no known browser token, so those
        // parsers return undefined for browser/OS and then crash on
        // `result.version.toLowerCase()` — which is exactly what broke
        // McMaster's ContentTransitionManager and left the page blank. We
        // identify as Chrome-on-macOS (the engine we are closest to in
        // behaviour) and append a Starling token, mirroring how Edge/Brave
        // extend the Chrome UA.
        const string ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) "
            + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Starling/0.1";
        JintInterop.DefineAccessor(engine, nav, "userAgent", (_, _) => JintInterop.Str(ua));
        JintInterop.DefineAccessor(engine, nav, "appName", (_, _) => JintInterop.Str("Netscape"));
        JintInterop.DefineAccessor(engine, nav, "appVersion", (_, _) => JintInterop.Str(ua["Mozilla/".Length..]));
        JintInterop.DefineAccessor(engine, nav, "vendor", (_, _) => JintInterop.Str("Google Inc."));
        // platform must agree with the UA's OS token; the previous value
        // (Environment.OSVersion.Platform → "Unix") contradicted it.
        JintInterop.DefineAccessor(engine, nav, "platform", (_, _) => JintInterop.Str("MacIntel"));
        JintInterop.DefineAccessor(engine, nav, "language", (_, _) => JintInterop.Str("en-US"));
        var langs = new global::Jint.Native.JsArray(engine, new JsValue[] { new JsString("en-US"), new JsString("en") });
        JintInterop.DefineAccessor(engine, nav, "languages", (_, _) => langs);
        JintInterop.DefineAccessor(engine, nav, "onLine", (_, _) => JsBoolean.True);
        JintInterop.DefineAccessor(engine, nav, "cookieEnabled", (_, _) => JsBoolean.True);
        JintInterop.DefineMethod(engine, nav, "javaEnabled", (_, _) => JsBoolean.False, length: 0);
        JintInterop.DefineMethod(engine, nav, "sendBeacon", (_, _) => JsBoolean.False, length: 2);
        return nav;
    }

    // One Location object per context, shared between window.location and
    // document.location so identity comparisons hold.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JintBackendContext, JsObject> s_locationCache = new();

    /// <summary>Return the (cached) Location object for this context. Used by
    /// both <c>window.location</c> and <c>document.location</c> so they are the
    /// same object. Mirrors the Starling backend's
    /// <c>WindowBinding.LocationObjectFor</c>.</summary>
    internal static JsObject LocationObjectFor(JintBackendContext ctx)
        => s_locationCache.GetValue(ctx, BuildLocation);

    private static JsObject BuildLocation(JintBackendContext ctx)
    {
        var engine = ctx.Engine;
        var loc = new JsObject(engine);

        JintInterop.DefineAccessor(engine, loc, "href",
            (_, _) => JintInterop.Str(UrlFor(ctx)),
            (_, args) =>
            {
                var target = args.Length > 0 ? args[0].ToString() : "";
                if (!HistoryBinding.NavigateSameDocument(ctx, target, replace: false))
                    ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                        $"location.href assignment ignored (cross-document navigation not yet wired): {target}");
                return JsValue.Undefined;
            });
        JintInterop.DefineAccessor(engine, loc, "protocol", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Scheme + ":")));
        JintInterop.DefineAccessor(engine, loc, "host", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Authority)));
        JintInterop.DefineAccessor(engine, loc, "hostname", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Host)));
        JintInterop.DefineAccessor(engine, loc, "port", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.IsDefaultPort ? "" : p.Port.ToString(CultureInfo.InvariantCulture))));
        JintInterop.DefineAccessor(engine, loc, "pathname", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.AbsolutePath)));
        JintInterop.DefineAccessor(engine, loc, "search", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Query)));
        JintInterop.DefineAccessor(engine, loc, "hash",
            (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Fragment)),
            (_, args) =>
            {
                var raw = args.Length > 0 ? args[0].ToString() : "";
                if (raw.Length > 0 && raw[0] != '#') raw = "#" + raw;
                HistoryBinding.NavigateSameDocument(ctx, raw, replace: false);
                return JsValue.Undefined;
            });
        JintInterop.DefineAccessor(engine, loc, "origin", (_, _) => JintInterop.Str(ParsedPart(ctx, p => $"{p.Scheme}://{p.Authority}")));

        JintInterop.DefineMethod(engine, loc, "toString", (_, _) => JintInterop.Str(UrlFor(ctx)), length: 0);
        JintInterop.DefineMethod(engine, loc, "assign", (_, args) =>
        {
            var target = args.Length > 0 ? args[0].ToString() : "";
            if (!HistoryBinding.NavigateSameDocument(ctx, target, replace: false))
                ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                    $"location.assign ignored (cross-document navigation not yet wired): {target}");
            return JsValue.Undefined;
        }, length: 1);
        JintInterop.DefineMethod(engine, loc, "replace", (_, args) =>
        {
            var target = args.Length > 0 ? args[0].ToString() : "";
            if (!HistoryBinding.NavigateSameDocument(ctx, target, replace: true))
                ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                    $"location.replace ignored (cross-document navigation not yet wired): {target}");
            return JsValue.Undefined;
        }, length: 1);
        JintInterop.DefineMethod(engine, loc, "reload", (_, _) =>
        {
            ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                "location.reload ignored (navigation not yet wired)");
            return JsValue.Undefined;
        }, length: 0);

        return loc;
    }

    /// <summary>Resolve the active document URL. HistoryBinding (also J2d)
    /// publishes its current entry into the context-bound holder; when none
    /// exists this falls back to the session's BaseUrl.</summary>
    internal static string UrlFor(JintBackendContext ctx)
    {
        var hist = HistoryBinding.CurrentUrlFor(ctx);
        if (hist is not null) return hist;
        return ctx.BaseUrl?.ToString() ?? "about:blank";
    }

    private static string ParsedPart(JintBackendContext ctx, Func<Uri, string> select)
    {
        var url = UrlFor(ctx);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        try { return select(uri); } catch { return ""; }
    }

    /// <summary>The session's layout host, or null when none was supplied (bare
    /// unit-test contexts). Strongly typed end-to-end since J7 moved
    /// <see cref="Starling.Bindings.ILayoutHost"/> into the engine-neutral hosting
    /// seam, so this backend no longer references Starling.Bindings.</summary>
    private static Starling.Bindings.ILayoutHost? LayoutHost(JintBackendContext ctx)
        => ctx.LayoutHost;

    private static JsObject BuildComputedStyleDeclaration(JintBackendContext ctx, Starling.Dom.Element element)
    {
        var engine = ctx.Engine;
        var host = LayoutHost(ctx);
        var decl = new JsObject(engine);

        JintInterop.DefineMethod(engine, decl, "getPropertyValue", (_, args) =>
        {
            var name = args.Length > 0 ? args[0].ToString() : "";
            if (string.IsNullOrEmpty(name) || host is null) return JintInterop.Str("");
            return JintInterop.Str(host.GetComputedProperty(element, name));
        }, length: 1);
        // setProperty / removeProperty are no-ops on a computed-style declaration
        // per spec — they only mean something on the element's inline style.
        JintInterop.DefineMethod(engine, decl, "setProperty",
            (_, _) => JsValue.Undefined, length: 2);
        JintInterop.DefineMethod(engine, decl, "removeProperty",
            (_, _) => JintInterop.Str(""), length: 1);

        // Common camel/kebab accessors so `cs.fontSize` / `cs['font-size']`
        // round-trip without going through getPropertyValue (mirrors the
        // Starling backend's CommonComputedStyleProps set).
        foreach (var prop in CommonComputedStyleProps)
        {
            var capturedProp = prop;
            var camel = ToCamelCase(prop);
            JintInterop.DefineAccessor(engine, decl, camel,
                (_, _) => host is null ? JintInterop.Str("") : JintInterop.Str(host.GetComputedProperty(element, capturedProp)));
            if (camel != prop)
                JintInterop.DefineAccessor(engine, decl, prop,
                    (_, _) => host is null ? JintInterop.Str("") : JintInterop.Str(host.GetComputedProperty(element, capturedProp)));
        }
        return decl;
    }

    private static readonly string[] CommonComputedStyleProps =
    [
        "color", "background-color", "font-size", "font-family", "font-weight", "font-style",
        "line-height", "letter-spacing", "text-align", "text-decoration",
        "display", "position", "visibility", "opacity",
        "width", "height", "min-width", "min-height", "max-width", "max-height",
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border-top-width", "border-right-width", "border-bottom-width", "border-left-width",
        "top", "right", "bottom", "left", "z-index",
        "flex-direction", "justify-content", "align-items", "flex-wrap", "gap",
        "overflow", "overflow-x", "overflow-y",
    ];

    private static string ToCamelCase(string kebab)
    {
        if (kebab.IndexOf('-') < 0) return kebab;
        var sb = new System.Text.StringBuilder(kebab.Length);
        var upper = false;
        foreach (var c in kebab)
        {
            if (c == '-') { upper = true; continue; }
            sb.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        return sb.ToString();
    }
}
