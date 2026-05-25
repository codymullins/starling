using System.Collections;

namespace Starling.Dom;

public sealed class NamedNodeMap : IReadOnlyList<Attr>
{
    private readonly Element _owner;
    private readonly List<Attr> _attributes = [];

    internal NamedNodeMap(Element owner)
    {
        _owner = owner;
    }

    public int Count => _attributes.Count;

    public Attr this[int index] => _attributes[index];

    public Attr? GetNamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var attr in _attributes)
        {
            if (attr.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return attr;
        }

        return null;
    }

    public void SetNamedItem(Attr attr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr.Name);
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name.Equals(attr.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (_attributes[i] == attr)
                    return;

                _attributes[i] = attr;
                _owner.OnAttributeMutated();
                return;
            }
        }

        _attributes.Add(attr);
        _owner.OnAttributeMutated();
    }

    public Attr? RemoveNamedItem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var removed = _attributes[i];
                _attributes.RemoveAt(i);
                _owner.OnAttributeMutated();
                return removed;
            }
        }

        return null;
    }

    // ---- Namespace-aware operations (DOM §4.9). Attributes are keyed by
    // (namespace, local name); the qualified Name carries the prefix. Null and
    // "" namespaces are equivalent (no namespace).

    /// <summary>Local part of a qualified name ("xlink:href" → "href").</summary>
    public static string LocalNameOf(string qualifiedName)
    {
        var i = qualifiedName.IndexOf(':', StringComparison.Ordinal);
        return i >= 0 ? qualifiedName[(i + 1)..] : qualifiedName;
    }

    private static bool NsEq(string? a, string? b)
        => (string.IsNullOrEmpty(a) ? null : a) == (string.IsNullOrEmpty(b) ? null : b);

    public Attr? GetNamedItemNS(string? ns, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        foreach (var attr in _attributes)
            if (NsEq(attr.Namespace, ns) && LocalNameOf(attr.Name).Equals(localName, StringComparison.Ordinal))
                return attr;
        return null;
    }

    public void SetNamedItemNS(Attr attr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attr.Name);
        var localName = LocalNameOf(attr.Name);
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (NsEq(_attributes[i].Namespace, attr.Namespace)
                && LocalNameOf(_attributes[i].Name).Equals(localName, StringComparison.Ordinal))
            {
                if (_attributes[i] == attr) return;
                _attributes[i] = attr;
                _owner.OnAttributeMutated();
                return;
            }
        }
        _attributes.Add(attr);
        _owner.OnAttributeMutated();
    }

    public Attr? RemoveNamedItemNS(string? ns, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (NsEq(_attributes[i].Namespace, ns) && LocalNameOf(_attributes[i].Name).Equals(localName, StringComparison.Ordinal))
            {
                var removed = _attributes[i];
                _attributes.RemoveAt(i);
                _owner.OnAttributeMutated();
                return removed;
            }
        }
        return null;
    }

    public IEnumerator<Attr> GetEnumerator() => _attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
