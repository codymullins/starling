using Starling.Dom.Events;

namespace Starling.Dom;

/// <summary>
/// Base DOM node. v1 keeps children as a simple doubly-linked list inside the
/// parent (O(1) Append/Remove, O(n) by-index lookup). See 05_DOM.md §Design choices.
/// </summary>
/// <remarks>
/// Inherits <see cref="EventTarget"/> so every node can have listeners attached
/// per [DOM §4.4](https://dom.spec.whatwg.org/#interface-node).
/// </remarks>
public abstract class Node : EventTarget
{
    /// <summary>DOM §6.3.3 — NodeIterator removal steps. Bindings layer
    /// subscribes here so live iterators can update their reference node when
    /// a node is removed from the tree. Called with (ownerDocument, removedNode)
    /// before the parent link is cleared. Null when no iterators are active.</summary>
    public static Action<Document, Node>? NodeRemovedHook;

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
        NotifyConnected(child);
        return child;
    }

    /// <summary>If <paramref name="inserted"/> is now connected to a document
    /// that has a host hook attached, raise the connection notification for the
    /// inserted node and every descendant. The engine uses this to discover
    /// <c>&lt;script&gt;</c> elements injected at runtime; pure-DOM callers pay
    /// only a null check.</summary>
    private void NotifyConnected(Node inserted)
    {
        var document = OwnerDocument ?? (this as Document);
        if (document?.NodeConnected is null) return;

        document.NotifyNodeConnected(inserted);
        foreach (var descendant in inserted.Descendants())
            document.NotifyNodeConnected(descendant);
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

        // DOM §6.3.3: notify live NodeIterators before the parent link is cleared.
        if (NodeRemovedHook is { } hook)
        {
            var doc = OwnerDocument ?? (this is Document d ? d : null);
            if (doc is not null) hook(doc, this);
        }

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
        // Iterative pre-order walk avoids unbounded recursion on hostile DOM depth.
        var stack = new Stack<Node>();
        for (var child = LastChild; child is not null; child = child.PreviousSibling)
            stack.Push(child);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
                stack.Push(child);
        }
    }

    public IEnumerable<Element> DescendantElements() => Descendants().OfType<Element>();

    // ---- Namespace lookup (DOM §4.4 "locate a namespace / prefix").
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    public string? LookupNamespaceURI(string? prefix)
        => LocateNamespace(this, string.IsNullOrEmpty(prefix) ? null : prefix);

    public bool IsDefaultNamespace(string? @namespace)
        => LookupNamespaceURI(null) == (string.IsNullOrEmpty(@namespace) ? null : @namespace);

    public string? LookupPrefix(string? @namespace)
    {
        if (string.IsNullOrEmpty(@namespace)) return null;
        return LocatePrefix(this is Document d ? d.DocumentElement : this as Element ?? ParentNode as Element, @namespace);
    }

    private static string? LocateNamespace(Node? node, string? prefix)
    {
        switch (node)
        {
            case Element el:
                if (!string.IsNullOrEmpty(el.Namespace) && el.Prefix == prefix) return el.Namespace;
                foreach (var a in el.Attributes)
                {
                    if (a.Namespace != XmlnsNamespace) continue;
                    var (pfx, local) = SplitName(a.Name);
                    if (pfx == "xmlns" && local == prefix) return string.IsNullOrEmpty(a.Value) ? null : a.Value;
                    if (pfx is null && a.Name == "xmlns" && prefix is null) return string.IsNullOrEmpty(a.Value) ? null : a.Value;
                }
                // Walk up to element ancestors only — stop at the document to avoid
                // documentElement<->Document recursion.
                return el.ParentNode is Element ep ? LocateNamespace(ep, prefix) : null;
            case Document doc:
                return doc.DocumentElement is { } de ? LocateNamespace(de, prefix) : null;
            default:
                return node?.ParentNode is Element p ? LocateNamespace(p, prefix) : null;
        }
    }

    private static string? LocatePrefix(Element? el, string ns)
    {
        for (var node = el; node is not null; node = node.ParentNode as Element)
        {
            if (node.Namespace == ns && node.Prefix is not null) return node.Prefix;
            foreach (var a in node.Attributes)
            {
                if (a.Namespace != XmlnsNamespace) continue;
                var (pfx, local) = SplitName(a.Name);
                if (pfx == "xmlns" && a.Value == ns) return local;
            }
        }
        return null;
    }

    private static (string? Prefix, string Local) SplitName(string name)
    {
        var i = name.IndexOf(':', StringComparison.Ordinal);
        return i >= 0 ? (name[..i], name[(i + 1)..]) : (null, name);
    }

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
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is not Document)
                node.OwnerDocument = document;

            for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
                stack.Push(child);
        }
    }
}
