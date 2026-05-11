namespace Tessera.Dom;

/// <summary>
/// Base DOM node. v1 keeps children as a simple doubly-linked list inside the
/// parent (O(1) Append/Remove, O(n) by-index lookup). See 05_DOM.md §Design choices.
/// </summary>
public abstract class Node
{
    public abstract NodeKind Kind { get; }

    public Node? ParentNode { get; internal set; }
    public Node? PreviousSibling { get; internal set; }
    public Node? NextSibling { get; internal set; }
    public Node? FirstChild { get; internal set; }
    public Node? LastChild { get; internal set; }

    public virtual Document? OwnerDocument { get; internal set; }

    public virtual string NodeName => Kind.ToString();

    public virtual string? NodeValue
    {
        get => null;
        set { }
    }

    public Node AppendChild(Node child) => InsertBefore(child, null);

    public Node InsertBefore(Node child, Node? referenceChild)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (referenceChild is not null && referenceChild.ParentNode != this)
            throw new InvalidOperationException("The reference child is not a child of this node.");
        if (child == referenceChild)
            return child;
        if (child == this)
            throw new InvalidOperationException("A node cannot be its own child.");
        for (var ancestor = this.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
        {
            if (ancestor == child)
                throw new InvalidOperationException("A node cannot be inserted into one of its descendants.");
        }

        if (child is DocumentFragment fragment)
        {
            var next = fragment.FirstChild;
            while (next is not null)
            {
                var current = next;
                next = current.NextSibling;
                InsertBefore(current, referenceChild);
            }
            return fragment;
        }

        child.RemoveFromParent();
        child.ParentNode = this;
        child.SetOwnerDocumentRecursive(OwnerDocument ?? (this as Document));

        if (referenceChild is null)
        {
            if (LastChild is null)
            {
                FirstChild = child;
                LastChild = child;
            }
            else
            {
                LastChild.NextSibling = child;
                child.PreviousSibling = LastChild;
                LastChild = child;
            }
        }
        else
        {
            var previous = referenceChild.PreviousSibling;
            child.NextSibling = referenceChild;
            child.PreviousSibling = previous;
            referenceChild.PreviousSibling = child;
            if (previous is null)
                FirstChild = child;
            else
                previous.NextSibling = child;
        }

        OnTreeMutated();
        return child;
    }

    public Node ReplaceChild(Node newChild, Node oldChild)
    {
        ArgumentNullException.ThrowIfNull(newChild);
        ArgumentNullException.ThrowIfNull(oldChild);
        if (oldChild.ParentNode != this)
            throw new InvalidOperationException("The old child is not a child of this node.");

        InsertBefore(newChild, oldChild);
        RemoveChild(oldChild);
        return oldChild;
    }

    public Node RemoveChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.ParentNode != this)
            throw new InvalidOperationException("The node is not a child of this node.");

        child.RemoveFromParent();
        return child;
    }

    /// <summary>Detach this node from its parent. No-op if already orphaned.</summary>
    public void RemoveFromParent()
    {
        var parent = ParentNode;
        if (parent is null) return;

        if (PreviousSibling is not null) PreviousSibling.NextSibling = NextSibling;
        else parent.FirstChild = NextSibling;

        if (NextSibling is not null) NextSibling.PreviousSibling = PreviousSibling;
        else parent.LastChild = PreviousSibling;

        ParentNode = null;
        PreviousSibling = null;
        NextSibling = null;
        parent.OnTreeMutated();
    }

    public IEnumerable<Node> ChildNodes
    {
        get
        {
            for (var child = FirstChild; child is not null; child = child.NextSibling)
                yield return child;
        }
    }

    public IEnumerable<Node> Descendants()
    {
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            yield return child;
            foreach (var descendant in child.Descendants())
                yield return descendant;
        }
    }

    public IEnumerable<Element> DescendantElements() => Descendants().OfType<Element>();

    public virtual string TextContent
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var node in Descendants())
            {
                if (node is Text text) sb.Append(text.Data);
                else if (node is CData cdata) sb.Append(cdata.Data);
            }
            return sb.ToString();
        }
        set
        {
            while (FirstChild is not null)
                RemoveChild(FirstChild);

            if (!string.IsNullOrEmpty(value))
            {
                var document = OwnerDocument ?? this as Document;
                AppendChild(document is null
                    ? new Text(value)
                    : document.CreateTextNode(value));
            }
        }
    }

    protected virtual void OnTreeMutated()
    {
        if (OwnerDocument is { } document) document.BumpMutationVersion();
        else ParentNode?.OnTreeMutated();
    }

    internal void SetOwnerDocumentRecursive(Document? document)
    {
        if (this is not Document)
            OwnerDocument = document;

        for (var child = FirstChild; child is not null; child = child.NextSibling)
            child.SetOwnerDocumentRecursive(document);
    }
}
