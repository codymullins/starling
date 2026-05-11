namespace Tessera.Dom;

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

    public string? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Attributes.GetNamedItem(name)?.Value;
    }

    public void SetAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        Attributes.SetNamedItem(new Attr(name.ToLowerInvariant(), value));
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

    public override string ToString() => $"<{TagName}>";

    internal void OnAttributeMutated() => OnTreeMutated();
}
