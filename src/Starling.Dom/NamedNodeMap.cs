using System.Collections;

namespace Tessera.Dom;

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

    public IEnumerator<Attr> GetEnumerator() => _attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
