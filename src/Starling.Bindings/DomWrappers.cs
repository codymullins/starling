using System.Runtime.CompilerServices;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// B5-1 — Lazy DOM-node ↔ JS-wrapper map. Each host node gets at most one JS
/// wrapper per realm, returned by every <see cref="Wrap"/> call. The wrapper's
/// prototype chain is wired so JS methods on Element/Document/Node/EventTarget
/// resolve via ordinary prototype walk.
/// </summary>
/// <remarks>
/// <para>Wrappers are stored in <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// so they are released when the host node becomes unreachable. The cache is
/// keyed by the host node, not by the wrapper, so a single node observed
/// through two paths still resolves to the same JS object — preserving the
/// invariant <c>document.body === document.body</c>.</para>
/// </remarks>
public static class DomWrappers
{
    // Per-realm wrapper caches. Two realms (rare) get independent caches so a
    // wrapper from realm A isn't reused as the identity in realm B.
    private static readonly ConditionalWeakTable<JsRealm, ConditionalWeakTable<Node, JsObject>> NodeCachesPerRealm = new();

    /// <summary>Return the JS wrapper for <paramref name="target"/> on the
    /// supplied realm. Allocates a new wrapper the first time a node is
    /// observed; returns the cached wrapper on subsequent calls. Non-Node
    /// EventTargets (e.g. Window's <see cref="InMemoryEventTarget"/>) fall back
    /// to a transient plain JS object — currently unused outside synthetic
    /// scenarios.</summary>
    public static JsObject Wrap(JsRealm realm, EventTarget target)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        if (target is Node node) return WrapNode(realm, node);
        // Fallback: return a fresh wrapper bound to the host. Not cached.
        var proto = realm.EventTargetPrototype ?? realm.ObjectPrototype;
        var fresh = new JsObject(proto);
        EventTargetBinding.BindWrapper(fresh, target);
        return fresh;
    }

    private static JsObject WrapNode(JsRealm realm, Node node)
    {
        var cache = NodeCachesPerRealm.GetValue(realm, _ => new ConditionalWeakTable<Node, JsObject>());
        if (cache.TryGetValue(node, out var existing)) return existing;

        // CharacterData subtypes (Text, Comment, CData, PI) get their own
        // prototype so `instanceof Text` / `instanceof Comment` work.
        var proto = node switch
        {
            Document => realm.DocumentPrototype ?? realm.ObjectPrototype,
            Element => realm.ElementPrototype ?? realm.ObjectPrototype,
            _ => NodeBindings.CharDataProtoFor(realm, node) ?? realm.NodePrototype ?? realm.ObjectPrototype,
        };
        var wrapper = new JsObject(proto);
        EventTargetBinding.BindWrapper(wrapper, node);
        cache.Add(node, wrapper);
        return wrapper;
    }

    /// <summary>Resolve the host <see cref="Node"/> backing a JS wrapper, or
    /// null when the object is not a wrapped DOM node.</summary>
    public static Node? UnwrapNode(JsValue v) =>
        EventTargetBinding.ResolveHost(v) as Node;

    public static Element? UnwrapElement(JsValue v) => UnwrapNode(v) as Element;
    public static Document? UnwrapDocument(JsValue v) => UnwrapNode(v) as Document;

    /// <summary>Convenience for proto methods: returns null when <c>this</c> is
    /// not a wrapped node of the expected type.</summary>
    internal static T? UnwrapAs<T>(JsValue v) where T : class => EventTargetBinding.ResolveHost(v) as T;
}

/// <summary>
/// Exotic JS object backing <c>element.dataset</c> (HTML §2.7.3 DOMStringMap).
/// Property reads/writes delegate to the element's <c>data-*</c> attributes.
/// Name mapping: camelCase property ↔ "data-" + kebab-case attribute name.
/// </summary>
internal sealed class JsDatasetObject : JsObject
{
    private readonly Element _element;

    public JsDatasetObject(JsObject proto, Element element) : base(proto)
    {
        _element = element;
    }

    // camelCase → data-kebab-case
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

    // data-kebab-case → camelCase
    private static string AttrToProp(string attr)
    {
        // Strip "data-" prefix
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

    public override JsValue Get(string name)
    {
        var attr = _element.GetAttribute(PropToAttr(name));
        return attr is not null ? JsValue.String(attr) : base.Get(name);
    }

    public override void Set(string name, JsValue value)
    {
        _element.SetAttribute(PropToAttr(name), JsValue.ToStringValue(value));
    }

    public override bool DefineOwnProperty(string name, PropertyDescriptor desc)
    {
        if (desc.IsAccessor) return base.DefineOwnProperty(name, desc);
        _element.SetAttribute(PropToAttr(name), JsValue.ToStringValue(desc.Value));
        return true;
    }

    public override PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        var attr = _element.GetAttribute(PropToAttr(name));
        if (attr is not null)
            return PropertyDescriptor.Data(JsValue.String(attr), writable: true, enumerable: true, configurable: true);
        return base.GetOwnPropertyDescriptor(name);
    }

    public override bool Has(string name)
    {
        if (_element.HasAttribute(PropToAttr(name))) return true;
        return base.Has(name);
    }

    public override bool HasOwn(string name)
    {
        if (_element.HasAttribute(PropToAttr(name))) return true;
        return base.HasOwn(name);
    }

    public override bool Delete(string name)
    {
        if (_element.HasAttribute(PropToAttr(name)))
        {
            _element.RemoveAttribute(PropToAttr(name));
            return true;
        }
        return base.Delete(name);
    }

    public override IEnumerable<string> EnumerableKeys()
    {
        foreach (var attr in _element.Attributes)
        {
            if (attr.Name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                yield return AttrToProp(attr.Name);
        }
    }

    public override IEnumerable<string> Keys =>
        EnumerableKeys().Concat(base.Keys);
}
