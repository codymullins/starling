namespace Starling.Dom;

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

    /// <summary>
    /// The element with keyboard focus — HTML's <c>document.activeElement</c>.
    /// Set by the shell when the user clicks a focusable control and by
    /// <c>element.focus()</c>/<c>.blur()</c>; read by the <c>:focus</c> cascade,
    /// the text-editing pipeline, and the <c>activeElement</c> JS accessor.
    /// Null when nothing is focused.
    /// </summary>
    public Element? FocusedElement { get; set; }

    /// <summary>
    /// Host hook fired when a node is connected into this document's tree (via
    /// <see cref="Node.InsertBefore"/>). The engine subscribes to this so that
    /// <c>&lt;script&gt;</c> elements created and appended at runtime by JS are
    /// fetched and executed through the same compile+run path as parser-found
    /// scripts. Null when no host is attached (pure-DOM usage). Kept off the
    /// public surface — only the engine wires it during script execution.
    /// </summary>
    internal Action<Node>? NodeConnected { get; set; }

    /// <summary>Raise <see cref="NodeConnected"/> for <paramref name="node"/>
    /// if a host has subscribed. Called from the tree-mutation path.</summary>
    internal void NotifyNodeConnected(Node node) => NodeConnected?.Invoke(node);

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

    /// <summary>DOM §4.5 createElementNS — preserves the qualified name's case and
    /// splits the prefix (unlike <see cref="CreateElement(string,string?)"/>).</summary>
    public Element CreateElementNS(string? @namespace, string qualifiedName)
    {
        var e = Element.CreateNamespaced(@namespace, qualifiedName);
        e.OwnerDocument = this;
        return e;
    }

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

    public IReadOnlyList<Element> GetElementsByTagNameNS(string? @namespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var anyNs = @namespace == "*";
        var anyLocal = localName == "*";
        var ns = string.IsNullOrEmpty(@namespace) ? null : @namespace;
        return new LiveElementCollection(this, element =>
            (anyNs || string.Equals(element.Namespace, ns, StringComparison.Ordinal))
            && (anyLocal || element.LocalName.Equals(localName, StringComparison.Ordinal)));
    }

    /// <summary>
    /// DOM §4.5.1 — create and initialize a new Document with a minimal HTML
    /// skeleton: a doctype, an &lt;html&gt; root, a &lt;head&gt; (optionally with
    /// a &lt;title&gt; child whose text is <paramref name="title"/>), and a
    /// &lt;body&gt;. Returns the document ready for use as an off-screen context
    /// for fragment parsing (e.g. DOMImplementation.createHTMLDocument).
    /// </summary>
    /// <param name="title">
    /// The text content for the &lt;title&gt; element. When <c>null</c> the spec
    /// says no &lt;title&gt; element is created; an empty string creates a
    /// &lt;title&gt; with empty text (matching jQuery's
    /// <c>createHTMLDocument("")</c> call).
    /// </param>
    public static Document CreateHtmlDocument(string? title = null)
    {
        var doc = new Document();

        // 1. DOCTYPE
        var doctype = doc.CreateDocumentType("html");
        doc.AppendChild(doctype);

        // 2. <html>
        var html = doc.CreateElement("html");
        doc.AppendChild(html);

        // 3. <head> (+ optional <title>)
        var head = doc.CreateElement("head");
        html.AppendChild(head);
        if (title is not null)
        {
            var titleEl = doc.CreateElement("title");
            titleEl.AppendChild(doc.CreateTextNode(title));
            head.AppendChild(titleEl);
        }

        // 4. <body>
        var body = doc.CreateElement("body");
        html.AppendChild(body);

        return doc;
    }
}

public enum QuirksMode
{
    NoQuirks,
    LimitedQuirks,
    Quirks,
}
