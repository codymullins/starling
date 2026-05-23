using System.Globalization;
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
        // globalThis is already provided by Jint per ECMA.

        var docWrapper = ctx.Wrappers.Wrap(doc);
        JintInterop.DefineDataProp(global, "document", docWrapper, writable: true, enumerable: true, configurable: true);

        JintInterop.DefineDataProp(global, "name", new JsString(""), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "innerWidth", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "innerHeight", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "devicePixelRatio", new JsNumber(1), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "scrollX", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "scrollY", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "pageXOffset", new JsNumber(0), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(global, "pageYOffset", new JsNumber(0), writable: true, enumerable: true, configurable: true);

        // navigator (data prop carrying a frozen-shape object).
        JintInterop.DefineDataProp(global, "navigator", BuildNavigator(engine), writable: true, enumerable: true, configurable: true);

        // location is an accessor so subsequent history.pushState reads are
        // reflected (HistoryBinding overrides via UrlFor).
        var locationObj = BuildLocation(ctx);
        JintInterop.DefineAccessor(engine, global, "location",
            (_, _) => locationObj,
            (_, args) =>
            {
                var target = args.Length > 0 ? args[0].ToString() : "";
                ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                    $"location assignment ignored (navigation not yet wired): {target}");
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
    }

    private static JsObject BuildNavigator(global::Jint.Engine engine)
    {
        var nav = new JsObject(engine);
        const string ua = "Mozilla/5.0 (Starling) StarlingBrowser/0.1";
        JintInterop.DefineAccessor(engine, nav, "userAgent", (_, _) => JintInterop.Str(ua));
        JintInterop.DefineAccessor(engine, nav, "appName", (_, _) => JintInterop.Str("Netscape"));
        JintInterop.DefineAccessor(engine, nav, "appVersion", (_, _) => JintInterop.Str("5.0 (Starling)"));
        JintInterop.DefineAccessor(engine, nav, "platform", (_, _) => JintInterop.Str(Environment.OSVersion.Platform.ToString()));
        JintInterop.DefineAccessor(engine, nav, "language", (_, _) => JintInterop.Str("en-US"));
        var langs = new global::Jint.Native.JsArray(engine, new JsValue[] { new JsString("en-US"), new JsString("en") });
        JintInterop.DefineAccessor(engine, nav, "languages", (_, _) => langs);
        JintInterop.DefineAccessor(engine, nav, "onLine", (_, _) => JsBoolean.True);
        JintInterop.DefineAccessor(engine, nav, "cookieEnabled", (_, _) => JsBoolean.True);
        JintInterop.DefineMethod(engine, nav, "javaEnabled", (_, _) => JsBoolean.False, length: 0);
        JintInterop.DefineMethod(engine, nav, "sendBeacon", (_, _) => JsBoolean.False, length: 2);
        return nav;
    }

    private static JsObject BuildLocation(JintBackendContext ctx)
    {
        var engine = ctx.Engine;
        var loc = new JsObject(engine);

        JintInterop.DefineAccessor(engine, loc, "href",
            (_, _) => JintInterop.Str(UrlFor(ctx)),
            (_, args) =>
            {
                var target = args.Length > 0 ? args[0].ToString() : "";
                ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                    $"location.href assignment ignored (navigation not yet wired): {target}");
                return JsValue.Undefined;
            });
        JintInterop.DefineAccessor(engine, loc, "protocol", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Scheme + ":")));
        JintInterop.DefineAccessor(engine, loc, "host", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Authority)));
        JintInterop.DefineAccessor(engine, loc, "hostname", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Host)));
        JintInterop.DefineAccessor(engine, loc, "port", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.IsDefaultPort ? "" : p.Port.ToString(CultureInfo.InvariantCulture))));
        JintInterop.DefineAccessor(engine, loc, "pathname", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.AbsolutePath)));
        JintInterop.DefineAccessor(engine, loc, "search", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Query)));
        JintInterop.DefineAccessor(engine, loc, "hash", (_, _) => JintInterop.Str(ParsedPart(ctx, p => p.Fragment)));
        JintInterop.DefineAccessor(engine, loc, "origin", (_, _) => JintInterop.Str(ParsedPart(ctx, p => $"{p.Scheme}://{p.Authority}")));

        JintInterop.DefineMethod(engine, loc, "toString", (_, _) => JintInterop.Str(UrlFor(ctx)), length: 0);
        JintInterop.DefineMethod(engine, loc, "assign", (_, args) =>
        {
            var target = args.Length > 0 ? args[0].ToString() : "";
            ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                $"location.assign ignored (navigation not yet wired): {target}");
            return JsValue.Undefined;
        }, length: 1);
        JintInterop.DefineMethod(engine, loc, "replace", (_, args) =>
        {
            var target = args.Length > 0 ? args[0].ToString() : "";
            ctx.Diag.Log(Starling.Common.Diagnostics.DiagLevel.Warn, "engine.js",
                $"location.replace ignored (navigation not yet wired): {target}");
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
