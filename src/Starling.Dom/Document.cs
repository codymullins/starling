namespace Tessera.Dom;

/// <summary>
/// Root of a DOM tree. This M1 core covers node creation, tree lookup helpers,
/// document convenience accessors, and live-collection invalidation.
/// </summary>
public sealed class Document : Node
{
    public override NodeKind Kind => NodeKind.Document;

    public override string NodeName => "#document";

    public override Document? OwnerDocument
    {
        get => this;
        internal set { /* documents own themselves; setter is a no-op for tree machinery. */ }
    }

    public int MutationVersion { get; private set; }

    internal void BumpMutationVersion() => MutationVersion++;

    public DocumentType? DocType
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
                if (child is DocumentType type) return type;
            return null;
        }
    }

    public QuirksMode Mode { get; internal set; } = QuirksMode.NoQuirks;

    public Element CreateElement(string tagName, string? @namespace = null)
        => new(tagName, @namespace) { OwnerDocument = this };

    public Text CreateText(string data) => CreateTextNode(data);

    public Text CreateTextNode(string data)
        => new(data) { OwnerDocument = this };

    public Comment CreateComment(string data)
        => new(data) { OwnerDocument = this };

    public CData CreateCDataSection(string data)
        => new(data) { OwnerDocument = this };

    public ProcessingInstruction CreateProcessingInstruction(string target, string data)
        => new(target, data) { OwnerDocument = this };

    public DocumentFragment CreateDocumentFragment()
        => new() { OwnerDocument = this };

    public DocumentType CreateDocumentType(string name, string publicId = "", string systemId = "")
        => new(name, publicId, systemId) { OwnerDocument = this };

    public Element? DocumentElement
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
                if (child is Element element) return element;
            return null;
        }
    }

    public Element? Head
    {
        get
        {
            foreach (var node in Descendants())
                if (node is Element { LocalName: "head" } element) return element;
            return null;
        }
    }

    public Element? Body
    {
        get
        {
            foreach (var node in Descendants())
                if (node is Element { LocalName: "body" } element) return element;
            return null;
        }
    }

    public Element? GetElementById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var node in Descendants())
            if (node is Element element && element.Id == id) return element;
        return null;
    }

    public IReadOnlyList<Element> GetElementsByTagName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new LiveElementCollection(this, element =>
            name == "*" || element.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<Element> GetElementsByClassName(string names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var classes = names.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new LiveElementCollection(this, element =>
            classes.Length > 0 && classes.All(element.ClassList.Contains));
    }
}

public enum QuirksMode
{
    NoQuirks,
    LimitedQuirks,
    Quirks,
}
