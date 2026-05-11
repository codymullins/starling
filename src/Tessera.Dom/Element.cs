namespace Tessera.Dom;

/// <summary>
/// Generic element. The HTMLElement subclass tree (HtmlAnchorElement, etc.)
/// per 05_DOM.md §Node hierarchy lights up in M1.
/// </summary>
public class Element : Node
{
    public Element(string tagName)
    {
        TagName = tagName;
    }

    public override NodeKind Kind => NodeKind.Element;

    /// <summary>Lower-cased tag name. Normalized at construction.</summary>
    public string TagName { get; }

    private readonly List<Attr> _attributes = [];

    public IReadOnlyList<Attr> Attributes => _attributes;

    public string? GetAttribute(string name)
    {
        foreach (var a in _attributes)
            if (a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return a.Value;
        return null;
    }

    public void SetAttribute(string name, string value)
    {
        for (var i = 0; i < _attributes.Count; i++)
        {
            if (_attributes[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _attributes[i] = new Attr(name, value);
                OnTreeMutated();
                return;
            }
        }
        _attributes.Add(new Attr(name, value));
        OnTreeMutated();
    }

    public override string ToString() => $"<{TagName}>";
}

/// <summary>
/// Attribute is no longer a Node since DOM4 (2013) — modeled as a small struct
/// owned by the element. See 05_DOM.md §Node hierarchy.
/// </summary>
public readonly record struct Attr(string Name, string Value);
