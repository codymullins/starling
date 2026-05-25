using Starling.Dom;
using Starling.Html;
using Starling.Html.TreeBuilder;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// B5-1 — Installs the Node / Element / Document JS prototypes onto a realm.
/// EventTargetBinding.Install must have run first (these prototypes inherit
/// from EventTargetPrototype).
/// </summary>
/// <remarks>
/// <para><b>Selector grammar:</b> <c>querySelector</c> / <c>querySelectorAll</c>
/// / <c>matches</c> / <c>closest</c> delegate to the full <c>Starling.Css</c>
/// selector engine (<see cref="QuerySelectorEngine"/>), so compound selectors,
/// combinators, attribute selectors, <c>:nth-*</c>, and <c>:is/:where/:not</c>
/// all work. An unparseable selector throws a JS <c>SyntaxError</c>.</para>
/// <para><b>innerHTML / outerHTML / insertAdjacentHTML:</b> these go through the
/// real <c>Starling.Html</c> parser and serializer.
/// <see cref="HtmlTreeBuilder.ParseFragment"/> runs the HTML fragment parsing
/// algorithm (§13.4) against the element as context; <see cref="HtmlSerializer"/>
/// renders the tree back to markup (§13.3). Adding the <c>Starling.Html</c>
/// project reference introduced no cycle — nothing in that project's transitive
/// dependency graph (Common / Dom / Url) refers back to Bindings — so the simpler
/// direct-reference option was taken over a parser-capability seam.</para>
/// </remarks>
public static class NodeBindings
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (realm.NodePrototype is not null) return; // idempotent
        if (realm.EventTargetPrototype is null)
            throw new InvalidOperationException("EventTargetBinding.Install must run before NodeBindings.Install");

        InstallNode(realm);
        InstallElement(realm);
        InstallDocument(realm);
    }

    // =====================================================================
    //                              Node
    // =====================================================================
    private static void InstallNode(JsRealm realm)
    {
        var nodeProto = new JsObject(realm.EventTargetPrototype);
        realm.NodePrototype = nodeProto;

        EventTargetBinding.DefineAccessor(realm, nodeProto, "nodeType",
            (thisV, _) => DomWrappers.UnwrapNode(thisV) is { } n ? JsValue.Number((int)NodeTypeFromKind(n.Kind)) : JsValue.Number(0));
        EventTargetBinding.DefineAccessor(realm, nodeProto, "nodeName",
            (thisV, _) => DomWrappers.UnwrapNode(thisV) is { } n ? JsValue.String(NormalizeNodeName(n)) : JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, nodeProto, "nodeValue",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.NodeValue is { } s ? JsValue.String(s) : JsValue.Null,
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapNode(thisV) is { } n && args.Length > 0)
                    n.NodeValue = args[0].IsNullish ? null : JsValue.ToStringValue(args[0]);
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, nodeProto, "textContent",
            (thisV, _) => DomWrappers.UnwrapNode(thisV) is { } n ? JsValue.String(n.TextContent) : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapNode(thisV) is { } n)
                    n.TextContent = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, nodeProto, "parentNode",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.ParentNode is { } p
                ? JsValue.Object(DomWrappers.Wrap(realm, p)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "parentElement",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.ParentNode is Element pe
                ? JsValue.Object(DomWrappers.Wrap(realm, pe)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "firstChild",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.FirstChild is { } c
                ? JsValue.Object(DomWrappers.Wrap(realm, c)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "lastChild",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.LastChild is { } c
                ? JsValue.Object(DomWrappers.Wrap(realm, c)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "nextSibling",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.NextSibling is { } c
                ? JsValue.Object(DomWrappers.Wrap(realm, c)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "previousSibling",
            (thisV, _) => DomWrappers.UnwrapNode(thisV)?.PreviousSibling is { } c
                ? JsValue.Object(DomWrappers.Wrap(realm, c)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, nodeProto, "childNodes", (thisV, _) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } n) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            foreach (var c in n.ChildNodes) items.Add(JsValue.Object(DomWrappers.Wrap(realm, c)));
            return MakeArray(realm, items);
        });
        EventTargetBinding.DefineAccessor(realm, nodeProto, "ownerDocument", (thisV, _) =>
        {
            var n = DomWrappers.UnwrapNode(thisV);
            if (n is Document) return JsValue.Null; // documents have no owner
            return n?.OwnerDocument is { } d ? JsValue.Object(DomWrappers.Wrap(realm, d)) : JsValue.Null;
        });

        EventTargetBinding.DefineMethod(realm, nodeProto, "appendChild", (thisV, args) =>
        {
            var parent = DomWrappers.UnwrapNode(thisV);
            var child = args.Length > 0 ? DomWrappers.UnwrapNode(args[0]) : null;
            if (parent is null || child is null)
                throw new JsThrow(realm.NewTypeError("appendChild requires a Node argument"));
            parent.AppendChild(child);
            return args[0];
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "removeChild", (thisV, args) =>
        {
            var parent = DomWrappers.UnwrapNode(thisV);
            var child = args.Length > 0 ? DomWrappers.UnwrapNode(args[0]) : null;
            if (parent is null || child is null)
                throw new JsThrow(realm.NewTypeError("removeChild requires a Node argument"));
            parent.RemoveChild(child);
            return args[0];
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "insertBefore", (thisV, args) =>
        {
            var parent = DomWrappers.UnwrapNode(thisV);
            var child = args.Length > 0 ? DomWrappers.UnwrapNode(args[0]) : null;
            var refChild = args.Length > 1 ? DomWrappers.UnwrapNode(args[1]) : null;
            if (parent is null || child is null)
                throw new JsThrow(realm.NewTypeError("insertBefore requires a Node argument"));
            parent.InsertBefore(child, refChild);
            return args[0];
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, nodeProto, "replaceChild", (thisV, args) =>
        {
            var parent = DomWrappers.UnwrapNode(thisV);
            var newChild = args.Length > 0 ? DomWrappers.UnwrapNode(args[0]) : null;
            var oldChild = args.Length > 1 ? DomWrappers.UnwrapNode(args[1]) : null;
            if (parent is null || newChild is null || oldChild is null)
                throw new JsThrow(realm.NewTypeError("replaceChild requires two Node arguments"));
            parent.ReplaceChild(newChild, oldChild);
            return args[1];
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, nodeProto, "hasChildNodes",
            (thisV, _) => JsValue.Boolean(DomWrappers.UnwrapNode(thisV)?.FirstChild is not null), length: 0);
        // DOM §4.4.4 — cloneNode. Defined on Node so all node types can use it.
        EventTargetBinding.DefineMethod(realm, nodeProto, "cloneNode", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } n) return JsValue.Null;
            var deep = args.Length > 0 && JsValue.ToBoolean(args[0]);
            var clone = CloneNode(realm, n, deep);
            return JsValue.Object(DomWrappers.Wrap(realm, clone));
        }, length: 0);
        // DOM §4.4 — normalize: merge adjacent text nodes, remove empty texts.
        EventTargetBinding.DefineMethod(realm, nodeProto, "normalize", (thisV, _) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { } n)
                NormalizeNode(n);
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, nodeProto, "contains", (thisV, args) =>
        {
            var self = DomWrappers.UnwrapNode(thisV);
            if (self is null || args.Length == 0) return JsValue.False;
            var other = DomWrappers.UnwrapNode(args[0]);
            if (other is null) return JsValue.False;
            for (var n = other; n is not null; n = n.ParentNode)
                if (ReferenceEquals(n, self)) return JsValue.True;
            return JsValue.False;
        }, length: 1);

        // ---- ChildNode + ParentNode mixins (DOM §4.2.1). Defined on Node.prototype
        // so Element/CharacterData/etc. inherit them; node-or-string args coerce to
        // Text nodes. Built on the host insert/remove primitives.
        EventTargetBinding.DefineMethod(realm, nodeProto, "before", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { ParentNode: { } parent } node)
                foreach (var n in CoerceNodes(realm, node, args)) parent.InsertBefore(n, node);
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "after", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { ParentNode: { } parent } node)
            {
                var refNode = node.NextSibling;
                foreach (var n in CoerceNodes(realm, node, args)) parent.InsertBefore(n, refNode);
            }
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "replaceWith", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { ParentNode: { } parent } node)
            {
                var refNode = node.NextSibling;
                var nodes = CoerceNodes(realm, node, args);
                node.RemoveFromParent();
                foreach (var n in nodes) parent.InsertBefore(n, refNode);
            }
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "remove", (thisV, _) =>
        {
            DomWrappers.UnwrapNode(thisV)?.RemoveFromParent();
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, nodeProto, "append", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { } node)
                foreach (var n in CoerceNodes(realm, node, args)) node.AppendChild(n);
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "prepend", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { } node)
            {
                var refNode = node.FirstChild;
                foreach (var n in CoerceNodes(realm, node, args)) node.InsertBefore(n, refNode);
            }
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "replaceChildren", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is { } node)
            {
                var nodes = CoerceNodes(realm, node, args);
                while (node.FirstChild is { } c) c.RemoveFromParent();
                foreach (var n in nodes) node.AppendChild(n);
            }
            return JsValue.Undefined;
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, nodeProto, "lookupNamespaceURI", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } n) return JsValue.Null;
            var r = n.LookupNamespaceURI(args.Length > 0 && !args[0].IsNullish ? JsValue.ToStringValue(args[0]) : null);
            return r is null ? JsValue.Null : JsValue.String(r);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "isDefaultNamespace", (thisV, args) =>
            DomWrappers.UnwrapNode(thisV) is { } n
                ? JsValue.Boolean(n.IsDefaultNamespace(args.Length > 0 && !args[0].IsNullish ? JsValue.ToStringValue(args[0]) : null))
                : JsValue.False, length: 1);
        EventTargetBinding.DefineMethod(realm, nodeProto, "lookupPrefix", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } n) return JsValue.Null;
            var r = n.LookupPrefix(args.Length > 0 && !args[0].IsNullish ? JsValue.ToStringValue(args[0]) : null);
            return r is null ? JsValue.Null : JsValue.String(r);
        }, length: 1);

        var nodeCtor = new JsNativeFunction(realm, "Node", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Illegal constructor")), isConstructor: false);
        nodeCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(nodeProto), writable: false, enumerable: false, configurable: false));
        nodeProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(nodeCtor), writable: true, enumerable: false, configurable: true));
        realm.NodeConstructor = nodeCtor;
        realm.GlobalObject.DefineOwnProperty("Node",
            PropertyDescriptor.Data(JsValue.Object(nodeCtor), writable: true, enumerable: false, configurable: true));
    }

    // =====================================================================
    //                             Element
    // =====================================================================
    private static void InstallElement(JsRealm realm)
    {
        var elProto = new JsObject(realm.NodePrototype);
        realm.ElementPrototype = elProto;

        EventTargetBinding.DefineAccessor(realm, elProto, "tagName",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.TagName.ToUpperInvariant()) : JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, elProto, "localName",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.LocalName) : JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, elProto, "id",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.Id) : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                    e.Id = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, elProto, "className",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.GetAttribute("class") ?? "") : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                    e.SetAttribute("class", args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
                return JsValue.Undefined;
            });
        // HTMLInputElement / HTMLTextAreaElement `value` IDL attribute. Reads
        // the live value (typed text or a prior assignment), falling back to the
        // `value` content attribute as the initial value. Writing updates the
        // live value so layout re-renders the field with the new text.
        EventTargetBinding.DefineAccessor(realm, elProto, "value",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e
                ? JsValue.String(e.InputValue ?? e.GetAttribute("value") ?? "") : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                    e.InputValue = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                return JsValue.Undefined;
            });
        // HTMLElement.focus() / .blur() — move the document focus. The shell
        // reads document.FocusedElement to drive the caret and :focus styling.
        EventTargetBinding.DefineMethod(realm, elProto, "focus", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && e.OwnerDocument is { } d)
                d.FocusedElement = e;
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "blur", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && e.OwnerDocument is { } d
                && ReferenceEquals(d.FocusedElement, e))
                d.FocusedElement = null;
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineAccessor(realm, elProto, "innerHTML",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e
                ? JsValue.String(HtmlSerializer.SerializeChildren(e)) : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                {
                    var markup = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                    var fragment = ParseFragment(e, markup);
                    // Replace all existing children with the parsed nodes.
                    while (e.FirstChild is not null) e.RemoveChild(e.FirstChild);
                    e.AppendChild(fragment);
                }
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, elProto, "outerHTML",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e
                ? JsValue.String(HtmlSerializer.SerializeNode(e)) : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Undefined;
                var parent = e.ParentNode;
                if (parent is null)
                    throw new JsThrow(realm.NewTypeError("Cannot set outerHTML on an element with no parent"));
                // Parse the markup in the context of the element's parent (or the
                // element itself if the parent is the document), then swap it in.
                var context = parent as Element ?? e;
                var markup = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                var fragment = ParseFragment(context, markup);
                parent.InsertBefore(fragment, e);
                parent.RemoveChild(e);
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineAccessor(realm, elProto, "children", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element child) items.Add(JsValue.Object(DomWrappers.Wrap(realm, child)));
            return MakeArray(realm, items);
        });
        EventTargetBinding.DefineAccessor(realm, elProto, "firstElementChild", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element c) return JsValue.Object(DomWrappers.Wrap(realm, c));
            return JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, elProto, "lastElementChild", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            for (var n = e.LastChild; n is not null; n = n.PreviousSibling)
                if (n is Element c) return JsValue.Object(DomWrappers.Wrap(realm, c));
            return JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, elProto, "nextElementSibling", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            for (var n = e.NextSibling; n is not null; n = n.NextSibling)
                if (n is Element s) return JsValue.Object(DomWrappers.Wrap(realm, s));
            return JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, elProto, "previousElementSibling", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            for (var n = e.PreviousSibling; n is not null; n = n.PreviousSibling)
                if (n is Element s) return JsValue.Object(DomWrappers.Wrap(realm, s));
            return JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, elProto, "childElementCount", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Number(0);
            var count = 0;
            for (var n = e.FirstChild; n is not null; n = n.NextSibling)
                if (n is Element) count++;
            return JsValue.Number(count);
        });

        EventTargetBinding.DefineMethod(realm, elProto, "getAttribute", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return JsValue.Null;
            var v = e.GetAttribute(JsValue.ToStringValue(args[0]));
            return v is null ? JsValue.Null : JsValue.String(v);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "setAttribute", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && args.Length >= 2)
            {
                var name = JsValue.ToStringValue(args[0]);
                var value = JsValue.ToStringValue(args[1]);
                e.SetAttribute(name, value);
                // HTML §4.12.1 "prepare a script": setting `src` on a script
                // element makes it eligible to load+run. Real browsers treat a
                // parser-inserted empty <script> that later gets a src as a
                // newly-fetched external script (the deferred-bundle pattern).
                MaybeTriggerScriptSrc(realm, e, name);
            }
            return JsValue.Undefined;
        }, length: 2);

        // `script.src` IDL property — get/set the resolved-ish src attribute.
        // The setter mirrors setAttribute('src', …) and runs "prepare a script"
        // so `el.src = url` loads the same way `setAttribute` does. Defined on
        // Element.prototype but only meaningful for <script>; for non-script
        // elements it's a plain attribute round-trip.
        EventTargetBinding.DefineAccessor(realm, elProto, "src",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e
                ? JsValue.String(e.GetAttribute("src") ?? "")
                : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                {
                    var value = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                    e.SetAttribute("src", value);
                    MaybeTriggerScriptSrc(realm, e, "src");
                }
                return JsValue.Undefined;
            });
        // `script.async` IDL property. Reflects the boolean `async` content
        // attribute so `el.async = true` is observable by the engine's script
        // classification (HTML §4.12.1). A script-inserted external script
        // defaults to async; analytics snippets (ga.js/gtag) set this flag, so
        // reflecting it is what lets the engine defer them past first paint.
        EventTargetBinding.DefineAccessor(realm, elProto, "async",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e
                ? JsValue.Boolean(e.HasAttribute("async"))
                : JsValue.False,
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                {
                    if (args.Length > 0 && JsValue.ToBoolean(args[0])) e.SetAttribute("async", "");
                    else e.RemoveAttribute("async");
                }
                return JsValue.Undefined;
            });
        EventTargetBinding.DefineMethod(realm, elProto, "hasAttribute", (thisV, args) =>
            DomWrappers.UnwrapElement(thisV) is { } e && args.Length > 0
                ? JsValue.Boolean(e.HasAttribute(JsValue.ToStringValue(args[0])))
                : JsValue.False, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "removeAttribute", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && args.Length > 0)
                e.RemoveAttribute(JsValue.ToStringValue(args[0]));
            return JsValue.Undefined;
        }, length: 1);
        // ---- Namespace-aware attribute methods (DOM §4.9).
        EventTargetBinding.DefineMethod(realm, elProto, "getAttributeNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length < 2) return JsValue.Null;
            var v = e.GetAttributeNS(args[0].IsNullish ? null : JsValue.ToStringValue(args[0]), JsValue.ToStringValue(args[1]));
            return v is null ? JsValue.Null : JsValue.String(v);
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, elProto, "setAttributeNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && args.Length >= 3)
                e.SetAttributeNS(args[0].IsNullish ? null : JsValue.ToStringValue(args[0]),
                    JsValue.ToStringValue(args[1]), JsValue.ToStringValue(args[2]));
            return JsValue.Undefined;
        }, length: 3);
        EventTargetBinding.DefineMethod(realm, elProto, "hasAttributeNS", (thisV, args) =>
            DomWrappers.UnwrapElement(thisV) is { } e && args.Length >= 2
                ? JsValue.Boolean(e.HasAttributeNS(args[0].IsNullish ? null : JsValue.ToStringValue(args[0]), JsValue.ToStringValue(args[1])))
                : JsValue.False, length: 2);
        EventTargetBinding.DefineMethod(realm, elProto, "removeAttributeNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e && args.Length >= 2)
                e.RemoveAttributeNS(args[0].IsNullish ? null : JsValue.ToStringValue(args[0]), JsValue.ToStringValue(args[1]));
            return JsValue.Undefined;
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, elProto, "getElementsByTagNameNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length < 2) return MakeArray(realm, Array.Empty<JsValue>());
            var ns = args[0].IsNullish ? null : JsValue.ToStringValue(args[0]);
            var items = new List<JsValue>();
            foreach (var d in e.GetElementsByTagNameNS(ns, JsValue.ToStringValue(args[1])))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, d)));
            return MakeArray(realm, items);
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, elProto, "appendChild", (thisV, args) =>
        {
            // Inherits from Node — re-stamped here so JS-level instanceof tests
            // see the property on Element.prototype too. Reuse Node impl via lookup.
            return CallInherited(realm, thisV, args, realm.NodePrototype!, "appendChild");
        }, length: 1);

        // HTMLCanvasElement.getContext — minimal "2d" context. Real raster
        // canvas isn't implemented, but libraries (e.g. @tanstack/table) call
        // canvas.getContext("2d") only to measure text for column sizing
        // (set ctx.font, read ctx.measureText(s).width). We return a context
        // object that supports that path with an approximate metric; other
        // context types and non-canvas elements return null (spec: getContext
        // is a HTMLCanvasElement member that returns null for unknown ids).
        EventTargetBinding.DefineMethod(realm, elProto, "getContext", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e
                || !string.Equals(e.LocalName, "canvas", StringComparison.OrdinalIgnoreCase))
                return JsValue.Null;
            var kind = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            if (kind != "2d") return JsValue.Null;
            return JsValue.Object(BuildCanvas2dContext(realm));
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, elProto, "querySelector", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return JsValue.Null;
            var match = QuerySelectorEngine.First(e, JsValue.ToStringValue(args[0]), realm);
            return match is null ? JsValue.Null : JsValue.Object(DomWrappers.Wrap(realm, match));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "querySelectorAll", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            foreach (var m in QuerySelectorEngine.All(e, JsValue.ToStringValue(args[0]), realm))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, m)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "matches", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return JsValue.False;
            return JsValue.Boolean(QuerySelectorEngine.Matches(e, JsValue.ToStringValue(args[0]), realm));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "closest", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return JsValue.Null;
            var match = QuerySelectorEngine.Closest(e, JsValue.ToStringValue(args[0]), realm);
            return match is null ? JsValue.Null : JsValue.Object(DomWrappers.Wrap(realm, match));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "getElementsByTagName", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var name = JsValue.ToStringValue(args[0]);
            var items = new List<JsValue>();
            foreach (var d in e.DescendantElements())
                if (name == "*" || d.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    items.Add(JsValue.Object(DomWrappers.Wrap(realm, d)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "getElementsByClassName", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var classes = JsValue.ToStringValue(args[0])
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var items = new List<JsValue>();
            foreach (var d in e.DescendantElements())
                if (classes.Length > 0 && classes.All(d.ClassList.Contains))
                    items.Add(JsValue.Object(DomWrappers.Wrap(realm, d)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, elProto, "remove", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is { } e) e.RemoveFromParent();
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "insertAdjacentHTML", (thisV, args) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e)
                throw new JsThrow(realm.NewTypeError("insertAdjacentHTML called on a non-Element"));
            if (args.Length < 2)
                throw new JsThrow(realm.NewTypeError("insertAdjacentHTML requires (position, text)"));
            var position = JsValue.ToStringValue(args[0]);
            var markup = JsValue.ToStringValue(args[1]);

            // §DOM-Parsing: beforebegin/afterend parse against the parent as
            // context; afterbegin/beforeend parse against the element itself.
            switch (position.ToLowerInvariant())
            {
                case "beforebegin":
                {
                    var parent = e.ParentNode;
                    if (parent is null) return JsValue.Undefined; // no-op per spec
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
                    var parent = e.ParentNode;
                    if (parent is null) return JsValue.Undefined; // no-op per spec
                    var context = parent as Element ?? e;
                    parent.InsertBefore(ParseFragment(context, markup), e.NextSibling);
                    break;
                }
                default:
                    throw new JsThrow(realm.NewTypeError(
                        $"insertAdjacentHTML: '{position}' is not a valid position"));
            }
            return JsValue.Undefined;
        }, length: 2);

        // ---- cloneNode(deep?) -----------------------------------------------
        // DOM §4.4.4 — shallow clone copies tag + attributes; deep clone also
        // recursively copies all descendant nodes.
        EventTargetBinding.DefineMethod(realm, elProto, "cloneNode", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } n) return JsValue.Null;
            var deep = args.Length > 0 && JsValue.ToBoolean(args[0]);
            var clone = CloneNode(realm, n, deep);
            return JsValue.Object(DomWrappers.Wrap(realm, clone));
        }, length: 0);

        // ---- before / after / prepend / append ------------------------------
        // DOM Living Standard — ParentNode / ChildNode mixins.
        // `prepend` / `append` insert at start/end of *this* (treating this as
        // the parent); `before` / `after` insert adjacent to *this* inside its
        // parent. String args become Text nodes.
        EventTargetBinding.DefineMethod(realm, elProto, "prepend", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } parent) return JsValue.Undefined;
            var refChild = parent.FirstChild;
            foreach (var arg in args)
                InsertAdjacentNode(realm, parent, arg, before: refChild);
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "append", (thisV, args) =>
        {
            if (DomWrappers.UnwrapNode(thisV) is not { } parent) return JsValue.Undefined;
            foreach (var arg in args)
                InsertAdjacentNode(realm, parent, arg, before: null);
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "before", (thisV, args) =>
        {
            var self = DomWrappers.UnwrapNode(thisV);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            foreach (var arg in args)
                InsertAdjacentNode(realm, parent, arg, before: self);
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "after", (thisV, args) =>
        {
            var self = DomWrappers.UnwrapNode(thisV);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            var refChild = self.NextSibling;
            foreach (var arg in args)
                InsertAdjacentNode(realm, parent, arg, before: refChild);
            return JsValue.Undefined;
        }, length: 0);

        // ---- replaceWith(...nodes) ------------------------------------------
        // Replaces this node with one or more nodes / strings.
        EventTargetBinding.DefineMethod(realm, elProto, "replaceWith", (thisV, args) =>
        {
            var self = DomWrappers.UnwrapNode(thisV);
            if (self?.ParentNode is not { } parent) return JsValue.Undefined;
            var refChild = self.NextSibling;
            foreach (var arg in args)
                InsertAdjacentNode(realm, parent, arg, before: refChild);
            self.RemoveFromParent();
            return JsValue.Undefined;
        }, length: 0);

        // ---- innerText getter / setter -------------------------------------
        // HTML §6.1.6.1 — for our purposes the getter returns the rendered
        // text content (same as textContent minus non-text nodes) and the
        // setter sets textContent. The full spec requires layout awareness;
        // this approximation satisfies the common read-existing-text cases.
        EventTargetBinding.DefineAccessor(realm, elProto, "innerText",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.TextContent) : JsValue.String(""),
            (thisV, args) =>
            {
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                    e.TextContent = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                return JsValue.Undefined;
            });

        // ---- dataset -------------------------------------------------------
        // HTML §2.7.3 HTMLOrSVGElement.dataset — a DOMStringMap giving access
        // to data-* attributes. Property name `el.dataset.fooBar` maps to
        // attribute `data-foo-bar` via camelCase ↔ kebab-case conversion.
        EventTargetBinding.DefineAccessor(realm, elProto, "dataset", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            var wrapper = thisV.IsObject ? thisV.AsObject : null;
            if (wrapper is null) return JsValue.Null;
            const string cacheKey = "__dataset__";
            var cached = wrapper.Get(cacheKey);
            if (!cached.IsUndefined) return cached;
            var dsObj = BuildDataset(realm, e);
            wrapper.Set(cacheKey, JsValue.Object(dsObj));
            return JsValue.Object(dsObj);
        });

        // ---- classList ------------------------------------------------------
        // DOM §7.1 DOMTokenList, exposed via Element.classList. The C# backing
        // object (DomTokenList) already implements add/remove/toggle/contains.
        // Here we build a JS-visible DOMTokenList wrapper per element. The
        // wrapper is lazily created once and cached on the element wrapper so
        // `el.classList === el.classList` holds.
        EventTargetBinding.DefineAccessor(realm, elProto, "classList", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            // Cache the DOMTokenList wrapper on the JS element wrapper object
            // so identity is stable across multiple reads.
            var wrapper = thisV.IsObject ? thisV.AsObject : null;
            if (wrapper is null) return JsValue.Null;
            const string cacheKey = "__classList__";
            var cached = wrapper.Get(cacheKey);
            if (!cached.IsUndefined) return cached;
            var clObj = BuildDomTokenList(realm, e);
            wrapper.Set(cacheKey, JsValue.Object(clObj));
            return JsValue.Object(clObj);
        });

        // ---- style ----------------------------------------------------------
        // Returns a per-element inline-style CSSStyleDeclaration-shaped object.
        // The element stores inline styles as a flat string in the `style`
        // attribute; we parse it lazily and expose individual property
        // accessors plus cssText / getPropertyValue / setProperty.
        EventTargetBinding.DefineAccessor(realm, elProto, "style", (thisV, _) =>
        {
            if (DomWrappers.UnwrapElement(thisV) is not { } e) return JsValue.Null;
            var wrapper = thisV.IsObject ? thisV.AsObject : null;
            if (wrapper is null) return JsValue.Null;
            const string cacheKey = "__style__";
            var cached = wrapper.Get(cacheKey);
            if (!cached.IsUndefined) return cached;
            var styleObj = BuildInlineStyleDecl(realm, e);
            wrapper.Set(cacheKey, JsValue.Object(styleObj));
            return JsValue.Object(styleObj);
        });

        // Layout-readback APIs — consult the realm's optional ILayoutHost
        // snapshot. With no host (e.g. JS run outside the engine pipeline)
        // they return spec-permitted zeros, matching a never-laid-out doc.
        EventTargetBinding.DefineMethod(realm, elProto, "getBoundingClientRect", (thisV, _) =>
        {
            var host = WindowBinding.LayoutHostForRealm(realm);
            if (DomWrappers.UnwrapElement(thisV) is { } e &&
                host is not null && host.TryGetBoundingClientRect(e, out var r))
            {
                return JsValue.Object(BuildDomRect(realm, r));
            }
            return JsValue.Object(BuildDomRect(realm, default));
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, elProto, "getClientRects", (thisV, _) =>
        {
            // Single-rect simplification — block boxes only return one
            // rect, which covers most layout-readback paths. Inline flows
            // that emit multiple line boxes would need the box tree walk.
            var host = WindowBinding.LayoutHostForRealm(realm);
            if (DomWrappers.UnwrapElement(thisV) is { } e &&
                host is not null && host.TryGetBoundingClientRect(e, out var r))
            {
                return MakeArray(realm, new[] { JsValue.Object(BuildDomRect(realm, r)) });
            }
            return MakeArray(realm, Array.Empty<JsValue>());
        }, length: 0);
        EventTargetBinding.DefineAccessor(realm, elProto, "offsetWidth",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetWidth));
        EventTargetBinding.DefineAccessor(realm, elProto, "offsetHeight",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetHeight));
        EventTargetBinding.DefineAccessor(realm, elProto, "offsetTop",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetTop));
        EventTargetBinding.DefineAccessor(realm, elProto, "offsetLeft",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetLeft));
        EventTargetBinding.DefineAccessor(realm, elProto, "clientWidth",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.ClientWidth));
        EventTargetBinding.DefineAccessor(realm, elProto, "clientHeight",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.ClientHeight));
        EventTargetBinding.DefineAccessor(realm, elProto, "scrollWidth",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetWidth));
        EventTargetBinding.DefineAccessor(realm, elProto, "scrollHeight",
            (thisV, _) => ReadOffsetMetric(realm, thisV, m => m.OffsetHeight));

        var elCtor = new JsNativeFunction(realm, "Element", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Illegal constructor")), isConstructor: false);
        elCtor.SetPrototypeOf(realm.NodeConstructor);
        elCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(elProto), writable: false, enumerable: false, configurable: false));
        elProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(elCtor), writable: true, enumerable: false, configurable: true));
        realm.ElementConstructor = elCtor;
        realm.GlobalObject.DefineOwnProperty("Element",
            PropertyDescriptor.Data(JsValue.Object(elCtor), writable: true, enumerable: false, configurable: true));

        // HTMLElement is just a thin alias for now (inherits from Element).
        var htmlElProto = new JsObject(elProto);
        var htmlElCtor = new JsNativeFunction(realm, "HTMLElement", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Illegal constructor")), isConstructor: false);
        htmlElCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(htmlElProto), writable: false, enumerable: false, configurable: false));
        htmlElProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(htmlElCtor), writable: true, enumerable: false, configurable: true));
        realm.GlobalObject.DefineOwnProperty("HTMLElement",
            PropertyDescriptor.Data(JsValue.Object(htmlElCtor), writable: true, enumerable: false, configurable: true));
    }

    // =====================================================================
    //                            Document
    // =====================================================================
    private static void InstallDocument(JsRealm realm)
    {
        var docProto = new JsObject(realm.NodePrototype);
        realm.DocumentPrototype = docProto;

        EventTargetBinding.DefineAccessor(realm, docProto, "documentElement", (thisV, _) =>
            DomWrappers.UnwrapDocument(thisV)?.DocumentElement is { } e
                ? JsValue.Object(DomWrappers.Wrap(realm, e)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, docProto, "body", (thisV, _) =>
            DomWrappers.UnwrapDocument(thisV)?.Body is { } b
                ? JsValue.Object(DomWrappers.Wrap(realm, b)) : JsValue.Null);
        EventTargetBinding.DefineAccessor(realm, docProto, "head", (thisV, _) =>
            DomWrappers.UnwrapDocument(thisV)?.Head is { } h
                ? JsValue.Object(DomWrappers.Wrap(realm, h)) : JsValue.Null);
        // HTML document.activeElement — the focused element, or <body> when
        // nothing has focus (never null for a rendered document), matching the
        // spec's fallback so scripts that read activeElement.tagName don't throw.
        EventTargetBinding.DefineAccessor(realm, docProto, "activeElement", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d) return JsValue.Null;
            var el = d.FocusedElement ?? d.Body;
            return el is { } ? JsValue.Object(DomWrappers.Wrap(realm, el)) : JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, docProto, "title", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d) return JsValue.String("");
            foreach (var n in d.Descendants())
                if (n is Element { LocalName: "title" } t) return JsValue.String(t.TextContent.Trim());
            return JsValue.String("");
        });
        EventTargetBinding.DefineAccessor(realm, docProto, "URL", (thisV, _) =>
            DomWrappers.UnwrapDocument(thisV) is { } d ? JsValue.String(WindowBinding.UrlFor(realm, d)) : JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, docProto, "documentURI", (thisV, _) =>
            DomWrappers.UnwrapDocument(thisV) is { } d ? JsValue.String(WindowBinding.UrlFor(realm, d)) : JsValue.String(""));
        EventTargetBinding.DefineAccessor(realm, docProto, "location", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d) return JsValue.Null;
            return JsValue.Object(WindowBinding.LocationObjectFor(realm, d));
        });
        EventTargetBinding.DefineAccessor(realm, docProto, "defaultView", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is null) return JsValue.Null;
            return JsValue.Object(realm.GlobalObject);
        });
        EventTargetBinding.DefineAccessor(realm, docProto, "readyState", (thisV, _) =>
            JsValue.String("complete"));
        // HTML §8.4.1 — document.compatMode returns "CSS1Compat" when the
        // document is in no-quirks or limited-quirks mode. jQuery 1.x reads
        // this on boot to decide the scrollSize calculation branch.
        EventTargetBinding.DefineAccessor(realm, docProto, "compatMode", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d) return JsValue.String("CSS1Compat");
            return JsValue.String(d.Mode == QuirksMode.Quirks ? "BackCompat" : "CSS1Compat");
        });
        // Page Visibility API §4 — document.hidden / visibilityState.
        // Starling has no background-tab concept; the document is always visible.
        EventTargetBinding.DefineAccessor(realm, docProto, "hidden",
            (_, _) => JsValue.False);
        EventTargetBinding.DefineAccessor(realm, docProto, "visibilityState",
            (_, _) => JsValue.String("visible"));

        // DOM §4.5 — document.implementation returns a DOMImplementation object.
        // One singleton per document is fine (cached on the wrapper with a hidden key).
        EventTargetBinding.DefineAccessor(realm, docProto, "implementation", (thisV, _) =>
        {
            var wrapper = thisV.IsObject ? thisV.AsObject : null;
            if (wrapper is null) return JsValue.Null;
            const string cacheKey = "__domImpl__";
            var cached = wrapper.Get(cacheKey);
            if (!cached.IsUndefined) return cached;
            var impl = BuildDomImplementation(realm);
            wrapper.Set(cacheKey, JsValue.Object(impl));
            return JsValue.Object(impl);
        });

        EventTargetBinding.DefineMethod(realm, docProto, "getElementById", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0) return JsValue.Null;
            return d.GetElementById(JsValue.ToStringValue(args[0])) is { } e
                ? JsValue.Object(DomWrappers.Wrap(realm, e)) : JsValue.Null;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "getElementsByTagName", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            foreach (var e in d.GetElementsByTagName(JsValue.ToStringValue(args[0])))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, e)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "getElementsByTagNameNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length < 2) return MakeArray(realm, Array.Empty<JsValue>());
            var ns = args[0].IsNullish ? null : JsValue.ToStringValue(args[0]);
            var items = new List<JsValue>();
            foreach (var e in d.GetElementsByTagNameNS(ns, JsValue.ToStringValue(args[1])))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, e)));
            return MakeArray(realm, items);
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, docProto, "getElementsByClassName", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            foreach (var e in d.GetElementsByClassName(JsValue.ToStringValue(args[0])))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, e)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "querySelector", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0) return JsValue.Null;
            var match = QuerySelectorEngine.First(d, JsValue.ToStringValue(args[0]), realm);
            return match is null ? JsValue.Null : JsValue.Object(DomWrappers.Wrap(realm, match));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "querySelectorAll", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0) return MakeArray(realm, Array.Empty<JsValue>());
            var items = new List<JsValue>();
            foreach (var m in QuerySelectorEngine.All(d, JsValue.ToStringValue(args[0]), realm))
                items.Add(JsValue.Object(DomWrappers.Wrap(realm, m)));
            return MakeArray(realm, items);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createElement", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length == 0)
                throw new JsThrow(realm.NewTypeError("createElement requires a tag name"));
            var name = JsValue.ToStringValue(args[0]);
            // DOM §4.5: an invalid Name throws InvalidCharacterError.
            if (!IsValidName(name))
                throw DomExceptionBinding.Throw(realm, "InvalidCharacterError", $"'{name}' is not a valid element name");
            return JsValue.Object(DomWrappers.Wrap(realm, d.CreateElement(name)));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createElementNS", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length < 2)
                throw new JsThrow(realm.NewTypeError("createElementNS requires (namespace, qualifiedName)"));
            var ns = args[0].IsNullish ? null : JsValue.ToStringValue(args[0]);
            var qname = args[1].IsNullish ? "" : JsValue.ToStringValue(args[1]);
            ValidateQualifiedName(realm, ns, qname); // throws InvalidCharacterError / NamespaceError
            return JsValue.Object(DomWrappers.Wrap(realm, d.CreateElementNS(ns, qname)));
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, docProto, "createEvent", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is null)
                throw new JsThrow(realm.NewTypeError("createEvent called on non-Document"));
            return EventTargetBinding.CreateLegacyEvent(realm, args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createTextNode", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d)
                throw new JsThrow(realm.NewTypeError("createTextNode called on non-Document"));
            var t = d.CreateTextNode(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
            return JsValue.Object(DomWrappers.Wrap(realm, t));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createProcessingInstruction", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d || args.Length < 2)
                throw new JsThrow(realm.NewTypeError("createProcessingInstruction requires (target, data)"));
            return JsValue.Object(DomWrappers.Wrap(realm,
                d.CreateProcessingInstruction(JsValue.ToStringValue(args[0]), JsValue.ToStringValue(args[1]))));
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, docProto, "createComment", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d)
                throw new JsThrow(realm.NewTypeError("createComment called on non-Document"));
            var c = d.CreateComment(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
            return JsValue.Object(DomWrappers.Wrap(realm, c));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createDocumentFragment", (thisV, _) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d)
                throw new JsThrow(realm.NewTypeError("createDocumentFragment called on non-Document"));
            return JsValue.Object(DomWrappers.Wrap(realm, d.CreateDocumentFragment()));
        }, length: 0);

        var docCtor = new JsNativeFunction(realm, "Document", 0, (_, _) =>
            throw new JsThrow(realm.NewTypeError("Illegal constructor")), isConstructor: false);
        docCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(docProto), writable: false, enumerable: false, configurable: false));
        docProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(docCtor), writable: true, enumerable: false, configurable: true));
        realm.DocumentConstructor = docCtor;
        realm.GlobalObject.DefineOwnProperty("Document",
            PropertyDescriptor.Data(JsValue.Object(docCtor), writable: true, enumerable: false, configurable: true));
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Build a DOMImplementation JS object for the given realm.
    /// DOM §4.5 — exposes <c>createHTMLDocument([title])</c>,
    /// <c>createDocumentFragment()</c>, and <c>hasFeature()</c> (always true).
    /// The returned document is wrapped with the realm's full Document prototype
    /// so all Document methods work on it.</summary>
    private static JsObject BuildDomImplementation(JsRealm realm)
    {
        var impl = new JsObject(realm.ObjectPrototype);

        // createHTMLDocument([title]) — DOM §4.5.1
        EventTargetBinding.DefineMethod(realm, impl, "createHTMLDocument", (_, args) =>
        {
            // title arg: if absent → null (no title el); if present (even "") → create title
            string? title = args.Length > 0 ? JsValue.ToStringValue(args[0]) : null;
            var doc = Document.CreateHtmlDocument(title);
            return JsValue.Object(DomWrappers.Wrap(realm, doc));
        }, length: 1);

        // createDocumentType(qualifiedName, publicId, systemId) — DOM §4.5.1
        EventTargetBinding.DefineMethod(realm, impl, "createDocumentType", (_, args) =>
        {
            var qname = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            var publicId = args.Length > 1 ? JsValue.ToStringValue(args[1]) : "";
            var systemId = args.Length > 2 ? JsValue.ToStringValue(args[2]) : "";
            return JsValue.Object(DomWrappers.Wrap(realm, new DocumentType(qname, publicId, systemId)));
        }, length: 3);

        // createDocument(namespace, qualifiedName, doctype) — DOM §4.5.1. Builds
        // an XML document; if a qualified name is given it becomes the document
        // element, and a passed doctype is inserted first.
        EventTargetBinding.DefineMethod(realm, impl, "createDocument", (_, args) =>
        {
            var ns = args.Length > 0 && !args[0].IsNullish ? JsValue.ToStringValue(args[0]) : null;
            var qname = args.Length > 1 && !args[1].IsNullish ? JsValue.ToStringValue(args[1]) : "";
            var doc = new Document();
            if (args.Length > 2 && DomWrappers.UnwrapAs<DocumentType>(args[2]) is { } dt)
                doc.AppendChild(dt);
            if (!string.IsNullOrEmpty(qname))
                doc.AppendChild(doc.CreateElementNS(ns, qname));
            return JsValue.Object(DomWrappers.Wrap(realm, doc));
        }, length: 3);

        // createDocumentFragment() — convenience stub
        EventTargetBinding.DefineMethod(realm, impl, "createDocumentFragment", (_, _) =>
        {
            var doc = new Document();
            return JsValue.Object(DomWrappers.Wrap(realm, doc.CreateDocumentFragment()));
        }, length: 0);

        // hasFeature() — DOM §4.5 always returns true per spec
        EventTargetBinding.DefineMethod(realm, impl, "hasFeature",
            (_, _) => JsValue.True, length: 0);

        return impl;
    }

    /// <summary>Build a real <see cref="JsArray"/> snapshot of a DOM result list.
    /// <para>Spec note: <c>Element.children</c>, <c>Element.childNodes</c>,
    /// <c>Document.getElementsByTagName</c>, and
    /// <c>Document.getElementsByClassName</c> are spec'd as <em>live</em>
    /// collections (HTMLCollection / NodeList). We simplify to a snapshot
    /// <see cref="JsArray"/>; the live-collection plumbing is a separate
    /// follow-up. <c>Document.querySelectorAll</c> is spec'd as a static
    /// NodeList, so a snapshot array is spec-correct there.</para></summary>
    private static JsValue MakeArray(JsRealm realm, IReadOnlyList<JsValue> items)
    {
        var arr = new JsArray(realm, items);
        return JsValue.Object(arr);
    }

    // ---- DOM Name / QName validation (DOM §1 "validate", §4.5). Approximates
    // the XML Name production: a NameStartChar (letter/_/:) followed by
    // NameChars (+digit/-/.). Sufficient for the WPT create*Element error cases.
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    /// <summary>Coerce ChildNode/ParentNode varargs to host nodes: a node arg
    /// passes through; anything else becomes a Text node (DOM §4.2.1).</summary>
    private static List<Node> CoerceNodes(JsRealm realm, Node context, JsValue[] args)
    {
        var doc = context.OwnerDocument ?? context as Document ?? new Document();
        var list = new List<Node>(args.Length);
        foreach (var a in args)
            list.Add(DomWrappers.UnwrapNode(a) is { } n ? n : doc.CreateTextNode(JsValue.ToStringValue(a)));
        return list;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_' || c == ':';
    private static bool IsNameChar(char c) => IsNameStart(c) || char.IsAsciiDigit(c) || c == '-' || c == '.';

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || !IsNameStart(name[0])) return false;
        for (var i = 1; i < name.Length; i++)
            if (!IsNameChar(name[i])) return false;
        return true;
    }

    /// <summary>A qualified name is one or two valid Names joined by a single
    /// internal colon.</summary>
    private static bool IsValidQName(string qname, out string? prefix)
    {
        prefix = null;
        if (string.IsNullOrEmpty(qname)) return false;
        var colon = qname.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return IsValidName(qname);
        if (colon == 0 || colon == qname.Length - 1) return false;
        var p = qname[..colon];
        var l = qname[(colon + 1)..];
        if (l.Contains(':', StringComparison.Ordinal) || !IsValidName(p) || !IsValidName(l)) return false;
        prefix = p;
        return true;
    }

    /// <summary>DOM §4.5 "validate and extract" for createElementNS/setAttributeNS.
    /// Throws InvalidCharacterError for a malformed qualified name and
    /// NamespaceError for prefix/namespace mismatches.</summary>
    private static void ValidateQualifiedName(JsRealm realm, string? ns, string qname)
    {
        if (!IsValidQName(qname, out var prefix))
            throw DomExceptionBinding.Throw(realm, "InvalidCharacterError", $"'{qname}' is not a valid qualified name");
        var nsOrNull = string.IsNullOrEmpty(ns) ? null : ns;
        if (prefix is not null && nsOrNull is null)
            throw DomExceptionBinding.Throw(realm, "NamespaceError", "a prefix requires a non-null namespace");
        if (prefix == "xml" && nsOrNull != XmlNamespace)
            throw DomExceptionBinding.Throw(realm, "NamespaceError", "the 'xml' prefix requires the XML namespace");
        var isXmlns = qname == "xmlns" || prefix == "xmlns";
        if (isXmlns != (nsOrNull == XmlnsNamespace))
            throw DomExceptionBinding.Throw(realm, "NamespaceError", "the xmlns name/namespace must match");
    }

    /// <summary>Run the HTML fragment parsing algorithm for <paramref name="markup"/>
    /// using <paramref name="context"/> as the parsing context element, returning a
    /// <see cref="DocumentFragment"/> whose nodes share the context element's
    /// document. Falls back to a throwaway <see cref="Document"/> for detached
    /// elements that have no owner document yet.</summary>
    private static DocumentFragment ParseFragment(Element context, string markup)
    {
        var ownerDocument = context.OwnerDocument ?? new Document();
        return HtmlTreeBuilder.ParseFragment(markup, context, ownerDocument);
    }

    /// <summary>Lookup a method on a parent prototype and call it bound to
    /// <paramref name="thisV"/>. Used when a subclass re-stamps a method that
    /// has the same body as its parent's (purely cosmetic).</summary>
    private static JsValue CallInherited(JsRealm realm, JsValue thisV, JsValue[] args,
        JsObject parentProto, string name)
    {
        var fn = parentProto.Get(name);
        return AbstractOperations.Call(realm.ActiveVm, fn, thisV, args);
    }

    /// <summary>Build a plain DOMRect-shaped JS object from a snapshot rect.
    /// Spec calls for a real <c>DOMRect</c> prototype; the bag here is
    /// duck-compatible for the read-only paths every test we care about
    /// touches (<c>r.width</c>, <c>r.top</c>, etc.).</summary>
    internal static JsObject BuildDomRect(JsRealm realm, LayoutRect rect)
    {
        var o = new JsObject(realm.ObjectPrototype);
        o.Set("x", JsValue.Number(rect.X));
        o.Set("y", JsValue.Number(rect.Y));
        o.Set("width", JsValue.Number(rect.Width));
        o.Set("height", JsValue.Number(rect.Height));
        o.Set("top", JsValue.Number(rect.Top));
        o.Set("right", JsValue.Number(rect.Right));
        o.Set("bottom", JsValue.Number(rect.Bottom));
        o.Set("left", JsValue.Number(rect.Left));
        return o;
    }

    /// <summary>A minimal CanvasRenderingContext2D supporting the text-metrics
    /// path (<c>font</c> + <c>measureText</c>) plus no-op drawing methods so
    /// code that grabs a 2d context doesn't crash. <c>measureText</c> width is
    /// an approximation: average glyph advance ≈ 0.5em of the font's px size,
    /// which is enough for column auto-sizing not to throw or collapse.</summary>
    private static JsObject BuildCanvas2dContext(JsRealm realm)
    {
        var ctx = new JsObject(realm.ObjectPrototype);
        ctx.DefineOwnProperty("font",
            PropertyDescriptor.Data(JsValue.String("10px sans-serif"),
                writable: true, enumerable: true, configurable: true));

        EventTargetBinding.DefineMethod(realm, ctx, "measureText", (thisV, args) =>
        {
            var text = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            var fontPx = 10.0;
            if (thisV.IsObject && thisV.AsObject.Get("font") is { IsString: true } f)
                fontPx = ParseFontPx(f.AsString) ?? 10.0;
            var metrics = new JsObject(realm.ObjectPrototype);
            metrics.Set("width", JsValue.Number(text.Length * fontPx * 0.5));
            return JsValue.Object(metrics);
        }, length: 1);

        // No-op drawing surface: enough for feature-detection / incidental calls.
        foreach (var noop in new[]
        {
            "save", "restore", "beginPath", "closePath", "moveTo", "lineTo",
            "fill", "stroke", "fillRect", "clearRect", "strokeRect", "rect",
            "scale", "rotate", "translate", "setTransform", "fillText", "strokeText",
            "drawImage", "arc", "clip",
        })
            EventTargetBinding.DefineMethod(realm, ctx, noop, (_, _) => JsValue.Undefined, length: 0);

        return ctx;
    }

    /// <summary>Pull the px size out of a CSS <c>font</c> shorthand
    /// (e.g. <c>"normal 12px Arial"</c> → 12). Returns null if absent.</summary>
    private static double? ParseFontPx(string font)
    {
        var idx = font.IndexOf("px", StringComparison.OrdinalIgnoreCase);
        if (idx <= 0) return null;
        var start = idx;
        while (start > 0 && (char.IsDigit(font[start - 1]) || font[start - 1] == '.')) start--;
        return double.TryParse(font.AsSpan(start, idx - start),
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static JsValue ReadOffsetMetric(JsRealm realm, JsValue thisV, Func<OffsetMetrics, double> select)
    {
        var host = WindowBinding.LayoutHostForRealm(realm);
        if (DomWrappers.UnwrapElement(thisV) is { } e &&
            host is not null && host.TryGetOffsetMetrics(e, out var m))
        {
            return JsValue.Number(select(m));
        }
        return JsValue.Number(0);
    }

    /// <summary>If <paramref name="e"/> is a <c>&lt;script&gt;</c> and the
    /// mutated attribute is <c>src</c> with a non-empty value, notify the
    /// engine so it can run HTML §4.12.1 "prepare a script" (fetch + execute +
    /// fire load/error). Scoped strictly to script-element <c>src</c>; every
    /// other attribute write is untouched.</summary>
    private static void MaybeTriggerScriptSrc(JsRealm realm, Element e, string attrName)
    {
        if (!attrName.Equals("src", StringComparison.OrdinalIgnoreCase)) return;
        if (!e.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)) return;
        var src = e.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src)) return;
        ScriptSrcHook.NotifySrcSet(realm, e);
    }

    private static int NodeTypeFromKind(NodeKind kind) => (int)kind;

    private static string NormalizeNodeName(Node n) => n switch
    {
        Element e => e.TagName.ToUpperInvariant(),
        Document => "#document",
        Text => "#text",
        Comment => "#comment",
        DocumentFragment => "#document-fragment",
        _ => n.NodeName,
    };

    // ---- DOM Living Standard helpers ----------------------------------------

    /// <summary>DOM §4.4.4 cloneNode implementation. Shallow copies tag+attributes
    /// for Elements; deep additionally clones the entire subtree.</summary>
    private static Node CloneNode(JsRealm realm, Node source, bool deep)
    {
        var doc = source.OwnerDocument ?? (source as Document);
        Node clone = source switch
        {
            Element el when doc is not null => CloneElement(doc, el),
            Element el => new Element(el.TagName, el.Namespace),
            Text t => doc?.CreateTextNode(t.Data) ?? new Text(t.Data),
            Comment c => doc?.CreateComment(c.Data) ?? new Comment(c.Data),
            DocumentFragment when doc is not null => doc.CreateDocumentFragment(),
            DocumentFragment => new DocumentFragment(),
            _ => source, // Documents and other node types: return as-is (simplified)
        };

        if (deep && clone != source)
        {
            for (var child = source.FirstChild; child is not null; child = child.NextSibling)
            {
                var childClone = CloneNode(realm, child, deep: true);
                clone.AppendChild(childClone);
            }
        }
        return clone;
    }

    private static Element CloneElement(Document doc, Element el)
    {
        // Preserve case + prefix for namespaced elements; HTML elements keep the
        // lowercasing path.
        var clone = el.Prefix is not null || el.Namespace != Element.HtmlNamespace
            ? doc.CreateElementNS(el.Namespace, el.Prefix is null ? el.LocalName : el.Prefix + ":" + el.LocalName)
            : doc.CreateElement(el.LocalName);
        // Copy attributes, preserving any namespace/qualified name.
        foreach (var attr in el.Attributes)
            clone.Attributes.SetNamedItemNS(attr);
        return clone;
    }

    /// <summary>Helper for before/after/prepend/append: inserts <paramref name="arg"/>
    /// (a JS Node wrapper or a string) before <paramref name="before"/> in
    /// <paramref name="parent"/>. A null <paramref name="before"/> means
    /// append at the end.</summary>
    private static void InsertAdjacentNode(JsRealm realm, Node parent, JsValue arg, Node? before)
    {
        Node child;
        if (arg.IsString)
        {
            var doc = parent.OwnerDocument ?? (parent as Document);
            child = doc?.CreateTextNode(JsValue.ToStringValue(arg)) ?? new Text(JsValue.ToStringValue(arg));
        }
        else if (DomWrappers.UnwrapNode(arg) is { } n)
        {
            child = n;
        }
        else
        {
            // Non-node, non-string: coerce to string, make text node.
            var s = JsValue.ToStringValue(arg);
            var doc = parent.OwnerDocument ?? (parent as Document);
            child = doc?.CreateTextNode(s) ?? new Text(s);
        }
        parent.InsertBefore(child, before);
    }

    /// <summary>DOM §4.2.6 normalize: removes empty Text nodes and merges
    /// adjacent Text sibling nodes. Operates on the node's subtree.</summary>
    private static void NormalizeNode(Node n)
    {
        var child = n.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            if (child is Text t)
            {
                if (string.IsNullOrEmpty(t.Data))
                {
                    n.RemoveChild(child);
                }
                else
                {
                    // Merge subsequent adjacent text nodes.
                    while (next is Text t2)
                    {
                        var following = t2.NextSibling;
                        t.Data += t2.Data;
                        n.RemoveChild(t2);
                        next = following;
                    }
                }
            }
            else
            {
                NormalizeNode(child);
            }
            child = next;
        }
    }

    /// <summary>Build a DOMTokenList JS object wrapping the element's classList.
    /// Spec: DOM §7.1. Methods: add, remove, toggle, contains, replace, item,
    /// forEach, keys, values, entries. Properties: length, value.</summary>
    private static JsObject BuildDomTokenList(JsRealm realm, Element element)
    {
        var obj = new JsObject(realm.ObjectPrototype);
        var cl = element.ClassList;

        EventTargetBinding.DefineAccessor(realm, obj, "length",
            (_, _) => JsValue.Number(cl.Count));
        EventTargetBinding.DefineAccessor(realm, obj, "value",
            (_, _) => JsValue.String(element.GetAttribute("class") ?? ""),
            (_, args) =>
            {
                element.SetAttribute("class", args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
                return JsValue.Undefined;
            });

        EventTargetBinding.DefineMethod(realm, obj, "contains", (_, args) =>
        {
            if (args.Length == 0) return JsValue.False;
            try { return JsValue.Boolean(cl.Contains(JsValue.ToStringValue(args[0]))); }
            catch { return JsValue.False; }
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, obj, "add", (_, args) =>
        {
            foreach (var arg in args)
            {
                try { cl.Add(JsValue.ToStringValue(arg)); }
                catch { /* invalid token — skip per spec */ }
            }
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, obj, "remove", (_, args) =>
        {
            foreach (var arg in args)
            {
                try { cl.Remove(JsValue.ToStringValue(arg)); }
                catch { /* invalid token — skip per spec */ }
            }
            return JsValue.Undefined;
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, obj, "toggle", (_, args) =>
        {
            if (args.Length == 0) return JsValue.False;
            var token = JsValue.ToStringValue(args[0]);
            // Optional force arg (args[1]).
            bool result;
            if (args.Length > 1 && !args[1].IsUndefined)
            {
                var force = JsValue.ToBoolean(args[1]);
                if (force) { try { cl.Add(token); } catch { } result = true; }
                else { try { cl.Remove(token); } catch { } result = false; }
            }
            else
            {
                if (cl.Contains(token)) { try { cl.Remove(token); } catch { } result = false; }
                else { try { cl.Add(token); } catch { } result = true; }
            }
            return JsValue.Boolean(result);
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, obj, "replace", (_, args) =>
        {
            if (args.Length < 2) return JsValue.False;
            var oldToken = JsValue.ToStringValue(args[0]);
            var newToken = JsValue.ToStringValue(args[1]);
            try
            {
                if (!cl.Contains(oldToken)) return JsValue.False;
                cl.Remove(oldToken);
                cl.Add(newToken);
                return JsValue.True;
            }
            catch { return JsValue.False; }
        }, length: 2);
        EventTargetBinding.DefineMethod(realm, obj, "item", (_, args) =>
        {
            if (args.Length == 0) return JsValue.Null;
            var idx = (int)JsValue.ToNumber(args[0]);
            return idx >= 0 && idx < cl.Count ? JsValue.String(cl[idx]) : JsValue.Null;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, obj, "forEach", (_, args) =>
        {
            if (args.Length == 0 || !AbstractOperations.IsCallable(args[0])) return JsValue.Undefined;
            var fn = args[0];
            var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
            for (var i = 0; i < cl.Count; i++)
            {
                var token = JsValue.String(cl[i]);
                AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { token, JsValue.Number(i), JsValue.Object(obj) });
            }
            return JsValue.Undefined;
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, obj, "keys", (_, _) =>
        {
            var items = new List<JsValue>();
            for (var i = 0; i < cl.Count; i++) items.Add(JsValue.Number(i));
            return MakeArray(realm, items);
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, obj, "values", (_, _) =>
        {
            var items = new List<JsValue>();
            for (var i = 0; i < cl.Count; i++) items.Add(JsValue.String(cl[i]));
            return MakeArray(realm, items);
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, obj, "entries", (_, _) =>
        {
            var items = new List<JsValue>();
            for (var i = 0; i < cl.Count; i++)
            {
                var pair = new JsArray(realm, new[] { JsValue.Number(i), JsValue.String(cl[i]) });
                items.Add(JsValue.Object(pair));
            }
            return MakeArray(realm, items);
        }, length: 0);
        EventTargetBinding.DefineMethod(realm, obj, "toString",
            (_, _) => JsValue.String(element.GetAttribute("class") ?? ""), length: 0);

        return obj;
    }

    /// <summary>Build an inline-style CSSStyleDeclaration-shaped object for
    /// an element's style attribute. Property reads/writes parse/serialize
    /// the element's `style` attribute as a flat CSS property bag.
    /// Spec: CSSOM §6.1 CSSStyleDeclaration with cssText / getPropertyValue /
    /// setProperty / removeProperty plus camelCase shorthand accessors.</summary>
    private static JsObject BuildInlineStyleDecl(JsRealm realm, Element element)
    {
        // Parse current style attribute into a dict; serialize back on mutation.
        var obj = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineAccessor(realm, obj, "cssText",
            (_, _) => JsValue.String(element.GetAttribute("style") ?? ""),
            (_, args) =>
            {
                var v = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
                if (string.IsNullOrWhiteSpace(v))
                    element.RemoveAttribute("style");
                else
                    element.SetAttribute("style", v);
                return JsValue.Undefined;
            });

        EventTargetBinding.DefineMethod(realm, obj, "getPropertyValue", (_, args) =>
        {
            if (args.Length == 0) return JsValue.String("");
            var prop = JsValue.ToStringValue(args[0]).Trim().ToLowerInvariant();
            return JsValue.String(ParseInlineStyleProp(element, prop));
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, obj, "setProperty", (_, args) =>
        {
            if (args.Length < 2) return JsValue.Undefined;
            var prop = JsValue.ToStringValue(args[0]).Trim().ToLowerInvariant();
            var value = JsValue.ToStringValue(args[1]).Trim();
            WriteInlineStyleProp(element, prop, value);
            return JsValue.Undefined;
        }, length: 2);

        EventTargetBinding.DefineMethod(realm, obj, "removeProperty", (_, args) =>
        {
            if (args.Length == 0) return JsValue.String("");
            var prop = JsValue.ToStringValue(args[0]).Trim().ToLowerInvariant();
            var old = ParseInlineStyleProp(element, prop);
            WriteInlineStyleProp(element, prop, null);
            return JsValue.String(old);
        }, length: 1);

        // Camel-case / kebab-case accessors for the most-used CSS properties.
        // The bundle uses: display, position, backgroundClip, filter, left, top,
        // right, cssText, zoom. We expose all CommonComputedStyleProps plus more.
        foreach (var kebab in InlineStyleProperties)
        {
            var capturedKebab = kebab;
            var camel = KebabToCamel(kebab);
            // Kebab accessor
            EventTargetBinding.DefineAccessor(realm, obj, capturedKebab,
                (_, _) => JsValue.String(ParseInlineStyleProp(element, capturedKebab)),
                (_, a) => { WriteInlineStyleProp(element, capturedKebab, a.Length > 0 ? JsValue.ToStringValue(a[0]) : ""); return JsValue.Undefined; });
            // CamelCase accessor (only if different from kebab)
            if (camel != capturedKebab)
            {
                EventTargetBinding.DefineAccessor(realm, obj, camel,
                    (_, _) => JsValue.String(ParseInlineStyleProp(element, capturedKebab)),
                    (_, a) => { WriteInlineStyleProp(element, capturedKebab, a.Length > 0 ? JsValue.ToStringValue(a[0]) : ""); return JsValue.Undefined; });
            }
        }

        return obj;
    }

    /// <summary>Build a DOMStringMap for the element's data-* attributes.
    /// Spec: HTML §2.7.3. Property names use camelCase; attribute names use
    /// kebab-case prefixed with "data-".</summary>
    private static JsDatasetObject BuildDataset(JsRealm realm, Element element)
    {
        // Exotic object: property reads/writes delegate to data-* attributes.
        return new JsDatasetObject(realm.ObjectPrototype, element);
    }

    /// <summary>Read a single property from the element's inline style attribute.</summary>
    private static string ParseInlineStyleProp(Element element, string kebabProp)
    {
        var styleAttr = element.GetAttribute("style");
        if (string.IsNullOrEmpty(styleAttr)) return "";
        foreach (var decl in styleAttr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            var name = decl[..colon].Trim().ToLowerInvariant();
            if (name == kebabProp)
                return decl[(colon + 1)..].Trim();
        }
        return "";
    }

    /// <summary>Set (or remove when <paramref name="value"/> is null/"") a
    /// single property in the element's inline style attribute.</summary>
    private static void WriteInlineStyleProp(Element element, string kebabProp, string? value)
    {
        var styleAttr = element.GetAttribute("style") ?? "";
        var pairs = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Parse existing.
        foreach (var decl in styleAttr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = decl.IndexOf(':');
            if (colon < 0) continue;
            pairs[decl[..colon].Trim().ToLowerInvariant()] = decl[(colon + 1)..].Trim();
        }
        // Mutate.
        if (string.IsNullOrEmpty(value))
            pairs.Remove(kebabProp);
        else
            pairs[kebabProp] = value;
        // Serialize.
        if (pairs.Count == 0)
            element.RemoveAttribute("style");
        else
            element.SetAttribute("style", string.Join("; ", pairs.Select(p => $"{p.Key}: {p.Value}")));
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

    // Comprehensive list of CSS properties exposed via element.style.X
    private static readonly string[] InlineStyleProperties =
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
