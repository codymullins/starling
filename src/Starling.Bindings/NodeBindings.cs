using Tessera.Dom;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// B5-1 — Installs the Node / Element / Document JS prototypes onto a realm.
/// EventTargetBinding.Install must have run first (these prototypes inherit
/// from EventTargetPrototype).
/// </summary>
/// <remarks>
/// <para><b>Selector grammar:</b> <c>querySelector</c> /
/// <c>querySelectorAll</c> only accept three forms — <c>#id</c>, <c>.class</c>,
/// and a bare tag name. Anything else throws a <c>SyntaxError</c>. The real
/// CSS-selector engine arrives in a follow-up.</para>
/// <para><b>innerHTML:</b> getter returns <see cref="Node.TextContent"/> — we
/// don't have a tree-to-HTML serializer threaded here. Setter accepts only
/// plain text (no HTML parsing) and replaces children with a single text node.
/// The full parser path lands with B5-3 plumbing.</para>
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
        EventTargetBinding.DefineAccessor(realm, elProto, "innerHTML",
            (thisV, _) => DomWrappers.UnwrapElement(thisV) is { } e ? JsValue.String(e.TextContent) : JsValue.String(""),
            (thisV, args) =>
            {
                // Simplification: no HTML parser is reachable from this layer.
                // Treat the assignment as a textContent replacement.
                if (DomWrappers.UnwrapElement(thisV) is { } e)
                    e.TextContent = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
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
                e.SetAttribute(JsValue.ToStringValue(args[0]), JsValue.ToStringValue(args[1]));
            return JsValue.Undefined;
        }, length: 2);
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
        EventTargetBinding.DefineMethod(realm, elProto, "appendChild", (thisV, args) =>
        {
            // Inherits from Node — re-stamped here so JS-level instanceof tests
            // see the property on Element.prototype too. Reuse Node impl via lookup.
            return CallInherited(realm, thisV, args, realm.NodePrototype!, "appendChild");
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
            var elt = d.CreateElement(JsValue.ToStringValue(args[0]));
            return JsValue.Object(DomWrappers.Wrap(realm, elt));
        }, length: 1);
        EventTargetBinding.DefineMethod(realm, docProto, "createTextNode", (thisV, args) =>
        {
            if (DomWrappers.UnwrapDocument(thisV) is not { } d)
                throw new JsThrow(realm.NewTypeError("createTextNode called on non-Document"));
            var t = d.CreateTextNode(args.Length > 0 ? JsValue.ToStringValue(args[0]) : "");
            return JsValue.Object(DomWrappers.Wrap(realm, t));
        }, length: 1);
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
}
