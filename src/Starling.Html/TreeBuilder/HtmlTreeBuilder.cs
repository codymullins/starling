using System.Diagnostics;
using System.Text;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

/// <summary>
/// WHATWG HTML §13.2.6 tree construction stage. This is a pragmatic subset:
/// the seven baseline insertion modes (Initial, BeforeHtml, BeforeHead, InHead,
/// AfterHead, InBody, Text, AfterBody, AfterAfterBody) plus implicit
/// open/close handling for paragraphs, headings, list items, and the
/// principal sectioning elements.
/// </summary>
/// <remarks>
/// <b>Known simplifications</b> (vs. full spec):
/// <list type="bullet">
///   <item>The list of active formatting elements and the adoption agency
///         algorithm are not implemented; mis-nested formatting tags
///         (e.g. <c>&lt;b&gt;&lt;i&gt;x&lt;/b&gt;y&lt;/i&gt;</c>) produce a
///         valid but shallowly-nested tree.</item>
///   <item>Table insertion uses InBody with no foster parenting; well-formed
///         tables parse correctly, but stray inter-cell text is appended
///         rather than re-parented.</item>
///   <item>Framesets, <c>&lt;template&gt;</c> contents, and the MathML/SVG
///         foreign-content modes are deferred.</item>
/// </list>
/// </remarks>
public sealed class HtmlTreeBuilder
{
    private readonly HtmlTokenizer _tokenizer;
    private readonly Document _document = new();
    private readonly StackOfOpenElements _openElements = new();
    private readonly StringBuilder _pendingText = new();
    private readonly IDiagnostics _diag;
    private readonly CountingParseErrorSink? _errorCounter;
    private readonly bool _scriptingEnabled;

    private Element? _headElement;
    private Element? _bodyElement;
    private InsertionMode _mode = InsertionMode.Initial;
    private InsertionMode _originalMode = InsertionMode.Initial;
    private int _tokenCount;

    /// <summary>
    /// Non-null when this builder is running the HTML fragment parsing algorithm
    /// (§13.4). The fragment's parsing context element steers initial tokenizer
    /// state and insertion mode; the resulting nodes are the children of the
    /// synthetic <c>html</c> root rather than the document tree.
    /// </summary>
    private readonly Element? _fragmentContext;

    public HtmlTreeBuilder(HtmlTokenizer tokenizer, IDiagnostics? diagnostics = null,
        CountingParseErrorSink? errorCounter = null, bool scriptingEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _errorCounter = errorCounter;
        _scriptingEnabled = scriptingEnabled;
    }

    private HtmlTreeBuilder(HtmlTokenizer tokenizer, Element fragmentContext, IDiagnostics? diagnostics,
        CountingParseErrorSink? errorCounter)
        : this(tokenizer, diagnostics, errorCounter)
    {
        _fragmentContext = fragmentContext;
    }

    /// <summary>
    /// Runs tree construction over <paramref name="html"/>.
    /// </summary>
    /// <param name="html">The HTML source to parse.</param>
    /// <param name="diagnostics">Optional diagnostics sink for spans/counters.</param>
    /// <param name="scriptingEnabled">
    /// WHATWG HTML §13.2 "scripting flag". When <c>true</c> (the engine, which
    /// runs JS) a <c>&lt;noscript&gt;</c> start tag in the "in head" insertion
    /// mode follows the generic raw text element parsing algorithm
    /// (§13.2.6.4.4) so its contents become an inert text node rather than
    /// parsed elements. When <c>false</c> (the html5lib conformance harness,
    /// which assumes scripting disabled) the legacy element-parsing behavior is
    /// preserved.
    /// </param>
    public static Document Parse(string html, IDiagnostics? diagnostics = null,
        bool scriptingEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        var diag = diagnostics ?? NoopDiagnostics.Instance;
        var errorCounter = new CountingParseErrorSink();
        var tokenizer = new HtmlTokenizer(errorCounter);
        tokenizer.Feed(html);
        tokenizer.EndOfInput();
        var builder = new HtmlTreeBuilder(tokenizer, diag, errorCounter, scriptingEnabled);
        return builder.Run();
    }

