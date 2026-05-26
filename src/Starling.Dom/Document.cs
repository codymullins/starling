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

    /// <summary>Subset of <see cref="MutationVersion"/> that only advances on
    /// mutations a built-in style/layout pass would actually care about
    /// (structural changes, text edits, and value changes to attributes
    /// flagged by <see cref="IsLayoutRelevantAttribute"/>). Used by the
    /// layout-cache invalidation check so a hot-path analytics burst that
    /// only writes <c>data-*</c> / <c>aria-*</c> / framework attributes
    /// doesn't tear down the cached layout.</summary>
    public int LayoutInvalidationVersion { get; private set; }

    internal void BumpLayoutInvalidationVersion() => LayoutInvalidationVersion++;

    /// <summary>Per-attribute-name bump counter (diagnostic). Engine reads
    /// this after script execution to surface "which attribute caused all the
    /// re-layouts" without needing a full mutation log.</summary>
    public readonly Dictionary<string, int> AttributeMutationCounts =
        new(StringComparer.OrdinalIgnoreCase);

    internal void NoteAttributeMutation(string attrName)
    {
        AttributeMutationCounts.TryGetValue(attrName, out var n);
        AttributeMutationCounts[attrName] = n + 1;
    }

    /// <summary>True iff a value change on <paramref name="attrName"/> can
    /// shift cascade or layout for an ordinary HTML page.</summary>
    /// <remarks>
    /// <para>Used to suppress <see cref="BumpMutationVersion"/> for
    /// accessibility / metadata attributes that real pages mutate frequently
    /// during their analytics passes but that no built-in style targets —
    /// <c>data-*</c>, <c>aria-*</c>, <c>role</c>, <c>jsname</c>, etc. On
    /// Google's homepage this turns ~350 of ~500 attribute mutations into
    /// no-op bumps, which keeps the layout cache valid through the analytics
    /// noise and skips redundant reflows.</para>
    /// <para><b>Correctness caveat:</b> a page whose author CSS uses an
    /// attribute selector (e.g. <c>[role="button"] { … }</c>) AND mutates
    /// that attribute via script will miss a cascade recompute until the
    /// next layout-relevant change. That combination is rare in the wild;
    /// the spec-correct alternative is selector-aware invalidation, which is
    /// a follow-up.</para>
    /// </remarks>
    public static bool IsLayoutRelevantAttribute(string attrName)
    {
        if (string.IsNullOrEmpty(attrName)) return true;
        if (attrName.StartsWith("data-", StringComparison.OrdinalIgnoreCase)) return false;
        if (attrName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)) return false;
        // js* covers Google's framework attributes (jsname, jscontroller,
        // jsaction, jsslot, jsl, etc.) — present on most modern Google pages
        // and never targeted by built-in styles.
        if (attrName.StartsWith("js", StringComparison.OrdinalIgnoreCase)
            && attrName.Length > 2 && char.IsLower(attrName[2]))
            return false;
        return attrName switch
        {
            "role" or "tabindex" or "title" or "alt"
                or "href" or "target" or "rel" or "download" or "ping"
                or "for" or "form" or "list" or "autocomplete"
                or "accesskey" or "contenteditable" or "draggable" or "spellcheck"
                or "translate" or "autocapitalize" or "enterkeyhint" or "inputmode"
                or "is" or "slot" or "part" or "exportparts"
                or "itemid" or "itemprop" or "itemref" or "itemscope" or "itemtype"
                => false,
            _ => true,
        };
    }

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

    /// <summary>True when this is an HTML document (created by the HTML parser
    /// or <c>createHTMLDocument</c>). False for XML documents created via
    /// <c>implementation.createDocument</c>. Affects name-casing in
    /// <c>createAttribute</c> (HTML lower-cases, XML preserves).</summary>
    public bool IsHtml { get; set; } = true;

    // ---- DOM §4.9 — createAttribute / createAttributeNS --------------------

    /// <summary>DOM §4.9 createAttribute — creates a new detached Attr node
    /// with the given local name and no namespace. Case is preserved by this
    /// method; the JS binding is responsible for lower-casing on HTML documents.</summary>
    public AttrNode CreateAttribute(string localName)
    {
        ArgumentException.ThrowIfNullOrEmpty(localName);
        return new AttrNode(localName) { OwnerDocument = this };
    }

    /// <summary>DOM §4.9 createAttributeNS — creates a new detached Attr node
    /// with a namespace URI and qualified name. Name validation and
    /// namespace/prefix consistency checks are enforced by the JS binding layer
    /// (ValidateQualifiedName) before calling this method.</summary>
    public AttrNode CreateAttributeNS(string? @namespace, string qualifiedName)
    {
        ArgumentException.ThrowIfNullOrEmpty(qualifiedName);
        var attr = AttrNode.CreateNamespaced(qualifiedName, @namespace);
        attr.OwnerDocument = this;
        return attr;
    }

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
