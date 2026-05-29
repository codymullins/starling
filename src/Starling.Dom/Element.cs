namespace Starling.Dom;

/// <summary>
/// Generic element. HTML-specific subclasses are deferred; this core element
/// provides attributes and tree behavior for parser/layout consumers.
/// </summary>
public class Element : Node
{
    public const string HtmlNamespace = "http://www.w3.org/1999/xhtml";

    public Element(string tagName, string? @namespace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        LocalName = tagName.ToLowerInvariant();
        Namespace = @namespace ?? HtmlNamespace;
        TagName = LocalName;
        Attributes = new NamedNodeMap(this);
        ClassList = new DomTokenList(
            () => GetAttribute("class") ?? string.Empty,
            value => SetAttribute("class", value));
    }

    // Namespace-aware construction (createElementNS / createDocument): unlike the
    // HTML ctor above it preserves the qualified name's case and splits the
    // prefix, so prefix/localName/tagName and namespace lookups are correct.
    private Element(string qualifiedName, string localName, string? prefix, string @namespace)
    {
        ArgumentException.ThrowIfNullOrEmpty(qualifiedName);
        TagName = qualifiedName;
        LocalName = localName;
        Prefix = prefix;
        Namespace = @namespace;
        Attributes = new NamedNodeMap(this);
        ClassList = new DomTokenList(
            () => GetAttribute("class") ?? string.Empty,
            value => SetAttribute("class", value));
    }

    /// <summary>DOM §4.5 createElementNS construction: parse <paramref name="qualifiedName"/>
    /// into prefix + local name (case preserved). A null/empty namespace maps to
    /// the HTML namespace (this engine models Namespace as non-null).</summary>
    public static Element CreateNamespaced(string? @namespace, string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var i = qualifiedName.IndexOf(':', StringComparison.Ordinal);
        var prefix = i >= 0 ? qualifiedName[..i] : null;
        var local = i >= 0 ? qualifiedName[(i + 1)..] : qualifiedName;
        return new Element(qualifiedName, local, prefix, string.IsNullOrEmpty(@namespace) ? HtmlNamespace : @namespace);
    }

    public override NodeKind Kind => NodeKind.Element;

    public override string NodeName => TagName;

    public string LocalName { get; }

    public string? Prefix { get; init; }

    public string Namespace { get; }

    /// <summary>Lower-cased tag name; existing M0 parser/tests depend on this shape.</summary>
    public string TagName { get; }

    public NamedNodeMap Attributes { get; }

    public string Id
    {
        get => GetAttribute("id") ?? string.Empty;
        set => SetAttribute("id", value);
    }

    public DomTokenList ClassList { get; }

    /// <summary>
    /// The live IDL value of a form control (<c>&lt;input&gt;</c> /
    /// <c>&lt;textarea&gt;</c>) — the text the user has typed or that a script
    /// assigned via <c>element.value = …</c>. Null until the field is first
    /// edited; while null the <c>value</c> content attribute supplies the
    /// initial value. Layout (the synthesized label text) and the JS
    /// <c>.value</c> accessor read this in preference to the attribute so typed
    /// text and scripted assignments are reflected. Non-form elements ignore it.
    /// </summary>
    public string? InputValue { get; set; }

    public string? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Attributes.GetNamedItem(name)?.Value;
    }

    public void SetAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        var lname = name.ToLowerInvariant();
        var existing = Attributes.GetNamedItem(lname);
        if (existing is not null)
            existing.Value = value; // mutate in-place so AttrNode identity is preserved
        else
            Attributes.SetNamedItem(new AttrNode(lname, value));
    }

    public bool HasAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Attributes.GetNamedItem(name) is not null;
    }

    public void RemoveAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Attributes.RemoveNamedItem(name);
    }

    // ---- Namespace-aware attributes (DOM §4.9). setAttributeNS preserves the
    // qualified name's case (unlike HTML setAttribute, which lower-cases).

    public string? GetAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return Attributes.GetNamedItemNS(@namespace, localName)?.Value;
    }

    public void SetAttributeNS(string? @namespace, string qualifiedName, string value)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        ArgumentNullException.ThrowIfNull(value);
        var ns = string.IsNullOrEmpty(@namespace) ? null : @namespace;
        var existing = Attributes.GetNamedItemNS(ns, NamedNodeMap.LocalNameOf(qualifiedName));
        if (existing is not null)
            existing.Value = value; // mutate in-place
        else
            Attributes.SetNamedItemNS(AttrNode.CreateNamespaced(qualifiedName, ns, value));
    }

    public bool HasAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return Attributes.GetNamedItemNS(@namespace, localName) is not null;
    }

    public void RemoveAttributeNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        Attributes.RemoveNamedItemNS(@namespace, localName);
    }

    /// <summary>DOM §4.9 getElementsByTagNameNS over this element's descendants.
    /// "*" matches any namespace / any local name.</summary>
    public IEnumerable<Element> GetElementsByTagNameNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var anyNs = @namespace == "*";
        var anyLocal = localName == "*";
        var ns = string.IsNullOrEmpty(@namespace) ? null : @namespace;
        foreach (var d in DescendantElements())
        {
            if (!anyNs && !string.Equals(d.Namespace, ns, StringComparison.Ordinal)) continue;
            if (!anyLocal && !d.LocalName.Equals(localName, StringComparison.Ordinal)) continue;
            yield return d;
        }
    }

    public override string ToString() => $"<{TagName}>";

    internal void OnAttributeMutated(string attrName)
    {
        if (OwnerDocument is { } d)
        {
            d.NoteAttributeMutation(attrName);
            // Always advance MutationVersion — observers, PumpFrame, and live
            // collections rely on "any DOM change advances this counter". But
            // LayoutInvalidationVersion only advances for attributes that the
            // built-in cascade actually depends on. See
            // Document.IsLayoutRelevantAttribute for the trade-off.
            d.BumpMutationVersion();
            // Selector-aware (plan §7): relevant if the static heuristic says so,
            // OR some active stylesheet selects on this attribute (so author CSS
            // keyed on a data-*/aria-* attribute still invalidates on a script write).
            if (d.IsAttributeLayoutRelevant(attrName))
            {
                d.BumpLayoutInvalidationVersion();
                d.RecordLayoutMutation(this, LayoutChangeKind.LayoutRelevantAttr);
            }
            return;
        }
        // Detached element — fall back to the parent walk in OnTreeMutated.
        OnTreeMutated();
    }

    /// <summary>Called by <see cref="AttrNode.Value"/> setter to propagate a
    /// value change on an attached attribute node back into any dependent state
    /// (mutation observers, style engine invalidation, etc.). Currently just
    /// bumps the mutation version via OnAttributeMutated.</summary>
    internal void SyncAttrNodeValue(AttrNode attr, string newValue)
    {
        // The AttrNode._value field is already updated by the caller.
        // Fire the same mutation hook that setAttribute fires so all observers
        // (cascade invalidation, mutation records) are notified.
        OnAttributeMutated(attr.Name);
    }
}