    /// <summary>
    /// HTML fragment parsing algorithm (§13.4). Parses <paramref name="markup"/>
    /// in the context of <paramref name="contextElement"/> and returns the
    /// resulting nodes as a detached <see cref="DocumentFragment"/> owned by
    /// <paramref name="ownerDocument"/> (so the new nodes share the element's
    /// document). The context element itself is not added to the output.
    /// </summary>
    public static DocumentFragment ParseFragment(string markup, Element contextElement,
        Document ownerDocument, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentNullException.ThrowIfNull(contextElement);
        ArgumentNullException.ThrowIfNull(ownerDocument);

        var diag = diagnostics ?? NoopDiagnostics.Instance;
        var errorCounter = new CountingParseErrorSink();
        var tokenizer = new HtmlTokenizer(errorCounter);
        // §13.4 step 4: set the tokenizer's state per the context element so RCDATA
        // / RAWTEXT / script / plaintext contexts treat their contents as text.
        tokenizer.SetState(InitialTokenizerStateFor(contextElement.LocalName));
        tokenizer.Feed(markup);
        tokenizer.EndOfInput();

        var builder = new HtmlTreeBuilder(tokenizer, contextElement, diag, errorCounter);
        return builder.RunFragment(ownerDocument);
    }

    private DocumentFragment RunFragment(Document ownerDocument)
    {
        using var _ = _diag.Span("html", "parse-fragment");

        // §13.4 steps 5-7: create a synthetic <html> root, push it as the only
        // open element, and reset the insertion mode appropriately for the
        // context element so its children land in the right place.
        var root = _document.CreateElement("html");
        _document.AppendChild(root);
        _openElements.Push(root);
        ResetInsertionModeForContext(_fragmentContext!.LocalName);

        while (_tokenizer.ReadToken() is { } token)
        {
            _tokenCount++;
            HandleToken(token);
            if (token is EndOfFileToken) break;
        }
        FlushText();

        // §13.4 step 14: the fragment is the child nodes of the root element.
        var fragment = ownerDocument.CreateDocumentFragment();
        var child = root.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            fragment.AppendChild(child); // re-parents into the fragment
            child = next;
        }
        return fragment;
    }

    /// <summary>
    /// §13.4 step 4 — the tokenizer state implied by the fragment's context
    /// element. Most contexts use the Data state; the text-content elements
    /// switch to RCDATA / RAWTEXT / script / plaintext.
    /// </summary>
    private static TokenizerState InitialTokenizerStateFor(string contextLocalName)
        => contextLocalName.ToLowerInvariant() switch
        {
            "title" or "textarea" => TokenizerState.Rcdata,
            "style" or "xmp" or "iframe" or "noembed" or "noframes" => TokenizerState.Rawtext,
            "script" => TokenizerState.ScriptData,
            "noscript" => TokenizerState.Rawtext, // scripting is enabled in this engine
            "plaintext" => TokenizerState.Plaintext,
            _ => TokenizerState.Data,
        };

    /// <summary>
    /// §13.4 step 7 — pick the insertion mode for fragment parsing from the
    /// context element. The common case (any descendant of body, or arbitrary
    /// element) is InBody; head/text contexts get their own modes so that, e.g.,
    /// parsing into a &lt;title&gt; produces a single text node.
    /// </summary>
    private void ResetInsertionModeForContext(string contextLocalName)
    {
        switch (contextLocalName.ToLowerInvariant())
        {
            case "head":
                _mode = InsertionMode.InHead;
                break;
            case "html":
                _mode = InsertionMode.BeforeHead;
                break;
            case "title":
            case "textarea":
            case "style":
            case "xmp":
            case "iframe":
            case "noembed":
            case "noframes":
            case "noscript":
            case "script":
                // Text contexts: the contents are pure character data; route
                // through the Text mode so the tokenizer's RCDATA/RAWTEXT output
                // becomes a single coalesced text node under the root.
                _originalMode = InsertionMode.InBody;
                _mode = InsertionMode.Text;
                break;
            default:
                _mode = InsertionMode.InBody;
                break;
        }
    }

    public Document Run()
    {
        using var _ = _diag.Span("html", "parse");
        try
        {
            while (_tokenizer.ReadToken() is { } token)
            {
                _tokenCount++;
                HandleToken(token);
                if (token is EndOfFileToken) break;
            }
            FlushText();

            var errorCount = _errorCounter?.Count ?? 0;
            Activity.Current?.SetTag("html.tokens", _tokenCount);
            Activity.Current?.SetTag("html.parse_errors", errorCount);
            _diag.Counter("html.parses", 1);
            if (errorCount > 0)
                _diag.Counter("html.parse_errors", errorCount);
            return _document;
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private void HandleToken(HtmlToken token)
    {
        // Text accumulation lives across many tokens; flush only when a
        // non-character token boundary arrives.
        if (token is not CharacterToken)
            FlushText();

        switch (_mode)
        {
            case InsertionMode.Initial: HandleInitial(token); break;
            case InsertionMode.BeforeHtml: HandleBeforeHtml(token); break;
            case InsertionMode.BeforeHead: HandleBeforeHead(token); break;
            case InsertionMode.InHead: HandleInHead(token); break;
            case InsertionMode.AfterHead: HandleAfterHead(token); break;
            case InsertionMode.InBody: HandleInBody(token); break;
            case InsertionMode.Text: HandleText(token); break;
            case InsertionMode.AfterBody: HandleAfterBody(token); break;
            case InsertionMode.AfterAfterBody: HandleAfterAfterBody(token); break;
        }
    }

    // ------------------------------------------------------------------ helpers

    private Element CreateElement(StartTagToken token)
    {
        var element = _document.CreateElement(token.Name);
        foreach (var attr in token.Attributes)
            element.SetAttribute(attr.Name, attr.Value);
        return element;
    }

    /// <summary>Insert an element as a child of the current insertion location.</summary>
    private Element InsertElement(StartTagToken token)
    {
        var element = CreateElement(token);
        InsertionTarget().AppendChild(element);
        _openElements.Push(element);
        return element;
    }

    private Node InsertionTarget()
        => _openElements.IsEmpty ? _document : _openElements.Current;

    private void InsertText(string data)
    {
        if (data.Length == 0) return;
        var parent = InsertionTarget();
        // Coalesce with a preceding Text node when possible.
        if (parent.LastChild is Text existing)
            existing.Data += data;
        else
            parent.AppendChild(_document.CreateText(data));
    }

    private void InsertComment(CommentToken token, Node? overrideParent = null)
    {
        var parent = overrideParent ?? InsertionTarget();
        parent.AppendChild(_document.CreateComment(token.Data));
    }

    private void FlushText()
    {
        if (_pendingText.Length == 0) return;
        InsertText(_pendingText.ToString());
        _pendingText.Clear();
    }

    private void AppendChar(int codePoint)
    {
        if (codePoint <= char.MaxValue) _pendingText.Append((char)codePoint);
        else _pendingText.Append(char.ConvertFromUtf32(codePoint));
    }

    private static bool IsWhitespaceChar(int c)
        => c == '\t' || c == '\n' || c == '\f' || c == '\r' || c == ' ';

    private void GenerateImpliedEndTags(string? except = null)
    {
        while (!_openElements.IsEmpty)
        {
            var name = _openElements.Current.LocalName;
            if (except is not null && string.Equals(name, except, StringComparison.OrdinalIgnoreCase))
                return;
            if (IsImpliedEndTag(name)) _openElements.Pop();
            else return;
        }
    }

    private static bool IsImpliedEndTag(string localName) => localName.ToLowerInvariant() switch
    {
        "dd" or "dt" or "li" or "optgroup" or "option" or "p" or "rb"
            or "rp" or "rt" or "rtc" => true,
        _ => false,
    };

    private static bool IsSpecialBlock(string localName) => localName.ToLowerInvariant() switch
    {
        "address" or "article" or "aside" or "blockquote" or "center" or "details"
            or "dialog" or "dir" or "div" or "dl" or "fieldset" or "figcaption"
            or "figure" or "footer" or "header" or "hgroup" or "main" or "menu"
            or "nav" or "ol" or "p" or "search" or "section" or "summary" or "ul" => true,
        _ => false,
    };

    private void ClosePIfOpen()
    {
        if (_openElements.HasInButtonScope("p"))
        {
            GenerateImpliedEndTags(except: "p");
            _openElements.PopUntilNamed("p");
        }
    }

    // ----------------------------------------------------------------- Initial

    private void HandleInitial(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                return; // Whitespace before DOCTYPE is ignored.
            case CommentToken comment:
                InsertComment(comment, overrideParent: _document);
                return;
            case DoctypeToken doctype:
                _document.AppendChild(_document.CreateDocumentType(doctype.Name ?? "", doctype.PublicId ?? "", doctype.SystemId ?? ""));
                if (doctype.ForceQuirks || !IsHtml5Doctype(doctype))
                    _document.Mode = QuirksMode.Quirks;
                _mode = InsertionMode.BeforeHtml;
                return;
        }

        // Anything else: treat as no DOCTYPE seen, set quirks, fall through.
        _document.Mode = QuirksMode.Quirks;
        _mode = InsertionMode.BeforeHtml;
        HandleBeforeHtml(token);
    }

    private static bool IsHtml5Doctype(DoctypeToken d)
        => string.Equals(d.Name, "html", StringComparison.OrdinalIgnoreCase) &&
           string.IsNullOrEmpty(d.PublicId) &&
           (string.IsNullOrEmpty(d.SystemId) || d.SystemId.Equals("about:legacy-compat", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- BeforeHtml

    private void HandleBeforeHtml(HtmlToken token)
    {
        switch (token)
        {
            case DoctypeToken: return; // Parse error, ignore.
            case CommentToken comment: InsertComment(comment, overrideParent: _document); return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): return;
            case StartTagToken { Name: "html" } start:
                {
                    var html = CreateElement(start);
                    _document.AppendChild(html);
                    _openElements.Push(html);
                    _mode = InsertionMode.BeforeHead;
                    return;
                }
            case EndTagToken end when end.Name is "head" or "body" or "html" or "br":
                break; // Fall through to anything-else.
            case EndTagToken: return; // Parse error.
        }

        // Anything else: implicitly create <html>, then reprocess.
        var implicitHtml = _document.CreateElement("html");
        _document.AppendChild(implicitHtml);
        _openElements.Push(implicitHtml);
        _mode = InsertionMode.BeforeHead;
        HandleBeforeHead(token);
    }

    // ---------------------------------------------------------------- BeforeHead

    private void HandleBeforeHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" } start: HandleInBody(start); return;
            case StartTagToken { Name: "head" } start:
                _headElement = InsertElement(start);
                _mode = InsertionMode.InHead;
                return;
            case EndTagToken end when end.Name is "head" or "body" or "html" or "br":
                break;
            case EndTagToken: return;
        }

        // Anything else: implicit <head>, then reprocess in InHead.
        _headElement = InsertElement(new StartTagToken("head", Array.Empty<HtmlAttribute>(), SelfClosing: false));
        _mode = InsertionMode.InHead;
        HandleInHead(token);
    }

    // ------------------------------------------------------------------- InHead

    private void HandleInHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertText(((char)c.CodePoint).ToString());
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" } start: HandleInBody(start); return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound" or "link" or "meta":
                InsertElement(start);
                _openElements.Pop(); // Void element.
                return;
            case StartTagToken start when start.Name == "title":
                InsertElement(start);
                _originalMode = _mode;
                _mode = InsertionMode.Text;
                _tokenizer.SetState(TokenizerState.Rcdata);
                return;
            case StartTagToken start when start.Name is "noframes" or "style":
                InsertElement(start);
                _originalMode = _mode;
                _mode = InsertionMode.Text;
                _tokenizer.SetState(TokenizerState.Rawtext);
                return;
            // §13.2.6.4.4 "in head" — <noscript> start tag. When the scripting
            // flag is ENABLED, follow the generic raw text element parsing
            // algorithm: the contents become an inert text node, never elements.
            // When DISABLED the spec switches to the "in head noscript" mode and
            // parses the contents as elements; this builder doesn't model that
            // sub-mode, so we leave the scripting-disabled path on its existing
            // "anything else" fall-through (html5lib conformance default).
            case StartTagToken start when start.Name == "noscript" && _scriptingEnabled:
                InsertElement(start);
                _originalMode = _mode;
                _mode = InsertionMode.Text;
                _tokenizer.SetState(TokenizerState.Rawtext);
                return;
            case StartTagToken start when start.Name == "script":
                InsertElement(start);
                _originalMode = _mode;
                _mode = InsertionMode.Text;
                _tokenizer.SetState(TokenizerState.ScriptData);
                return;
            case EndTagToken end when end.Name == "head":
                _openElements.Pop();
                _mode = InsertionMode.AfterHead;
                return;
            case EndTagToken end when end.Name is "body" or "html" or "br":
                break;
            case EndTagToken: return;
            case StartTagToken start when start.Name == "head": return;
        }

        // Anything else: pop head, switch to AfterHead, reprocess.
        _openElements.Pop();
        _mode = InsertionMode.AfterHead;
        HandleAfterHead(token);
    }

    // ----------------------------------------------------------------- AfterHead

    private void HandleAfterHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertText(((char)c.CodePoint).ToString());
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" } start: HandleInBody(start); return;
            case StartTagToken { Name: "body" } start:
                _bodyElement = InsertElement(start);
                _mode = InsertionMode.InBody;
                return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound"
                                                       or "link" or "meta" or "noframes"
                                                       or "script" or "style" or "template"
                                                       or "title":
                // Push head back onto the stack temporarily so InHead inserts there, then pop.
                if (_headElement is not null) _openElements.Push(_headElement);
                HandleInHead(token);
                if (_headElement is not null && _openElements.Contains(_headElement))
                {
                    // Pop head if it ended up on top after InHead processing.
                    while (!_openElements.IsEmpty && _openElements.Current == _headElement)
                        _openElements.Pop();
                }
                return;
            case EndTagToken end when end.Name is "body" or "html" or "br": break;
            case EndTagToken: return;
            case StartTagToken start when start.Name == "head": return;
        }

        // Anything else: implicit <body>, switch, reprocess.
        _bodyElement = InsertElement(new StartTagToken("body", Array.Empty<HtmlAttribute>(), SelfClosing: false));
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }

    // -------------------------------------------------------------------- InBody

    private void HandleInBody(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c:
                if (c.CodePoint == 0) return; // U+0000 NULL is a parse error, ignore in this mode.
                AppendChar(c.CodePoint);
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case EndOfFileToken: return;
            case StartTagToken { Name: "html" } start:
                MergeAttributesInto(_openElements.Count > 0 ? _openElements[0] : null, start);
                return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound"
                                                       or "link" or "meta" or "noframes"
                                                       or "script" or "style" or "title":
                HandleInHead(start);
                return;
            case StartTagToken start when start.Name == "body":
                MergeAttributesInto(_bodyElement, start);
                return;
            case StartTagToken start when IsSpecialBlock(start.Name):
                ClosePIfOpen();
                InsertElement(start);
                return;
            case StartTagToken start when start.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                ClosePIfOpen();
                if (IsHeading(_openElements.Current.LocalName)) _openElements.Pop();
                InsertElement(start);
                return;
            case StartTagToken start when start.Name is "pre" or "listing":
                ClosePIfOpen();
                InsertElement(start);
                return;
            case StartTagToken start when start.Name is "li":
                CloseListItem();
                ClosePIfOpen();
                InsertElement(start);
                return;
            case StartTagToken start when start.Name is "dt" or "dd":
                CloseDefinitionItem();
                ClosePIfOpen();
                InsertElement(start);
                return;
            case StartTagToken start when start.Name == "button":
                if (_openElements.HasInScope("button"))
                {
                    GenerateImpliedEndTags();
                    _openElements.PopUntilNamed("button");
                }
                InsertElement(start);
                return;
            case StartTagToken start when start.Name is "area" or "br" or "embed" or "img"
                                                       or "keygen" or "wbr" or "input"
                                                       or "param" or "source" or "track"
                                                       or "hr" or "col":
                InsertElement(start);
                _openElements.Pop();
                return;
            case StartTagToken start when start.Name == "textarea":
                InsertElement(start);
                _originalMode = _mode;
                _mode = InsertionMode.Text;
                _tokenizer.SetState(TokenizerState.Rcdata);
                return;
            case StartTagToken start:
                InsertElement(start);
                if (start.SelfClosing) _openElements.Pop();
                return;
            case EndTagToken end when end.Name == "body":
                if (_openElements.HasInScope("body")) _mode = InsertionMode.AfterBody;
                return;
            case EndTagToken end when end.Name == "html":
                if (_openElements.HasInScope("body")) _mode = InsertionMode.AfterBody;
                HandleAfterBody(end);
                return;
            // </p> must be tested BEFORE the generic IsSpecialBlock case below:
            // `p` is itself in the implied-end-tag set, so the generic handler's
            // unbounded GenerateImpliedEndTags() would pop the <p> we're trying
            // to close, then PopUntilNamed("p") would scan the whole stack and
            // drain <body>/<html>. The HTML spec's "close a p element" step
            // exempts `p` from implied end tags for exactly this reason.
            case EndTagToken end when end.Name == "p":
                if (!_openElements.HasInButtonScope("p"))
                {
                    // Implicit <p> creation per spec; we approximate by inserting an empty one.
                    InsertElement(new StartTagToken("p", Array.Empty<HtmlAttribute>(), SelfClosing: false));
                }
                GenerateImpliedEndTags(except: "p");
                _openElements.PopUntilNamed("p");
                return;
            case EndTagToken end when IsSpecialBlock(end.Name):
                if (!_openElements.HasInScope(end.Name)) return;
                GenerateImpliedEndTags();
                _openElements.PopUntilNamed(end.Name);
                return;
            case EndTagToken end when end.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (!AnyHeadingInScope()) return;
                GenerateImpliedEndTags();
                while (!_openElements.IsEmpty && !IsHeading(_openElements.Current.LocalName))
                    _openElements.Pop();
                if (!_openElements.IsEmpty) _openElements.Pop();
                return;
            case EndTagToken end when end.Name == "li":
                if (!_openElements.HasInListItemScope("li")) return;
                GenerateImpliedEndTags(except: "li");
                _openElements.PopUntilNamed("li");
                return;
            case EndTagToken end when end.Name is "dt" or "dd":
                if (!_openElements.HasInScope(end.Name)) return;
                GenerateImpliedEndTags(except: end.Name);
                _openElements.PopUntilNamed(end.Name);
                return;
            case EndTagToken end when end.Name is "button":
                if (_openElements.HasInScope("button"))
                {
                    GenerateImpliedEndTags();
                    _openElements.PopUntilNamed("button");
                }
                return;
            case EndTagToken end:
                // Generic end tag: walk down stack and close if found in scope.
                for (var i = _openElements.Count - 1; i >= 0; i--)
                {
                    var node = _openElements[i];
                    if (string.Equals(node.LocalName, end.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        GenerateImpliedEndTags(except: end.Name);
                        _openElements.PopUntilNamed(end.Name);
                        return;
                    }
                    if (IsSpecialBlock(node.LocalName)) return; // Parse error.
                }
                return;
        }
    }

    private bool AnyHeadingInScope()
    {
        return _openElements.HasInScope("h1") || _openElements.HasInScope("h2")
            || _openElements.HasInScope("h3") || _openElements.HasInScope("h4")
            || _openElements.HasInScope("h5") || _openElements.HasInScope("h6");
    }

    private static bool IsHeading(string localName) => localName.ToLowerInvariant() switch
    {
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
        _ => false,
    };

    private void CloseListItem()
    {
        // Walk down the stack looking for an "li" before hitting a special block other than li/address/div/p.
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var name = _openElements[i].LocalName;
            if (string.Equals(name, "li", StringComparison.OrdinalIgnoreCase))
            {
                GenerateImpliedEndTags(except: "li");
                _openElements.PopUntilNamed("li");
                return;
            }
            if (IsSpecialBlock(name) && name is not ("address" or "div" or "p"))
                return;
        }
    }

    private void CloseDefinitionItem()
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var name = _openElements[i].LocalName;
            if (string.Equals(name, "dd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "dt", StringComparison.OrdinalIgnoreCase))
            {
                GenerateImpliedEndTags(except: name);
                _openElements.PopUntilNamed(name);
                return;
            }
            if (IsSpecialBlock(name) && name is not ("address" or "div" or "p"))
                return;
        }
    }

    private static void MergeAttributesInto(Element? target, StartTagToken start)
    {
        if (target is null) return;
        foreach (var attr in start.Attributes)
            if (!target.HasAttribute(attr.Name))
                target.SetAttribute(attr.Name, attr.Value);
    }

    // ---------------------------------------------------------------------- Text

    private void HandleText(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c:
                AppendChar(c.CodePoint);
                return;
            case EndTagToken:
                FlushText();
                _openElements.Pop();
                _mode = _originalMode;
                return;
            case EndOfFileToken:
                FlushText();
                if (!_openElements.IsEmpty) _openElements.Pop();
                _mode = _originalMode;
                return;
        }
    }

    // ------------------------------------------------------------------- AfterBody

    private void HandleAfterBody(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                HandleInBody(token); return;
            case CommentToken comment:
                // Comment is appended to the html element.
                if (_openElements.Count > 0) _openElements[0].AppendChild(_document.CreateComment(comment.Data));
                return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" } start: HandleInBody(start); return;
            case EndTagToken { Name: "html" }:
                _mode = InsertionMode.AfterAfterBody;
                return;
            case EndOfFileToken: return;
        }
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }

    // --------------------------------------------------------------- AfterAfterBody

    private void HandleAfterAfterBody(HtmlToken token)
    {
        switch (token)
        {
            case CommentToken comment: InsertComment(comment, overrideParent: _document); return;
            case DoctypeToken: return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): HandleInBody(token); return;
            case StartTagToken { Name: "html" } start: HandleInBody(start); return;
            case EndOfFileToken: return;
        }
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }
}
