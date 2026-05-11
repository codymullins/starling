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

    /// <summary>
    /// Append <paramref name="child"/> as the last child. Reparents from any
    /// existing tree position. Returns the inserted node for chaining.
    /// </summary>
    public Node AppendChild(Node child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child == this)
            throw new InvalidOperationException("A node cannot be its own child.");

        child.RemoveFromParent();
        child.ParentNode = this;
        child.OwnerDocument = OwnerDocument ?? (this as Document);

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
        OnTreeMutated();
        return child;
    }

    /// <summary>Detach this node from its parent. No-op if already orphaned.</summary>
    public void RemoveFromParent()
    {
        var p = ParentNode;
        if (p is null) return;

        if (PreviousSibling is not null) PreviousSibling.NextSibling = NextSibling;
        else p.FirstChild = NextSibling;

        if (NextSibling is not null) NextSibling.PreviousSibling = PreviousSibling;
        else p.LastChild = PreviousSibling;

        ParentNode = null;
        PreviousSibling = null;
        NextSibling = null;
        p.OnTreeMutated();
    }

    /// <summary>Children of this node, in document order. Walks the linked list.</summary>
    public IEnumerable<Node> ChildNodes
    {
        get
        {
            for (var c = FirstChild; c is not null; c = c.NextSibling)
                yield return c;
        }
    }

    /// <summary>Recursive descendants in document order (pre-order traversal).</summary>
    public IEnumerable<Node> Descendants()
    {
        for (var c = FirstChild; c is not null; c = c.NextSibling)
        {
            yield return c;
            foreach (var d in c.Descendants())
                yield return d;
        }
    }

    /// <summary>Concatenation of all descendant Text nodes' data. Equivalent to <c>Node.textContent</c>.</summary>
    public string TextContent
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var n in Descendants())
                if (n is Text t) sb.Append(t.Data);
            return sb.ToString();
        }
    }

    /// <summary>Hook for live-collection invalidation. Walks up to the owner document.</summary>
    protected virtual void OnTreeMutated()
    {
        if (OwnerDocument is { } d) d.BumpMutationVersion();
        else ParentNode?.OnTreeMutated();
    }
}
