using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Jint.Runtime.Interop;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Html;
using Starling.Html.TreeBuilder;

namespace Starling.Bindings.Jint;

// J2b — Node / Element / Document prototypes + methods for the Jint backend.
//
// Mirrors the semantics (property names, accessor/operation shape, selector
// grammar, innerHTML parsing) of Starling.Bindings/NodeBindings.cs,
// DomWrappers.cs, and QuerySelectorEngine.cs — but over the Jint value model
// and the J2a JintInterop/JintDomWrapper helpers, not the Starling.Js types.
//
// Edits are confined to this file (plus the Starling.Html project reference in
// the csproj, required for the HTML fragment parser/serializer). Prototype slots
// are set on ctx.Wrappers so JintDomWrapper.GetOrCreate mints wrappers whose
// prototype is the most-derived installed slot for the node's type.
internal static class NodeBindings
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;

        // ---- prototype chain -------------------------------------------------
        // Node => EventTarget (if J2c installed it) => Object.
        // Element => Node, CharacterData => Node, Text => CharacterData,
        // Document => Node.
        var nodeProto = NewProto(engine, ctx.Wrappers.EventTargetPrototype);
        ctx.Wrappers.NodePrototype = nodeProto;

        var charDataProto = NewProto(engine, nodeProto);
        var textProto = NewProto(engine, charDataProto);

        var elementProto = NewProto(engine, nodeProto);
        ctx.Wrappers.ElementPrototype = elementProto;

        var documentProto = NewProto(engine, nodeProto);
        ctx.Wrappers.DocumentPrototype = documentProto;

        InstallNode(ctx, nodeProto);
        InstallCharacterData(ctx, charDataProto);
        InstallElement(ctx, elementProto);
        InstallDocument(ctx, documentProto);

        InstallConstructors(ctx, nodeProto, elementProto, documentProto, charDataProto, textProto);

        // Expose `document` on the global so scripts can reach the DOM.
        JintInterop.DefineDataProp(engine.Global, "document",
            ctx.Wrappers.Wrap(ctx.Document), writable: true, enumerable: false, configurable: true);
    }

    // =====================================================================
    //                              Node
    // =====================================================================
    private static void InstallNode(JintBackendContext ctx, ObjectInstance proto)
    {
        var engine = ctx.Engine;
        var w = ctx.Wrappers;

        Accessor(ctx, proto, "nodeType",
            (t, _) => w.UnwrapNode(t) is { } n ? JintInterop.Num((int)n.Kind) : JintInterop.Num(0));
        Accessor(ctx, proto, "nodeName",
            (t, _) => w.UnwrapNode(t) is { } n ? JintInterop.Str(NodeName(n)) : JintInterop.Str(""));
        Accessor(ctx, proto, "nodeValue",
            (t, _) => w.UnwrapNode(t)?.NodeValue is { } s ? JintInterop.Str(s) : JsValue.Null,
            (t, args) =>
            {
                if (w.UnwrapNode(t) is { } n)
                    n.NodeValue = Arg(args, 0).IsNull() || Arg(args, 0).IsUndefined() ? null : Str(Arg(args, 0));
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "textContent",
            (t, _) => w.UnwrapNode(t) is { } n ? JintInterop.Str(n.TextContent) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapNode(t) is { } n)
                    n.TextContent = args.Length > 0 ? Str(args[0]) : "";
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "parentNode",
            (t, _) => w.UnwrapNode(t)?.ParentNode is { } p ? w.Wrap(p) : JsValue.Null);
        Accessor(ctx, proto, "parentElement",
            (t, _) => w.UnwrapNode(t)?.ParentNode is Element pe ? w.Wrap(pe) : JsValue.Null);
        Accessor(ctx, proto, "firstChild",
            (t, _) => w.UnwrapNode(t)?.FirstChild is { } c ? w.Wrap(c) : JsValue.Null);
        Accessor(ctx, proto, "lastChild",
            (t, _) => w.UnwrapNode(t)?.LastChild is { } c ? w.Wrap(c) : JsValue.Null);
        Accessor(ctx, proto, "nextSibling",
            (t, _) => w.UnwrapNode(t)?.NextSibling is { } c ? w.Wrap(c) : JsValue.Null);
        Accessor(ctx, proto, "previousSibling",
            (t, _) => w.UnwrapNode(t)?.PreviousSibling is { } c ? w.Wrap(c) : JsValue.Null);
        Accessor(ctx, proto, "childNodes", (t, _) =>
        {
            if (w.UnwrapNode(t) is not { } n) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var c in n.ChildNodes) items.Add(w.Wrap(c));
            return Array(engine, items);
        });
        Accessor(ctx, proto, "ownerDocument", (t, _) =>
        {
            var n = w.UnwrapNode(t);
            if (n is Document) return JsValue.Null; // documents have no owner
            return n?.OwnerDocument is { } d ? w.Wrap(d) : JsValue.Null;
        });
        Accessor(ctx, proto, "isConnected",
            (t, _) => JintInterop.Bool(IsConnected(w.UnwrapNode(t))));

        Method(ctx, proto, "appendChild", (t, args) =>
        {
            var parent = w.UnwrapNode(t);
            var child = w.UnwrapNode(Arg(args, 0));
            if (parent is null || child is null) throw TypeError(engine, "appendChild requires a Node argument");
            parent.AppendChild(child);
            return args[0];
        }, 1);
        Method(ctx, proto, "removeChild", (t, args) =>
        {
            var parent = w.UnwrapNode(t);
            var child = w.UnwrapNode(Arg(args, 0));
            if (parent is null || child is null) throw TypeError(engine, "removeChild requires a Node argument");
            parent.RemoveChild(child);
            return args[0];
        }, 1);
        Method(ctx, proto, "insertBefore", (t, args) =>
        {
            var parent = w.UnwrapNode(t);
            var child = w.UnwrapNode(Arg(args, 0));
            var refChild = w.UnwrapNode(Arg(args, 1));
            if (parent is null || child is null) throw TypeError(engine, "insertBefore requires a Node argument");
            parent.InsertBefore(child, refChild);
            return args[0];
        }, 2);
        Method(ctx, proto, "replaceChild", (t, args) =>
        {
            var parent = w.UnwrapNode(t);
            var newChild = w.UnwrapNode(Arg(args, 0));
            var oldChild = w.UnwrapNode(Arg(args, 1));
            if (parent is null || newChild is null || oldChild is null)
                throw TypeError(engine, "replaceChild requires two Node arguments");
            parent.ReplaceChild(newChild, oldChild);
            return args[1];
        }, 2);
        Method(ctx, proto, "hasChildNodes",
            (t, _) => JintInterop.Bool(w.UnwrapNode(t)?.FirstChild is not null), 0);
        Method(ctx, proto, "cloneNode", (t, args) =>
        {
            if (w.UnwrapNode(t) is not { } n) return JsValue.Null;
            var deep = args.Length > 0 && Bool(args[0]);
            return w.Wrap(CloneNode(n, deep));
        }, 0);
        Method(ctx, proto, "normalize", (t, _) =>
        {
            if (w.UnwrapNode(t) is { } n) NormalizeNode(n);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "contains", (t, args) =>
        {
            var self = w.UnwrapNode(t);
            var other = w.UnwrapNode(Arg(args, 0));
            if (self is null || other is null) return JsBoolean.False;
            for (var n = other; n is not null; n = n.ParentNode)
                if (ReferenceEquals(n, self)) return JsBoolean.True;
            return JsBoolean.False;
        }, 1);

        // Node type constants (Node.ELEMENT_NODE, etc.) on the prototype.
        DefineNodeConstants(proto);
    }

    // =====================================================================
    //                          CharacterData / Text
    // =====================================================================
    private static void InstallCharacterData(JintBackendContext ctx, ObjectInstance proto)
    {
        var w = ctx.Wrappers;
        Accessor(ctx, proto, "data",
            (t, _) => w.Unwrap(t) is CharacterData c ? JintInterop.Str(c.Data) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.Unwrap(t) is CharacterData c) c.Data = args.Length > 0 ? Str(args[0]) : "";
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "length",
            (t, _) => w.Unwrap(t) is CharacterData c ? JintInterop.Num(c.Data.Length) : JintInterop.Num(0));
    }

    // =====================================================================
    //                             Element
    // =====================================================================
    private static void InstallElement(JintBackendContext ctx, ObjectInstance proto)
    {
        var engine = ctx.Engine;
        var w = ctx.Wrappers;

        Accessor(ctx, proto, "tagName",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.TagName.ToUpperInvariant()) : JintInterop.Str(""));
        Accessor(ctx, proto, "localName",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.LocalName) : JintInterop.Str(""));
        Accessor(ctx, proto, "namespaceURI",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.Namespace) : JsValue.Null);
        Accessor(ctx, proto, "id",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.Id) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e) e.Id = args.Length > 0 ? Str(args[0]) : "";
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "className",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.GetAttribute("class") ?? "") : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e) e.SetAttribute("class", args.Length > 0 ? Str(args[0]) : "");
                return JsValue.Undefined;
            });
        // `value` IDL attribute of form controls. Real browsers expose it on
        // HTMLInputElement/HTMLTextAreaElement/... prototypes, not on every
        // Element; we share one element prototype, so we gate on the tag and
        // return undefined for non-controls (keeps `'value' in el` / `el.value
        // !== undefined` feature tests honest). Backed by the `value` attribute
        // — the same source BoxTreeBuilder paints input text from — so a JS
        // `el.value = x` round-trips and shows up in layout. Without this,
        // `input.value` was undefined and `input.value.toLowerCase()` threw,
        // which is what aborted McMaster's search-box init and blanked the page.
        Accessor(ctx, proto, "value",
            (t, _) =>
            {
                if (w.UnwrapElement(t) is not { } e || !IsFormControl(e)) return JsValue.Undefined;
                // Read the live IDL value: InputValue (what the user typed / a
                // script assigned) in preference to the `value` content attribute,
                // matching BoxTreeBuilder's painted text and the GUI editor. A
                // textarea's value is its text content.
                return JintInterop.Str(IsTextArea(e)
                    ? e.TextContent
                    : e.InputValue ?? e.GetAttribute("value") ?? "");
            },
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e && IsFormControl(e))
                {
                    var v = args.Length > 0 ? Str(args[0]) : "";
                    // Write the live IDL value (not the content attribute) so
                    // `el.value = …` round-trips through layout and the GUI caret,
                    // and `getAttribute("value")` still reports the initial value.
                    if (IsTextArea(e)) e.TextContent = v;
                    else e.InputValue = v;
                }
                return JsValue.Undefined;
            });
        // focus()/blur() drive Document.FocusedElement — the same focus model the
        // GUI caret and `:focus` styling read, and what `document.activeElement`
        // reports. A field that scripts re-focus after a form submit (the classic
        // todo pattern) thus keeps the GUI caret.
        Method(ctx, proto, "focus", (t, _) =>
        {
            if (w.UnwrapElement(t) is { OwnerDocument: { } d } e) d.FocusedElement = e;
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "blur", (t, _) =>
        {
            if (w.UnwrapElement(t) is { OwnerDocument: { } d } e
                && ReferenceEquals(d.FocusedElement, e))
                d.FocusedElement = null;
            return JsValue.Undefined;
        }, 0);

        Accessor(ctx, proto, "innerHTML",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(HtmlSerializer.SerializeChildren(e)) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e)
                {
                    var markup = args.Length > 0 ? Str(args[0]) : "";
                    var fragment = ParseFragment(e, markup);
                    while (e.FirstChild is not null) e.RemoveChild(e.FirstChild);
                    e.AppendChild(fragment);
                }
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "outerHTML",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(HtmlSerializer.SerializeNode(e)) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is not { } e) return JsValue.Undefined;
                var parent = e.ParentNode
                    ?? throw TypeError(engine, "Cannot set outerHTML on an element with no parent");
                var context = parent as Element ?? e;
                var markup = args.Length > 0 ? Str(args[0]) : "";
                var fragment = ParseFragment(context, markup);
                parent.InsertBefore(fragment, e);
                parent.RemoveChild(e);
                return JsValue.Undefined;
            });
        Accessor(ctx, proto, "innerText",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.TextContent) : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e) e.TextContent = args.Length > 0 ? Str(args[0]) : "";
                return JsValue.Undefined;
            });

        Accessor(ctx, proto, "children", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return EmptyArray(engine);
            var items = new List<JsValue>();
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element c) items.Add(w.Wrap(c));
            return Array(engine, items);
        });
        Accessor(ctx, proto, "firstElementChild", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return JsValue.Null;
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element c) return w.Wrap(c);
            return JsValue.Null;
        });
        Accessor(ctx, proto, "lastElementChild", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return JsValue.Null;
            for (var n = e.LastChild; n is not null; n = n.PreviousSibling)
                if (n is Element c) return w.Wrap(c);
            return JsValue.Null;
        });
        Accessor(ctx, proto, "nextElementSibling", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return JsValue.Null;
            for (var n = e.NextSibling; n is not null; n = n.NextSibling)
                if (n is Element s) return w.Wrap(s);
            return JsValue.Null;
        });
        Accessor(ctx, proto, "previousElementSibling", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return JsValue.Null;
            for (var n = e.PreviousSibling; n is not null; n = n.PreviousSibling)
                if (n is Element s) return w.Wrap(s);
            return JsValue.Null;
        });
        Accessor(ctx, proto, "childElementCount", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return JintInterop.Num(0);
            var count = 0;
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element) count++;
            return JintInterop.Num(count);
        });

        Method(ctx, proto, "getAttribute", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return JsValue.Null;
            var v = e.GetAttribute(Str(args[0]));
            return v is null ? JsValue.Null : JintInterop.Str(v);
        }, 1);
        Method(ctx, proto, "setAttribute", (t, args) =>
        {
            if (w.UnwrapElement(t) is { } e && args.Length >= 2)
            {
                var name = Str(args[0]);
                e.SetAttribute(name, Str(args[1]));
                // HTML §4.12.1: setting `src` on a not-yet-started <script> from
                // JS runs "prepare a script" (the deferred-bundle loader pattern,
                // e.g. a parser-created empty <script> that later gets a src as a
                // newly-fetched external script).
                MaybeTriggerScriptSrc(ctx, e, name);
            }
            return JsValue.Undefined;
        }, 2);

        // `script.src` IDL property — get/set the src attribute. The setter
        // mirrors setAttribute('src', …) and runs "prepare a script" so
        // `el.src = url` loads the same way `setAttribute` does. Defined on
        // Element.prototype but only meaningful for <script>; for non-script
        // elements it's a plain attribute round-trip.
        Accessor(ctx, proto, "src",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Str(e.GetAttribute("src") ?? "") : JintInterop.Str(""),
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e)
                {
                    e.SetAttribute("src", args.Length > 0 ? Str(args[0]) : "");
                    MaybeTriggerScriptSrc(ctx, e, "src");
                }
                return JsValue.Undefined;
            });
        // `script.async` IDL property. Reflects the boolean `async` content
        // attribute so `el.async = true` is observable by the engine's script
        // classification (HTML §4.12.1). A script-inserted external script
        // defaults to async; analytics snippets (ga.js/gtag) set this flag, so
        // reflecting it is what lets the engine defer them past first paint.
        Accessor(ctx, proto, "async",
            (t, _) => w.UnwrapElement(t) is { } e ? JintInterop.Bool(e.HasAttribute("async")) : JsBoolean.False,
            (t, args) =>
            {
                if (w.UnwrapElement(t) is { } e)
                {
                    if (args.Length > 0 && Bool(args[0])) e.SetAttribute("async", "");
                    else e.RemoveAttribute("async");
                }
                return JsValue.Undefined;
            });
        Method(ctx, proto, "hasAttribute", (t, args) =>
            w.UnwrapElement(t) is { } e && args.Length > 0
                ? JintInterop.Bool(e.HasAttribute(Str(args[0])))
                : JsBoolean.False, 1);
        Method(ctx, proto, "removeAttribute", (t, args) =>
        {
            if (w.UnwrapElement(t) is { } e && args.Length > 0) e.RemoveAttribute(Str(args[0]));
            return JsValue.Undefined;
        }, 1);
        Method(ctx, proto, "getAttributeNames", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var a in e.Attributes) items.Add(JintInterop.Str(a.Name));
            return Array(engine, items);
        }, 0);
        Method(ctx, proto, "toggleAttribute", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return JsBoolean.False;
            var name = Str(args[0]);
            bool result;
            if (args.Length > 1 && !args[1].IsUndefined())
            {
                var force = Bool(args[1]);
                if (force) { e.SetAttribute(name, ""); result = true; }
                else { e.RemoveAttribute(name); result = false; }
            }
            else if (e.HasAttribute(name)) { e.RemoveAttribute(name); result = false; }
            else { e.SetAttribute(name, ""); result = true; }
            return JintInterop.Bool(result);
        }, 1);

        // attributes — snapshot NamedNodeMap-shaped array of {name,value}.
        Accessor(ctx, proto, "attributes", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var a in e.Attributes)
            {
                var obj = new JsObject(engine);
                JintInterop.DefineDataProp(obj, "name", JintInterop.Str(a.Name));
                JintInterop.DefineDataProp(obj, "value", JintInterop.Str(a.Value));
                items.Add(obj);
            }
            return Array(engine, items);
        });

        // Selector engine — querySelector / querySelectorAll / matches / closest.
        Method(ctx, proto, "querySelector", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return JsValue.Null;
            var match = SelectorFirst(engine, e, Str(args[0]));
            return match is null ? JsValue.Null : w.Wrap(match);
        }, 1);
        Method(ctx, proto, "querySelectorAll", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var m in SelectorAll(engine, e, Str(args[0]))) items.Add(w.Wrap(m));
            return Array(engine, items);
        }, 1);
        Method(ctx, proto, "matches", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return JsBoolean.False;
            return JintInterop.Bool(SelectorMatches(engine, e, Str(args[0])));
        }, 1);
        Method(ctx, proto, "closest", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return JsValue.Null;
            var match = SelectorClosest(engine, e, Str(args[0]));
            return match is null ? JsValue.Null : w.Wrap(match);
        }, 1);
        Method(ctx, proto, "getElementsByTagName", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return EmptyArray(engine);
            var name = Str(args[0]);
            var items = new List<JsValue>();
            foreach (var d in e.DescendantElements())
                if (name == "*" || d.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    items.Add(w.Wrap(d));
            return Array(engine, items);
        }, 1);
        Method(ctx, proto, "getElementsByClassName", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e || args.Length == 0) return EmptyArray(engine);
            var classes = Str(args[0]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var items = new List<JsValue>();
            foreach (var d in e.DescendantElements())
                if (classes.Length > 0 && classes.All(d.ClassList.Contains)) items.Add(w.Wrap(d));
            return Array(engine, items);
        }, 1);

        // ChildNode / ParentNode mixins.
        Method(ctx, proto, "remove", (t, _) =>
        {
            if (w.UnwrapNode(t) is { } n) n.RemoveFromParent();
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "prepend", (t, args) =>
        {
            if (w.UnwrapNode(t) is not { } parent) return JsValue.Undefined;
            var refChild = parent.FirstChild;
            foreach (var a in args) InsertAdjacent(w, parent, a, refChild);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "append", (t, args) =>
        {
            if (w.UnwrapNode(t) is not { } parent) return JsValue.Undefined;
            foreach (var a in args) InsertAdjacent(w, parent, a, null);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "before", (t, args) =>
        {
            var self = w.UnwrapNode(t);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            foreach (var a in args) InsertAdjacent(w, parent, a, self);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "after", (t, args) =>
        {
            var self = w.UnwrapNode(t);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            var refChild = self.NextSibling;
            foreach (var a in args) InsertAdjacent(w, parent, a, refChild);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "replaceWith", (t, args) =>
        {
            var self = w.UnwrapNode(t);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            var refChild = self.NextSibling;
            foreach (var a in args) InsertAdjacent(w, parent, a, refChild);
            self.RemoveFromParent();
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "replaceChildren", (t, args) =>
        {
            if (w.UnwrapNode(t) is not { } parent) return JsValue.Undefined;
            while (parent.FirstChild is not null) parent.RemoveChild(parent.FirstChild);
            foreach (var a in args) InsertAdjacent(w, parent, a, null);
            return JsValue.Undefined;
        }, 0);
        Method(ctx, proto, "insertAdjacentHTML", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e) throw TypeError(engine, "insertAdjacentHTML called on a non-Element");
            if (args.Length < 2) throw TypeError(engine, "insertAdjacentHTML requires (position, text)");
            var position = Str(args[0]);
            var markup = Str(args[1]);
            switch (position.ToLowerInvariant())
            {
                case "beforebegin":
                    {
                        if (e.ParentNode is not { } parent) return JsValue.Undefined;
                        var context = parent as Element ?? e;
                        parent.InsertBefore(ParseFragment(context, markup), e);
                        break;
                    }
                case "afterbegin":
                    e.InsertBefore(ParseFragment(e, markup), e.FirstChild);
                    break;
                case "beforeend":
                    e.AppendChild(ParseFragment(e, markup));
                    break;
                case "afterend":
                    {
                        if (e.ParentNode is not { } parent) return JsValue.Undefined;
                        var context = parent as Element ?? e;
                        parent.InsertBefore(ParseFragment(context, markup), e.NextSibling);
                        break;
                    }
                default:
                    throw TypeError(engine, $"insertAdjacentHTML: '{position}' is not a valid position");
            }
            return JsValue.Undefined;
        }, 2);

        // classList / dataset / style — lazily built + cached on the wrapper.
        Accessor(ctx, proto, "classList", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e || t is not ObjectInstance wrapper) return JsValue.Null;
            return Cached(wrapper, "__classList__", () => BuildDomTokenList(ctx, e));
        });
        Accessor(ctx, proto, "dataset", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e || t is not ObjectInstance wrapper) return JsValue.Null;
            return Cached(wrapper, "__dataset__", () => BuildDataset(ctx, e));
        });
        Accessor(ctx, proto, "style", (t, _) =>
        {
            if (w.UnwrapElement(t) is not { } e || t is not ObjectInstance wrapper) return JsValue.Null;
            return Cached(wrapper, "__style__", () => BuildStyle(ctx, e));
        });

        // Layout-readback APIs — route through the session's optional
        // ILayoutHost (strongly typed on ctx.LayoutHost since J7),
        // exactly as the Starling backend's NodeBindings does. The host's
        // TryGetBoundingClientRect / TryGetOffsetMetrics trigger the engine's
        // lazy pre-script layout and increment its diagnostics counters. With no
        // host (bare unit-test contexts) every read returns spec-permitted zeros,
        // matching a never-laid-out document.
        Method(ctx, proto, "getBoundingClientRect", (t, _) =>
        {
            if (w.UnwrapElement(t) is { } e && LayoutHost(ctx) is { } host &&
                host.TryGetBoundingClientRect(e, out var r))
            {
                return BuildDomRect(engine, r);
            }
            return BuildDomRect(engine, default);
        }, 0);
        Method(ctx, proto, "getClientRects", (t, _) =>
        {
            // Single-rect simplification — block boxes only return one rect,
            // which covers every layout-readback path the tests touch. Inline
            // flows that emit multiple line boxes would need the box-tree walk.
            if (w.UnwrapElement(t) is { } e && LayoutHost(ctx) is { } host &&
                host.TryGetBoundingClientRect(e, out var r))
            {
                return Array(engine, new JsValue[] { BuildDomRect(engine, r) });
            }
            return EmptyArray(engine);
        }, 0);
        Accessor(ctx, proto, "offsetWidth", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetWidth));
        Accessor(ctx, proto, "offsetHeight", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetHeight));
        Accessor(ctx, proto, "offsetTop", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetTop));
        Accessor(ctx, proto, "offsetLeft", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetLeft));
        Accessor(ctx, proto, "clientWidth", (t, _) => ReadOffsetMetric(ctx, t, m => m.ClientWidth));
        Accessor(ctx, proto, "clientHeight", (t, _) => ReadOffsetMetric(ctx, t, m => m.ClientHeight));
        // scrollWidth/Height mirror the border-box metrics until content-overflow
        // is wired (matches the Starling backend).
        Accessor(ctx, proto, "scrollWidth", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetWidth));
        Accessor(ctx, proto, "scrollHeight", (t, _) => ReadOffsetMetric(ctx, t, m => m.OffsetHeight));
        // scrollTop/Left stay 0 (no scrolling is wired through this surface yet);
        // the setters are accepted no-ops so assignment doesn't throw.
        Accessor(ctx, proto, "scrollTop", (_, _) => JintInterop.Num(0), (_, _) => JsValue.Undefined);
        Accessor(ctx, proto, "scrollLeft", (_, _) => JintInterop.Num(0), (_, _) => JsValue.Undefined);

        // HTMLCanvasElement.getContext — canvas-only. We have no real raster
        // surface behind the JS seam, but real pages (e.g. McMaster) use a
        // throwaway 2D context purely to measure text widths for layout sizing.
        // Return a stub 2D context (cached per canvas) with a settable `font`
        // and a `measureText` that estimates width from the font size; all the
        // drawing operations are accepted no-ops. Non-canvas elements and
        // unsupported context types return null (spec-correct).
        Method(ctx, proto, "getContext", (t, args) =>
        {
            if (w.UnwrapElement(t) is not { } e ||
                !e.LocalName.Equals("canvas", StringComparison.OrdinalIgnoreCase))
                return JsValue.Null;
            var type = args.Length > 0 ? Str(args[0]) : "";
            if (!type.Equals("2d", StringComparison.OrdinalIgnoreCase)) return JsValue.Null;
            if (t is not ObjectInstance wrapper) return JsValue.Null;
            return Cached(wrapper, "__ctx2d__", () => BuildCanvas2dContext(engine, wrapper));
        }, 1);
    }

    /// <summary>Build a stub <c>CanvasRenderingContext2D</c>. There is no raster
    /// surface behind the JS seam, so drawing ops are no-ops; the one method that
    /// must return a meaningful value is <c>measureText</c>, which pages use to
    /// size text. We estimate the advance width from the px size parsed out of
    /// the current <c>font</c> (≈0.5em per character — a serviceable mean for
    /// proportional fonts). <paramref name="canvas"/> is exposed via the
    /// <c>canvas</c> back-reference per spec.</summary>
    private static JsObject BuildCanvas2dContext(global::Jint.Engine engine, ObjectInstance canvas)
    {
        var c2d = new JsObject(engine);
        JintInterop.DefineDataProp(c2d, "canvas", canvas, writable: false, enumerable: true, configurable: true);
        // Settable state the measurement path reads/writes.
        foreach (var (name, def) in new (string, string)[]
        {
            ("font", "10px sans-serif"), ("textAlign", "start"), ("textBaseline", "alphabetic"),
            ("fillStyle", "#000000"), ("strokeStyle", "#000000"), ("lineCap", "butt"), ("lineJoin", "miter"),
        })
        {
            JintInterop.DefineDataProp(c2d, name, JintInterop.Str(def), writable: true, enumerable: true, configurable: true);
        }
        JintInterop.DefineDataProp(c2d, "lineWidth", JintInterop.Num(1), writable: true, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(c2d, "globalAlpha", JintInterop.Num(1), writable: true, enumerable: true, configurable: true);

        JintInterop.DefineMethod(engine, c2d, "measureText", (self, a) =>
        {
            var text = a.Length > 0 ? Str(a[0]) : "";
            var px = 10.0;
            if (self is ObjectInstance o && o.Get("font") is { } f && !f.IsUndefined())
                px = ParseFontPx(f.ToString());
            var width = text.Length * px * 0.5;
            var metrics = new JsObject(engine);
            JintInterop.DefineDataProp(metrics, "width", JintInterop.Num(width));
            // A couple of TextMetrics fields some scripts read; conservative values.
            JintInterop.DefineDataProp(metrics, "actualBoundingBoxAscent", JintInterop.Num(px * 0.8));
            JintInterop.DefineDataProp(metrics, "actualBoundingBoxDescent", JintInterop.Num(px * 0.2));
            return metrics;
        }, 1);

        // Drawing / state ops — accepted no-ops so canvas code never throws.
        foreach (var name in new[]
        {
            "fillRect", "clearRect", "strokeRect", "fillText", "strokeText", "beginPath", "closePath",
            "moveTo", "lineTo", "rect", "arc", "fill", "stroke", "save", "restore", "scale", "rotate",
            "translate", "transform", "setTransform", "drawImage", "clip", "setLineDash",
        })
        {
            JintInterop.DefineMethod(engine, c2d, name, (_, _) => JsValue.Undefined, 0);
        }
        return c2d;
    }

    /// <summary>Pull the pixel size out of a CSS shorthand <c>font</c> string
    /// (e.g. <c>"normal 12px Arial"</c> → 12). Defaults to 10 when absent.</summary>
    private static double ParseFontPx(string font)
    {
        var idx = font.IndexOf("px", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return 10.0;
        var start = idx;
        while (start > 0 && (char.IsDigit(font[start - 1]) || font[start - 1] == '.')) start--;
        return double.TryParse(font.AsSpan(start, idx - start),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var px)
            ? px : 10.0;
    }

    // =====================================================================
    //                            Document
    // =====================================================================
    private static void InstallDocument(JintBackendContext ctx, ObjectInstance proto)
    {
        var engine = ctx.Engine;
        var w = ctx.Wrappers;

        Accessor(ctx, proto, "documentElement",
            (t, _) => w.UnwrapDocument(t)?.DocumentElement is { } e ? w.Wrap(e) : JsValue.Null);
        Accessor(ctx, proto, "body",
            (t, _) => w.UnwrapDocument(t)?.Body is { } b ? w.Wrap(b) : JsValue.Null);
        Accessor(ctx, proto, "head",
            (t, _) => w.UnwrapDocument(t)?.Head is { } h ? w.Wrap(h) : JsValue.Null);
        Accessor(ctx, proto, "title",
            (t, _) =>
            {
                if (w.UnwrapDocument(t) is not { } d) return JintInterop.Str("");
                foreach (var n in d.Descendants())
                    if (n is Element { LocalName: "title" } titleEl) return JintInterop.Str(titleEl.TextContent.Trim());
                return JintInterop.Str("");
            },
            (t, args) =>
            {
                if (w.UnwrapDocument(t) is not { } d) return JsValue.Undefined;
                var value = args.Length > 0 ? Str(args[0]) : "";
                // Set the text of the first <title>, creating one in <head> if absent.
                Element? titleEl = null;
                foreach (var n in d.Descendants())
                    if (n is Element { LocalName: "title" } found) { titleEl = found; break; }
                if (titleEl is null)
                {
                    titleEl = d.CreateElement("title");
                    (d.Head ?? d.DocumentElement)?.AppendChild(titleEl);
                }
                titleEl.TextContent = value;
                return JsValue.Undefined;
            });
        // The focused element, falling back to <body> per the HTML spec so
        // `document.activeElement.tagName` never throws on an unfocused page.
        Accessor(ctx, proto, "activeElement", (t, _) =>
        {
            if (w.UnwrapDocument(t) is not { } d) return JsValue.Null;
            var el = d.FocusedElement ?? (Element?)d.Body;
            return el is null ? JsValue.Null : w.Wrap(el);
        });
        Accessor(ctx, proto, "readyState", (_, _) => JintInterop.Str("complete"));
        Accessor(ctx, proto, "compatMode",
            (t, _) => w.UnwrapDocument(t) is { } d
                ? JintInterop.Str(d.Mode == QuirksMode.Quirks ? "BackCompat" : "CSS1Compat")
                : JintInterop.Str("CSS1Compat"));
        Accessor(ctx, proto, "hidden", (_, _) => JsBoolean.False);
        Accessor(ctx, proto, "visibilityState", (_, _) => JintInterop.Str("visible"));

        // DOM §4.5 — document.implementation returns a DOMImplementation object.
        // Cached per-document on the wrapper (hidden slot) so identity is stable.
        Accessor(ctx, proto, "implementation", (t, _) =>
        {
            if (t is not ObjectInstance wrapper) return JsValue.Null;
            return Cached(wrapper, "__domImpl__", () => BuildDomImplementation(ctx));
        });
        Accessor(ctx, proto, "characterSet", (_, _) => JintInterop.Str("UTF-8"));
        Accessor(ctx, proto, "contentType", (_, _) => JintInterop.Str("text/html"));

        // document.location / URL / documentURI — mirror the Starling backend.
        // document.location shares the same object as window.location.
        Accessor(ctx, proto, "location",
            (t, _) => w.UnwrapDocument(t) is null ? JsValue.Null : WindowBinding.LocationObjectFor(ctx));
        Accessor(ctx, proto, "URL",
            (t, _) => w.UnwrapDocument(t) is null ? JintInterop.Str("") : JintInterop.Str(WindowBinding.UrlFor(ctx)));
        Accessor(ctx, proto, "documentURI",
            (t, _) => w.UnwrapDocument(t) is null ? JintInterop.Str("") : JintInterop.Str(WindowBinding.UrlFor(ctx)));
        Accessor(ctx, proto, "defaultView",
            (t, _) => w.UnwrapDocument(t) is null ? JsValue.Null : engine.Global);

        Method(ctx, proto, "getElementById", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0) return JsValue.Null;
            return d.GetElementById(Str(args[0])) is { } e ? w.Wrap(e) : JsValue.Null;
        }, 1);
        Method(ctx, proto, "getElementsByTagName", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var e in d.GetElementsByTagName(Str(args[0]))) items.Add(w.Wrap(e));
            return Array(engine, items);
        }, 1);
        Method(ctx, proto, "getElementsByClassName", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var e in d.GetElementsByClassName(Str(args[0]))) items.Add(w.Wrap(e));
            return Array(engine, items);
        }, 1);
        Method(ctx, proto, "querySelector", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0) return JsValue.Null;
            var match = SelectorFirst(engine, d, Str(args[0]));
            return match is null ? JsValue.Null : w.Wrap(match);
        }, 1);
        Method(ctx, proto, "querySelectorAll", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0) return EmptyArray(engine);
            var items = new List<JsValue>();
            foreach (var m in SelectorAll(engine, d, Str(args[0]))) items.Add(w.Wrap(m));
            return Array(engine, items);
        }, 1);
        Method(ctx, proto, "createElement", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length == 0)
                throw TypeError(engine, "createElement requires a tag name");
            return w.Wrap(d.CreateElement(Str(args[0])));
        }, 1);
        Method(ctx, proto, "createElementNS", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d || args.Length < 2)
                throw TypeError(engine, "createElementNS requires (namespace, qualifiedName)");
            var ns = Arg(args, 0).IsNull() ? null : Str(args[0]);
            return w.Wrap(d.CreateElement(Str(args[1]), ns));
        }, 2);
        Method(ctx, proto, "createTextNode", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d) throw TypeError(engine, "createTextNode called on non-Document");
            return w.Wrap(d.CreateTextNode(args.Length > 0 ? Str(args[0]) : ""));
        }, 1);
        Method(ctx, proto, "createComment", (t, args) =>
        {
            if (w.UnwrapDocument(t) is not { } d) throw TypeError(engine, "createComment called on non-Document");
            return w.Wrap(d.CreateComment(args.Length > 0 ? Str(args[0]) : ""));
        }, 1);
        Method(ctx, proto, "createDocumentFragment", (t, _) =>
        {
            if (w.UnwrapDocument(t) is not { } d) throw TypeError(engine, "createDocumentFragment called on non-Document");
            return w.Wrap(d.CreateDocumentFragment());
        }, 0);
        // Minimal createEvent — returns a plain object carrying a settable type +
        // initEvent. Full Event semantics live in J2c; this keeps legacy
        // document.createEvent('Event') call sites from throwing.
        // TODO(J2b): route through J2c's Event prototype once available.
        Method(ctx, proto, "createEvent", (_, _) =>
        {
            var ev = new JsObject(engine);
            JintInterop.DefineDataProp(ev, "type", JintInterop.Str(""));
            JintInterop.DefineDataProp(ev, "bubbles", JsBoolean.False);
            JintInterop.DefineDataProp(ev, "cancelable", JsBoolean.False);
            JintInterop.DefineMethod(engine, ev, "initEvent", (self, a) =>
            {
                if (self is ObjectInstance o && a.Length > 0)
                {
                    o.Set("type", JintInterop.Str(Str(a[0])));
                    if (a.Length > 1) o.Set("bubbles", JintInterop.Bool(Bool(a[1])));
                    if (a.Length > 2) o.Set("cancelable", JintInterop.Bool(Bool(a[2])));
                }
                return JsValue.Undefined;
            }, 3);
            return ev;
        }, 1);
    }

    // =====================================================================
    //                          Constructors
    // =====================================================================
    // Expose Node/Element/Document/CharacterData/Text/HTMLElement on the global
    // with the right .prototype + .constructor wiring so `instanceof`,
    // `Node.ELEMENT_NODE`, and feature-detection (`'querySelector' in
    // Element.prototype`) work. Calling them throws (illegal constructor).
    private static void InstallConstructors(
        JintBackendContext ctx, ObjectInstance nodeProto, ObjectInstance elementProto,
        ObjectInstance documentProto, ObjectInstance charDataProto, ObjectInstance textProto)
    {
        var engine = ctx.Engine;

        var nodeCtor = MakeCtor(engine, "Node", nodeProto);
        DefineNodeConstants(nodeCtor); // also on the constructor (Node.ELEMENT_NODE)
        var elementCtor = MakeCtor(engine, "Element", elementProto);
        var documentCtor = MakeCtor(engine, "Document", documentProto);
        var charDataCtor = MakeCtor(engine, "CharacterData", charDataProto);
        var textCtor = MakeCtor(engine, "Text", textProto);

        // HTMLElement is a thin subclass of Element (so `el instanceof HTMLElement`
        // and library prototype patches resolve). Element wrappers use
        // ElementPrototype directly; HTMLElement.prototype sits between.
        var htmlElementProto = NewProto(engine, elementProto);
        var htmlElementCtor = MakeCtor(engine, "HTMLElement", htmlElementProto);

        Global(engine, "Node", nodeCtor);
        Global(engine, "Element", elementCtor);
        Global(engine, "Document", documentCtor);
        Global(engine, "CharacterData", charDataCtor);
        Global(engine, "Text", textCtor);
        Global(engine, "HTMLElement", htmlElementCtor);

        // HTMLDocument is a legacy alias of Document; share its prototype so
        // `document instanceof HTMLDocument` resolves.
        Global(engine, "HTMLDocument", MakeCtor(engine, "HTMLDocument", documentProto));

        // Marker constructors for DOM interfaces real pages name in
        // `instanceof` / `typeof X !== 'undefined'` feature tests. We model the
        // collections as plain snapshot arrays (so instanceof is false), but the
        // global names must at least exist so the references don't throw
        // ReferenceError. Non-element interfaces get a plain prototype; element
        // subclasses chain off Element.prototype.
        foreach (var name in new[]
        {
            "HTMLCollection", "NodeList", "DOMTokenList", "NamedNodeMap", "DOMStringMap",
            "CSSStyleDeclaration", "DOMRect", "DOMRectReadOnly", "DOMException",
            // File API + misc interfaces used in `instanceof` / feature tests.
            "Blob", "File", "FileList", "FileReader", "FormData", "DataTransfer",
            "Comment", "DocumentType", "ShadowRoot", "MediaQueryList",
            // SVG interfaces named in `instanceof` checks. Notably SVGAnimatedString:
            // an SVG element's `className` is an SVGAnimatedString (not a string), so
            // className helpers branch on `n instanceof SVGAnimatedString`. Our
            // elements use string classNames, so the test is correctly false — the
            // global just has to exist so the reference doesn't throw.
            "SVGAnimatedString", "SVGElement", "SVGSVGElement", "SVGAnimatedLength",
        })
        {
            Global(engine, name, MakeCtor(engine, name, NewProto(engine, null)));
        }
        InstallImageConstructor(ctx);

        foreach (var name in new[]
        {
            "HTMLInputElement", "HTMLAnchorElement", "HTMLImageElement", "HTMLFormElement",
            "HTMLButtonElement", "HTMLSelectElement", "HTMLTextAreaElement", "HTMLScriptElement",
            "HTMLCanvasElement", "HTMLDivElement", "HTMLSpanElement", "HTMLUListElement",
            "HTMLLIElement", "HTMLParagraphElement", "HTMLHeadingElement", "HTMLTableElement",
            // React's focus management does `node instanceof window.HTMLIFrameElement`
            // while walking the active-element chain — a missing global throws.
            "HTMLIFrameElement", "HTMLLabelElement", "HTMLOptionElement", "HTMLBodyElement",
            "HTMLHtmlElement", "HTMLStyleElement", "HTMLLinkElement", "HTMLMetaElement",
            "HTMLPreElement", "HTMLBRElement", "HTMLHRElement", "HTMLTableRowElement",
            "HTMLTableCellElement", "HTMLTableSectionElement", "HTMLFieldSetElement",
            "HTMLLegendElement", "HTMLPictureElement", "HTMLSourceElement", "HTMLVideoElement",
            "HTMLAudioElement", "HTMLMediaElement", "HTMLOptGroupElement", "HTMLDataListElement",
        })
        {
            Global(engine, name, MakeCtor(engine, name, NewProto(engine, htmlElementProto)));
        }
    }

    // =====================================================================
    //                      classList / dataset / style
    // =====================================================================
    private static JsObject BuildDomTokenList(JintBackendContext ctx, Element element)
    {
        var engine = ctx.Engine;
        var obj = new JsObject(engine);
        var cl = element.ClassList;

        Accessor(ctx, obj, "length", (_, _) => JintInterop.Num(cl.Count));
        Accessor(ctx, obj, "value",
            (_, _) => JintInterop.Str(element.GetAttribute("class") ?? ""),
            (_, args) => { element.SetAttribute("class", args.Length > 0 ? Str(args[0]) : ""); return JsValue.Undefined; });

        Method(ctx, obj, "contains", (_, args) =>
        {
            if (args.Length == 0) return JsBoolean.False;
            try { return JintInterop.Bool(cl.Contains(Str(args[0]))); } catch { return JsBoolean.False; }
        }, 1);
        Method(ctx, obj, "add", (_, args) =>
        {
            foreach (var a in args) { try { cl.Add(Str(a)); } catch { /* invalid token */ } }
            return JsValue.Undefined;
        }, 0);
        Method(ctx, obj, "remove", (_, args) =>
        {
            foreach (var a in args) { try { cl.Remove(Str(a)); } catch { /* invalid token */ } }
            return JsValue.Undefined;
        }, 0);
        Method(ctx, obj, "toggle", (_, args) =>
        {
            if (args.Length == 0) return JsBoolean.False;
            var token = Str(args[0]);
            bool result;
            if (args.Length > 1 && !args[1].IsUndefined())
            {
                if (Bool(args[1])) { try { cl.Add(token); } catch { /* invalid */ } result = true; }
                else { try { cl.Remove(token); } catch { /* invalid */ } result = false; }
            }
            else if (cl.Contains(token)) { try { cl.Remove(token); } catch { /* invalid */ } result = false; }
            else { try { cl.Add(token); } catch { /* invalid */ } result = true; }
            return JintInterop.Bool(result);
        }, 1);
        Method(ctx, obj, "replace", (_, args) =>
        {
            if (args.Length < 2) return JsBoolean.False;
            var oldT = Str(args[0]);
            var newT = Str(args[1]);
            try
            {
                if (!cl.Contains(oldT)) return JsBoolean.False;
                cl.Remove(oldT);
                cl.Add(newT);
                return JsBoolean.True;
            }
            catch { return JsBoolean.False; }
        }, 2);
        Method(ctx, obj, "item", (_, args) =>
        {
            if (args.Length == 0) return JsValue.Null;
            var idx = (int)Num(args[0]);
            return idx >= 0 && idx < cl.Count ? JintInterop.Str(cl[idx]) : JsValue.Null;
        }, 1);
        Method(ctx, obj, "toString", (_, _) => JintInterop.Str(element.GetAttribute("class") ?? ""), 0);
        return obj;
    }

    private static JintDatasetObject BuildDataset(JintBackendContext ctx, Element element)
        => new(ctx.Engine, element);

    private static JsObject BuildStyle(JintBackendContext ctx, Element element)
    {
        var engine = ctx.Engine;
        var obj = new JsObject(engine);

        Accessor(ctx, obj, "cssText",
            (_, _) => JintInterop.Str(element.GetAttribute("style") ?? ""),
            (_, args) =>
            {
                var v = args.Length > 0 ? Str(args[0]) : "";
                if (string.IsNullOrWhiteSpace(v)) element.RemoveAttribute("style");
                else element.SetAttribute("style", v);
                return JsValue.Undefined;
            });
        Method(ctx, obj, "getPropertyValue", (_, args) =>
            args.Length == 0 ? JintInterop.Str("")
                : JintInterop.Str(ReadStyleProp(element, Str(args[0]).Trim().ToLowerInvariant())), 1);
        Method(ctx, obj, "setProperty", (_, args) =>
        {
            if (args.Length >= 2) WriteStyleProp(element, Str(args[0]).Trim().ToLowerInvariant(), Str(args[1]).Trim());
            return JsValue.Undefined;
        }, 2);
        Method(ctx, obj, "removeProperty", (_, args) =>
        {
            if (args.Length == 0) return JintInterop.Str("");
            var prop = Str(args[0]).Trim().ToLowerInvariant();
            var old = ReadStyleProp(element, prop);
            WriteStyleProp(element, prop, null);
            return JintInterop.Str(old);
        }, 1);

        foreach (var kebab in StyleProperties)
        {
            var k = kebab;
            var camel = KebabToCamel(k);
            Accessor(ctx, obj, k,
                (_, _) => JintInterop.Str(ReadStyleProp(element, k)),
                (_, a) => { WriteStyleProp(element, k, a.Length > 0 ? Str(a[0]) : ""); return JsValue.Undefined; });
            if (camel != k)
                Accessor(ctx, obj, camel,
                    (_, _) => JintInterop.Str(ReadStyleProp(element, k)),
                    (_, a) => { WriteStyleProp(element, k, a.Length > 0 ? Str(a[0]) : ""); return JsValue.Undefined; });
        }
        return obj;
    }

    // =====================================================================
    //                          selector engine
    // =====================================================================
    // Mirrors QuerySelectorEngine.cs: delegate to the full Starling.Css selector
    // parser + matcher so the full grammar works; invalid selectors throw JS
    // SyntaxError.
    private static Element? SelectorFirst(global::Jint.Engine engine, Node root, string selector)
    {
        var list = ParseSelector(engine, selector);
        var ctx = SelectorContext(root);
        foreach (var e in root.DescendantElements())
            if (SelectorMatcher.Matches(list, e, ctx)) return e;
        return null;
    }

    private static List<Element> SelectorAll(global::Jint.Engine engine, Node root, string selector)
    {
        var list = ParseSelector(engine, selector);
        var ctx = SelectorContext(root);
        var result = new List<Element>();
        foreach (var e in root.DescendantElements())
            if (SelectorMatcher.Matches(list, e, ctx)) result.Add(e);
        return result;
    }

    private static bool SelectorMatches(global::Jint.Engine engine, Element element, string selector)
        => SelectorMatcher.Matches(ParseSelector(engine, selector), element, SelectorContext(element));

    private static Element? SelectorClosest(global::Jint.Engine engine, Element element, string selector)
    {
        var list = ParseSelector(engine, selector);
        var ctx = SelectorContext(element);
        for (Node? n = element; n is not null; n = n.ParentNode)
            if (n is Element e && SelectorMatcher.Matches(list, e, ctx)) return e;
        return null;
    }

    private static SelectorMatchContext SelectorContext(Node root)
        => root is Element scope
            ? new SelectorMatchContext { ScopeElement = scope }
            : SelectorMatchContext.Default;

    private static SelectorList ParseSelector(global::Jint.Engine engine, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw SyntaxError(engine, "The selector is empty.");
        SelectorList list;
        try { list = SelectorParser.ParseSelectorList(raw); }
        catch (FormatException ex) { throw SyntaxError(engine, $"'{raw}' is not a valid selector: {ex.Message}"); }
        if (list.Selectors.Count == 0) throw SyntaxError(engine, $"'{raw}' is not a valid selector.");
        return list;
    }

    // =====================================================================
    //                         DOM tree helpers
    // =====================================================================
    private static DocumentFragment ParseFragment(Element context, string markup)
    {
        var ownerDocument = context.OwnerDocument ?? new Document();
        return HtmlTreeBuilder.ParseFragment(markup, context, ownerDocument);
    }

    private static void InsertAdjacent(JintDomWrapper w, Node parent, JsValue arg, Node? before)
    {
        Node child;
        if (arg.IsString())
        {
            var doc = parent.OwnerDocument ?? parent as Document;
            var s = arg.AsString();
            child = doc?.CreateTextNode(s) ?? new Text(s);
        }
        else if (w.UnwrapNode(arg) is { } n)
        {
            child = n;
        }
        else
        {
            var s = Str(arg);
            var doc = parent.OwnerDocument ?? parent as Document;
            child = doc?.CreateTextNode(s) ?? new Text(s);
        }
        parent.InsertBefore(child, before);
    }

    private static Node CloneNode(Node source, bool deep)
    {
        var doc = source.OwnerDocument ?? source as Document;
        Node clone = source switch
        {
            Element el when doc is not null => CloneElement(doc, el),
            Element el => CloneElementDetached(el),
            Text txt => doc?.CreateTextNode(txt.Data) ?? new Text(txt.Data),
            Comment c => doc?.CreateComment(c.Data) ?? new Comment(c.Data),
            DocumentFragment when doc is not null => doc.CreateDocumentFragment(),
            DocumentFragment => new DocumentFragment(),
            _ => source,
        };
        if (deep && !ReferenceEquals(clone, source))
            for (var child = source.FirstChild; child is not null; child = child.NextSibling)
                clone.AppendChild(CloneNode(child, deep: true));
        return clone;
    }

    private static Element CloneElement(Document doc, Element el)
    {
        var clone = doc.CreateElement(el.TagName, el.Namespace);
        foreach (var attr in el.Attributes) clone.SetAttribute(attr.Name, attr.Value);
        return clone;
    }

    private static Element CloneElementDetached(Element el)
    {
        var clone = new Element(el.TagName, el.Namespace);
        foreach (var attr in el.Attributes) clone.SetAttribute(attr.Name, attr.Value);
        return clone;
    }

    private static void NormalizeNode(Node n)
    {
        var child = n.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            if (child is Text t)
            {
                if (string.IsNullOrEmpty(t.Data)) n.RemoveChild(child);
                else
                    while (next is Text t2)
                    {
                        var following = t2.NextSibling;
                        t.Data += t2.Data;
                        n.RemoveChild(t2);
                        next = following;
                    }
            }
            else NormalizeNode(child);
            child = next;
        }
    }

    private static bool IsConnected(Node? n)
    {
        for (var cur = n; cur is not null; cur = cur.ParentNode)
            if (cur is Document) return true;
        return false;
    }

    private static string NodeName(Node n) => n switch
    {
        Element e => e.TagName.ToUpperInvariant(),
        Document => "#document",
        Text => "#text",
        Comment => "#comment",
        DocumentFragment => "#document-fragment",
        _ => n.NodeName,
    };

    // ---- inline style attribute parse/serialize -------------------------
    private static string ReadStyleProp(Element element, string kebabProp)
    {
        var style = element.GetAttribute("style");
        if (string.IsNullOrEmpty(style)) return "";
        foreach (var decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            if (decl[..colon].Trim().Equals(kebabProp, StringComparison.OrdinalIgnoreCase))
                return decl[(colon + 1)..].Trim();
        }
        return "";
    }

    private static void WriteStyleProp(Element element, string kebabProp, string? value)
    {
        var style = element.GetAttribute("style") ?? "";
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            pairs[decl[..colon].Trim().ToLowerInvariant()] = decl[(colon + 1)..].Trim();
        }
        if (string.IsNullOrEmpty(value)) pairs.Remove(kebabProp);
        else pairs[kebabProp] = value;
        if (pairs.Count == 0) element.RemoveAttribute("style");
        else element.SetAttribute("style", string.Join("; ", pairs.Select(p => $"{p.Key}: {p.Value}")));
    }

    private static string KebabToCamel(string kebab)
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

    // =====================================================================
    //                         Jint plumbing helpers
    // =====================================================================
    private static JsObject NewProto(global::Jint.Engine engine, ObjectInstance? parent)
    {
        var proto = new JsObject(engine);
        if (parent is not null) proto.Prototype = parent;
        return proto;
    }

    private static void Accessor(JintBackendContext ctx, ObjectInstance proto, string name,
        Func<JsValue, JsValue[], JsValue> getter, Func<JsValue, JsValue[], JsValue>? setter = null)
        => JintInterop.DefineAccessor(ctx.Engine, proto, name, getter, setter);

    private static void Method(JintBackendContext ctx, ObjectInstance proto, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
        => JintInterop.DefineMethod(ctx.Engine, proto, name, body, length);

    private static JsValue Cached(ObjectInstance wrapper, string key, Func<ObjectInstance> build)
    {
        var existing = wrapper.Get(key);
        if (!existing.IsUndefined()) return existing;
        var obj = build();
        // Hidden cache slot: non-enumerable so it doesn't leak into for-in.
        JintInterop.DefineDataProp(wrapper, key, obj, writable: true, enumerable: false, configurable: true);
        return obj;
    }

    /// <summary>Build a DOMImplementation JS object (DOM §4.5). Exposes
    /// <c>createHTMLDocument([title])</c>, <c>createDocumentFragment()</c>, and
    /// <c>hasFeature()</c> (always true). The returned document is wrapped with
    /// the Document prototype so all Document methods work on it. Mirrors the
    /// Starling backend's <c>NodeBindings.BuildDomImplementation</c>.</summary>
    private static JsObject BuildDomImplementation(JintBackendContext ctx)
    {
        var engine = ctx.Engine;
        var w = ctx.Wrappers;
        var impl = new JsObject(engine);

        JintInterop.DefineMethod(engine, impl, "createHTMLDocument", (_, args) =>
        {
            // title arg: absent → null (no <title>); present (even "") → create one.
            string? title = args.Length > 0 && !args[0].IsUndefined() ? Str(args[0]) : null;
            return w.Wrap(Document.CreateHtmlDocument(title));
        }, 1);

        JintInterop.DefineMethod(engine, impl, "createDocumentFragment",
            (_, _) => w.Wrap(new Document().CreateDocumentFragment()), 0);

        JintInterop.DefineMethod(engine, impl, "hasFeature", (_, _) => JsBoolean.True, 0);

        return impl;
    }

    private static JsArray Array(global::Jint.Engine engine, IReadOnlyList<JsValue> items)
        => new(engine, items as JsValue[] ?? items.ToArray());

    private static JsArray EmptyArray(global::Jint.Engine engine) => new(engine, System.Array.Empty<JsValue>());

    // =====================================================================
    //                          layout readback
    // =====================================================================
    /// <summary>The session's layout host, or null when none was supplied (bare
    /// unit-test contexts). Strongly typed end-to-end since J7 moved
    /// <see cref="ILayoutHost"/> into the engine-neutral hosting seam.</summary>
    private static ILayoutHost? LayoutHost(JintBackendContext ctx) => ctx.LayoutHost;

    /// <summary>If <paramref name="e"/> is a <c>&lt;script&gt;</c> and the mutated
    /// attribute is <c>src</c> with a non-empty value, notify the session (via the
    /// J6b <see cref="JintBackendContext.OnScriptSrcSet"/> hook) so it can run
    /// HTML §4.12.1 "prepare a script" (fetch + execute + fire load/error).
    /// Scoped strictly to script-element <c>src</c>; every other attribute write
    /// is untouched. No-op when no session installed the hook (bare context).</summary>
    private static void MaybeTriggerScriptSrc(JintBackendContext ctx, Element e, string attrName)
    {
        if (!attrName.Equals("src", StringComparison.OrdinalIgnoreCase)) return;
        if (!e.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(e.GetAttribute("src"))) return;
        ctx.OnScriptSrcSet?.Invoke(e);
    }

    private static JsValue ReadOffsetMetric(JintBackendContext ctx, JsValue thisV, Func<OffsetMetrics, double> select)
    {
        if (ctx.Wrappers.UnwrapElement(thisV) is { } e && LayoutHost(ctx) is { } host &&
            host.TryGetOffsetMetrics(e, out var m))
        {
            return JintInterop.Num(select(m));
        }
        return JintInterop.Num(0);
    }

    /// <summary>Build a DOMRect-shaped JS object from a snapshot rect. A duck-
    /// compatible bag (x/y/width/height/top/right/bottom/left + toJSON) covering
    /// the read-only paths every layout-readback test touches.</summary>
    private static JsObject BuildDomRect(global::Jint.Engine engine, LayoutRect rect)
    {
        var o = new JsObject(engine);
        JintInterop.DefineDataProp(o, "x", JintInterop.Num(rect.X));
        JintInterop.DefineDataProp(o, "y", JintInterop.Num(rect.Y));
        JintInterop.DefineDataProp(o, "width", JintInterop.Num(rect.Width));
        JintInterop.DefineDataProp(o, "height", JintInterop.Num(rect.Height));
        JintInterop.DefineDataProp(o, "top", JintInterop.Num(rect.Top));
        JintInterop.DefineDataProp(o, "right", JintInterop.Num(rect.Right));
        JintInterop.DefineDataProp(o, "bottom", JintInterop.Num(rect.Bottom));
        JintInterop.DefineDataProp(o, "left", JintInterop.Num(rect.Left));
        JintInterop.DefineMethod(engine, o, "toJSON", (self, _) =>
        {
            var json = new JsObject(engine);
            if (self is ObjectInstance src)
                foreach (var k in new[] { "x", "y", "width", "height", "top", "right", "bottom", "left" })
                    JintInterop.DefineDataProp(json, k, src.Get(k));
            return json;
        }, 0);
        return o;
    }

    // HTML §4.8.5 — `new Image(width?, height?)` constructs a detached
    // HTMLImageElement (an <img> not yet in any document). Pages use it to
    // preload sprites and to feature-test image support; a missing `Image`
    // global throws ReferenceError and aborts the surrounding init (McMaster's
    // Shell_InitContent_Loaded). Jint's ClrFunction isn't constructable, so we
    // install a real JS function whose body returns a freshly wrapped <img>:
    // a constructor that returns an object yields that object from `new`.
    private const string ImageHook = "__starlingCreateImage";

    private static void InstallImageConstructor(JintBackendContext ctx)
    {
        var engine = ctx.Engine;
        JintInterop.DefineDataProp(engine.Global, ImageHook,
            new ClrFunction(engine, ImageHook, (_, a) =>
            {
                var img = ctx.Document.CreateElement("img");
                if (a.Length > 0 && !a[0].IsUndefined())
                    img.SetAttribute("width", ((int)Num(a[0])).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (a.Length > 1 && !a[1].IsUndefined())
                    img.SetAttribute("height", ((int)Num(a[1])).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return ctx.Wrappers.Wrap(img);
            }, 2),
            writable: false, enumerable: false, configurable: true);

        var ctor = (ObjectInstance)engine.Evaluate(
            $"(function Image(width, height){{ return {ImageHook}(width, height); }})");
        Global(engine, "Image", ctor);
    }

    private static ClrFunction MakeCtor(global::Jint.Engine engine, string name, ObjectInstance proto)
    {
        var ctor = new ClrFunction(engine, name,
            (_, _) => throw TypeError(engine, "Illegal constructor"), 0, PropertyFlag.Configurable);
        // ClrFunction shadows a data `prototype` set via FastSetProperty, so the
        // OrdinaryHasInstance path reads `undefined`; assigning via Set installs a
        // readable own slot so `node instanceof Element` resolves correctly.
        ctor.Set("prototype", proto);
        JintInterop.DefineDataProp(proto, "constructor", ctor, writable: true, enumerable: false, configurable: true);
        return ctor;
    }

    private static void Global(global::Jint.Engine engine, string name, JsValue value)
        => JintInterop.DefineDataProp(engine.Global, name, value, writable: true, enumerable: false, configurable: true);

    // Elements that carry a `value` IDL attribute (HTML §4.10). `select` is
    // included even though its real value reflects the selected option — for a
    // statically-rendered page reflecting the value attribute is close enough
    // and, crucially, never undefined.
    private static bool IsFormControl(Element e) => e.LocalName.ToLowerInvariant() switch
    {
        "input" or "textarea" or "select" or "option" or "button"
            or "li" or "meter" or "progress" or "param" or "data" => true,
        _ => false,
    };

    private static bool IsTextArea(Element e)
        => string.Equals(e.LocalName, "textarea", StringComparison.OrdinalIgnoreCase);

    private static void DefineNodeConstants(ObjectInstance target)
    {
        void C(string n, int v) => JintInterop.DefineDataProp(target, n, JintInterop.Num(v),
            writable: false, enumerable: true, configurable: false);
        C("ELEMENT_NODE", 1);
        C("ATTRIBUTE_NODE", 2);
        C("TEXT_NODE", 3);
        C("CDATA_SECTION_NODE", 4);
        C("PROCESSING_INSTRUCTION_NODE", 7);
        C("COMMENT_NODE", 8);
        C("DOCUMENT_NODE", 9);
        C("DOCUMENT_TYPE_NODE", 10);
        C("DOCUMENT_FRAGMENT_NODE", 11);
    }

    private static JavaScriptException TypeError(global::Jint.Engine engine, string message)
        => new(engine.Intrinsics.TypeError.Construct(message));

    private static JavaScriptException SyntaxError(global::Jint.Engine engine, string message)
        => new(engine.Construct("SyntaxError", new JsValue[] { JintInterop.Str(message) }));

    // ---- value coercion -------------------------------------------------
    private static JsValue Arg(JsValue[] args, int i) => i < args.Length ? args[i] : JsValue.Undefined;
    private static string Str(JsValue v) => TypeConverter.ToString(v);
    private static bool Bool(JsValue v) => TypeConverter.ToBoolean(v);
    private static double Num(JsValue v) => TypeConverter.ToNumber(v);

    // Comprehensive list of CSS properties exposed via element.style.X (mirrors
    // the Starling.Js binding's set so JS that touches inline styles works).
    private static readonly string[] StyleProperties =
    [
        "display", "visibility", "opacity", "position", "top", "right", "bottom", "left",
        "z-index", "width", "height", "min-width", "min-height", "max-width", "max-height",
        "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
        "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
        "border", "border-width", "border-style", "border-color",
        "border-top", "border-right", "border-bottom", "border-left",
        "border-radius", "box-shadow", "outline",
        "background", "background-color", "background-image", "background-clip",
        "color", "font-size", "font-family", "font-weight", "font-style", "line-height",
        "text-align", "text-decoration", "text-transform", "white-space",
        "flex", "flex-direction", "flex-wrap", "justify-content", "align-items", "align-self",
        "gap", "row-gap", "column-gap",
        "overflow", "overflow-x", "overflow-y",
        "cursor", "pointer-events",
        "transform", "transition", "animation",
        "float", "clear",
        "filter", "backdrop-filter",
        "zoom",
        "content", "list-style", "list-style-type",
    ];
}

/// <summary>
/// Exotic JS object backing <c>element.dataset</c> (HTML 2.7.3 DOMStringMap),
/// mirroring <c>Starling.Bindings.JsDatasetObject</c> but over Jint's
/// <see cref="ObjectInstance"/> property model. Property reads/writes delegate to
/// the element's <c>data-*</c> attributes; camelCase property maps to <c>data-</c>
/// + kebab-case. (<see cref="JsObject"/> is sealed, so this derives from the
/// overridable <see cref="ObjectInstance"/> base.)
/// </summary>
internal sealed class JintDatasetObject : ObjectInstance
{
    private readonly Element _element;

    public JintDatasetObject(global::Jint.Engine engine, Element element) : base(engine)
        => _element = element;

    private static string PropToAttr(string name)
    {
        var sb = new System.Text.StringBuilder("data-");
        foreach (var c in name)
        {
            if (char.IsUpper(c)) { sb.Append('-'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string AttrToProp(string attr)
    {
        if (!attr.StartsWith("data-", StringComparison.OrdinalIgnoreCase)) return attr;
        var kebab = attr[5..];
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

    public override JsValue Get(JsValue property, JsValue receiver)
    {
        if (property.IsString())
        {
            var attr = _element.GetAttribute(PropToAttr(property.AsString()));
            if (attr is not null) return JintInterop.Str(attr);
        }
        return base.Get(property, receiver);
    }

    public override bool Set(JsValue property, JsValue value, JsValue receiver)
    {
        if (property.IsString())
        {
            _element.SetAttribute(PropToAttr(property.AsString()), TypeConverter.ToString(value));
            return true;
        }
        return base.Set(property, value, receiver);
    }

    public override bool HasProperty(JsValue property)
    {
        if (property.IsString() && _element.HasAttribute(PropToAttr(property.AsString()))) return true;
        return base.HasProperty(property);
    }

    public override PropertyDescriptor GetOwnProperty(JsValue property)
    {
        if (property.IsString())
        {
            var attr = _element.GetAttribute(PropToAttr(property.AsString()));
            if (attr is not null)
                return new PropertyDescriptor(JintInterop.Str(attr), writable: true, enumerable: true, configurable: true);
        }
        return base.GetOwnProperty(property);
    }

    public override bool Delete(JsValue property)
    {
        if (property.IsString())
        {
            var attr = PropToAttr(property.AsString());
            if (_element.HasAttribute(attr)) { _element.RemoveAttribute(attr); return true; }
        }
        return base.Delete(property);
    }

    public override List<JsValue> GetOwnPropertyKeys(Types types = Types.String | Types.Symbol)
    {
        var keys = new List<JsValue>();
        if ((types & Types.String) != 0)
            foreach (var a in _element.Attributes)
                if (a.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                    keys.Add(JintInterop.Str(AttrToProp(a.Name)));
        keys.AddRange(base.GetOwnPropertyKeys(types));
        return keys;
    }
}
