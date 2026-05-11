namespace Tessera.Dom;

/// <summary>
/// Root of a DOM tree. The full <see cref="Document"/> per 05_DOM.md grows
/// methods like <c>CreateElement</c>, <c>QuerySelector</c>, mutation observers,
/// and so on; M0 needs only enough to be a container and bump a mutation version.
/// </summary>
public sealed class Document : Node
{
    public override NodeKind Kind => NodeKind.Document;

    public override Document? OwnerDocument
    {
        get => this;
        internal set { /* documents own themselves; setter is a no-op for the tree machinery. */ }
    }

    /// <summary>
    /// Incremented on every tree mutation. Live collections snapshot this and
    /// re-materialize when stale — see 05_DOM.md §Design choices.
    /// </summary>
    public int MutationVersion { get; private set; }

    internal void BumpMutationVersion() => MutationVersion++;

    public Element CreateElement(string tagName)
    {
        var el = new Element(tagName.ToLowerInvariant()) { OwnerDocument = this };
        return el;
    }

    public Text CreateTextNode(string data)
        => new(data) { OwnerDocument = this };

    /// <summary>Convenience: the root <c>&lt;html&gt;</c> element, or null if not yet inserted.</summary>
    public Element? DocumentElement
    {
        get
        {
            for (var c = FirstChild; c is not null; c = c.NextSibling)
                if (c is Element e) return e;
            return null;
        }
    }

    /// <summary>Convenience: the first <c>&lt;body&gt;</c> descendant, or null if absent.</summary>
    public Element? Body
    {
        get
        {
            foreach (var n in Descendants())
                if (n is Element e && e.TagName == "body") return e;
            return null;
        }
    }
}
