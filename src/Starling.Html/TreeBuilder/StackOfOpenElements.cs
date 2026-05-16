using Tessera.Dom;

namespace Tessera.Html.TreeBuilder;

/// <summary>
/// Stack of open elements per [HTML §13.2.4.2](https://html.spec.whatwg.org/multipage/parsing.html#the-stack-of-open-elements).
/// Provides "has X in scope" queries used to validate end-tag closures.
/// </summary>
internal sealed class StackOfOpenElements
{
    private readonly List<Element> _items = [];

    public int Count => _items.Count;
    public Element this[int i] => _items[i];

    /// <summary>The "current node" is the bottom of the stack.</summary>
    public Element Current => _items[^1];

    public bool IsEmpty => _items.Count == 0;

    public void Push(Element e) => _items.Add(e);

    public Element Pop()
    {
        var top = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        return top;
    }

    public bool Contains(Element e) => _items.Contains(e);

    /// <summary>Pop elements off until — and including — the named element.</summary>
    public void PopUntilNamed(string localName)
    {
        while (_items.Count > 0)
        {
            var popped = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (string.Equals(popped.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return;
        }
    }

    /// <summary>Pop elements off until the named element is at the top (exclusive).</summary>
    public void PopWhileNotNamed(string localName)
    {
        while (_items.Count > 0 && !string.Equals(_items[^1].LocalName, localName, StringComparison.OrdinalIgnoreCase))
            _items.RemoveAt(_items.Count - 1);
    }

    public Element? FindByName(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_items[i].LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return _items[i];
        }
        return null;
    }

    /// <summary>
    /// "Has element in scope" per §13.2.4.2 — walk down the stack until either
    /// <paramref name="localName"/> is found (true), or a scope-terminating
    /// element is found (false).
    /// </summary>
    public bool HasInScope(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (string.Equals(node.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsScopeTerminator(node.LocalName))
                return false;
        }
        return false;
    }

    public bool HasInButtonScope(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (string.Equals(node.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsScopeTerminator(node.LocalName) ||
                string.Equals(node.LocalName, "button", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return false;
    }

    public bool HasInListItemScope(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (string.Equals(node.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                return true;
            if (IsScopeTerminator(node.LocalName) ||
                string.Equals(node.LocalName, "ol", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.LocalName, "ul", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return false;
    }

    private static bool IsScopeTerminator(string localName) => localName.ToLowerInvariant() switch
    {
        "applet" or "caption" or "html" or "table" or "td" or "th"
            or "marquee" or "object" or "template"
            or "mi" or "mo" or "mn" or "ms" or "mtext"
            or "annotation-xml" or "foreignobject" or "desc" or "title" => true,
        _ => false,
    };
}
