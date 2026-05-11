# 05 — DOM

## Scope

**In:** Node hierarchy, mutation methods, traversal, events, observers, ranges (minimal), HTMLElement subtypes used by layout/paint.
**Out:** Selectors ([06_CSS.md](06_CSS.md)), Shadow DOM v1 (sketched but unimplemented), custom elements (deferred), accessibility tree.

## Spec refs

- [SPEC: DOM Living Standard](https://dom.spec.whatwg.org/)
- [SPEC: HTML §3 — Document](https://html.spec.whatwg.org/multipage/dom.html)
- [SPEC: UI Events](https://w3c.github.io/uievents/)
- [SPEC: WebIDL](https://webidl.spec.whatwg.org/)

## Design choices

- **Reference-counted? No.** Use .NET GC. We don't share nodes across cycles because the DOM is a tree; the rare cyclic case (event listener closures) is handled by .NET GC's mark-and-sweep correctly.
- **Live collections.** `NodeList`/`HTMLCollection` returned by `getElementsByTagName` must be live. Use a versioned snapshot pattern: each `Document` has a `MutationVersion` int; collections cache `(version, materialized list)` and re-materialize on access if stale.
- **Insertion order.** All children kept in a doubly-linked list inside the parent. `Children` is `O(n)` to materialize but `Append`/`Remove` is `O(1)`.
- **`Element.Attributes`.** Small struct array (4-slot inline) for typical cases; falls back to `List<Attribute>`.

## Project layout

```
src/Tessera.Dom/
├── Tessera.Dom.csproj
├── Node.cs
├── NodeKind.cs
├── Document.cs
├── DocumentFragment.cs
├── DocumentType.cs
├── Element.cs
├── Attr.cs
├── Text.cs
├── Comment.cs
├── CData.cs
├── ProcessingInstruction.cs
├── NodeList.cs
├── HtmlCollection.cs
├── NamedNodeMap.cs
├── Events/
│   ├── EventTarget.cs
│   ├── Event.cs
│   ├── EventDispatcher.cs
│   └── EventTypes.cs
├── Mutation/
│   ├── MutationObserver.cs
│   ├── MutationRecord.cs
│   └── MutationObserverRegistry.cs
├── Traversal/
│   ├── TreeWalker.cs
│   └── NodeIterator.cs
├── Html/                          # HTMLElement subtypes
│   ├── HtmlElement.cs
│   ├── HtmlAnchorElement.cs
│   ├── HtmlInputElement.cs
│   ├── HtmlScriptElement.cs
│   ├── HtmlLinkElement.cs
│   ├── HtmlStyleElement.cs
│   ├── HtmlImageElement.cs
│   ├── HtmlBodyElement.cs
│   ├── HtmlHeadElement.cs
│   ├── HtmlHtmlElement.cs
│   ├── HtmlFormElement.cs
│   ├── HtmlButtonElement.cs
│   └── ...                        # ~30 total in v1
└── Shadow/                        # OUT-OF-SCOPE-V1; folder + stub interfaces only
    └── ShadowRoot.cs
```

## Node hierarchy

```
Node (abstract)
├── Document
├── DocumentType
├── DocumentFragment
├── Element
│   └── HtmlElement
│       ├── HtmlAnchorElement
│       ├── HtmlInputElement
│       ├── ... (~30)
│       └── (catch-all HtmlUnknownElement)
├── Attr                  (no longer a Node in the spec since 2013; we follow the modern shape)
├── CharacterData (abstract)
│   ├── Text
│   ├── Comment
│   └── CData
└── ProcessingInstruction
```

`Attr` was removed from the Node hierarchy in 2013's DOM4 erratum. **Follow the modern shape**: attributes are owned by `Element.Attributes`, not children of the element.

## Core types

```csharp
namespace Tessera.Dom;

public abstract class Node : EventTarget
{
    public NodeKind Kind { get; }
    public Document OwnerDocument { get; internal set; }
    public Node? ParentNode { get; internal set; }
    public Node? FirstChild { get; internal set; }
    public Node? LastChild { get; internal set; }
    public Node? PreviousSibling { get; internal set; }
    public Node? NextSibling { get; internal set; }
    public string? NodeName { get; }
    public string? NodeValue { get; set; }
    public string TextContent { get; set; }

    // Mutation primitives — spec section 4.2.3
    public Node AppendChild(Node child);
    public Node InsertBefore(Node child, Node? reference);
    public Node ReplaceChild(Node newChild, Node oldChild);
    public Node RemoveChild(Node child);

    // Tree-walk helpers
    public IEnumerable<Node> Descendants();
    public IEnumerable<Element> DescendantElements();
}

public enum NodeKind : byte
{
    Element = 1, Attribute = 2, Text = 3, CData = 4,
    EntityReference = 5, Entity = 6, ProcessingInstruction = 7,
    Comment = 8, Document = 9, DocumentType = 10,
    DocumentFragment = 11, Notation = 12,
}

public sealed class Document : Node
{
    public DocumentType? DocType { get; internal set; }
    public Element? DocumentElement { get; }     // usually <html>
    public Element? Head { get; }
    public HtmlBodyElement? Body { get; }
    public QuirksMode Mode { get; internal set; }
    public Url Url { get; init; }
    public Encoding CharacterSet { get; init; }
    public IReadOnlyList<StyleSheet> StyleSheets { get; }
    public uint MutationVersion { get; internal set; }   // bumped on every mutation

    public Element CreateElement(string localName, string? @namespace = null);
    public Text CreateText(string data);
    public Comment CreateComment(string data);
    public DocumentFragment CreateDocumentFragment();
    public Event CreateEvent(string type);

    // Selectors (calls into Tessera.Css):
    public Element? QuerySelector(string selectors);
    public IReadOnlyList<Element> QuerySelectorAll(string selectors);
    public Element? GetElementById(string id);
    public IReadOnlyList<Element> GetElementsByTagName(string name);   // LIVE
    public IReadOnlyList<Element> GetElementsByClassName(string names); // LIVE
}

public class Element : Node
{
    public string LocalName { get; }
    public string? Prefix { get; }
    public string Namespace { get; }   // "http://www.w3.org/1999/xhtml" for HTML
    public string TagName { get; }     // uppercase ASCII for HTML
    public NamedNodeMap Attributes { get; }
    public string Id { get; set; }     // proxy for the "id" attribute
    public DomTokenList ClassList { get; }

    public string? GetAttribute(string name);
    public void SetAttribute(string name, string value);
    public bool HasAttribute(string name);
    public void RemoveAttribute(string name);

    // Layout-affecting properties (compute lazily, invalidate on style/mutation):
    public ComputedStyle ComputedStyle { get; }   // populated by Tessera.Css

    public string InnerHtml { get; set; }
    public string OuterHtml { get; set; }
}
```

### Attribute storage

```csharp
public sealed class NamedNodeMap
{
    private InlineAttr4 _inline;
    private List<Attr>? _overflow;

    public int Count { get; }
    public Attr this[int index] { get; }
    public Attr? GetNamedItem(string name);
    public void SetNamedItem(Attr attr);
    public Attr? RemoveNamedItem(string name);
}

public readonly record struct Attr(string Name, string Value, string? Namespace = null);
```

`InlineAttr4` is a `[StructLayout(LayoutKind.Sequential)]` 4-slot struct. Avoid the `List<>` allocation for the common case.

### HTML element name → CLR type map

Generate at compile time. Implementation detail: a `Dictionary<string, Func<Element>>` keyed by lowercase local name.

| Tag | Type |
|---|---|
| `a` | `HtmlAnchorElement` |
| `body` | `HtmlBodyElement` |
| `button` | `HtmlButtonElement` |
| `canvas` | `HtmlCanvasElement` (stub in v1, OUT-OF-SCOPE-V1 for rendering) |
| `div` | `HtmlDivElement` |
| `form` | `HtmlFormElement` |
| `head` | `HtmlHeadElement` |
| `html` | `HtmlHtmlElement` |
| `iframe` | `HtmlIFrameElement` (stub) |
| `img` | `HtmlImageElement` |
| `input` | `HtmlInputElement` |
| `link` | `HtmlLinkElement` |
| `meta` | `HtmlMetaElement` |
| `script` | `HtmlScriptElement` |
| `select` | `HtmlSelectElement` |
| `span` | `HtmlSpanElement` |
| `style` | `HtmlStyleElement` |
| `template` | `HtmlTemplateElement` |
| `textarea` | `HtmlTextAreaElement` |
| `title` | `HtmlTitleElement` |
| ... | ... |

Anything not in the map is `HtmlUnknownElement`. About 30 concrete subtypes for v1.

## Events

### Public API

```csharp
public abstract class EventTarget
{
    public void AddEventListener(string type, EventListener listener, AddOptions opts = default);
    public bool RemoveEventListener(string type, EventListener listener, RemoveOptions opts = default);
    public bool DispatchEvent(Event @event);
}

public delegate void EventListener(Event @event);

public sealed record AddOptions(bool Capture = false, bool Once = false, bool Passive = false);
public sealed record RemoveOptions(bool Capture = false);
```

### Dispatch algorithm

Implement per [SPEC: DOM §2.9 Dispatching events](https://dom.spec.whatwg.org/#dispatching-events) literally:

1. Build event path from target up to root.
2. Capture phase: invoke capture listeners in root→target order.
3. At-target: invoke target listeners (both capture+bubble).
4. Bubble phase (if `event.bubbles`): invoke bubble listeners in target→root order.
5. Honor `stopPropagation` / `stopImmediatePropagation` / `preventDefault`.
6. `once` listeners auto-remove after firing.

### Event types in v1

| Category | Events |
|---|---|
| Mouse | `click`, `mousedown`, `mouseup`, `mousemove`, `mouseover`, `mouseout`, `dblclick`, `contextmenu` |
| Keyboard | `keydown`, `keyup`. **`keypress` is NOT implemented** — deprecated since 2015; modern SPAs (incl. claude.ai) use `keydown`/`keyup`. Bound handler is a no-op. |
| Input | `input`, `change`, `submit` |
| Focus | `focus`, `blur`, `focusin`, `focusout` |
| Window/Document | `load`, `DOMContentLoaded`, `unload`, `beforeunload`, `resize`, `scroll`, `error`, `popstate` |
| Touch | `touchstart`, `touchmove`, `touchend`, `touchcancel` (stub fields; OS dispatch deferred) |
| Custom | `CustomEvent` constructor |

Specific `Event` subclasses:

```csharp
public class MouseEvent : UiEvent {
    public double ClientX { get; init; }
    public double ClientY { get; init; }
    public short Button { get; init; }
    public bool CtrlKey { get; init; }
    public bool ShiftKey { get; init; }
    public bool AltKey { get; init; }
    public bool MetaKey { get; init; }
}

public class KeyboardEvent : UiEvent {
    public string Key { get; init; }
    public string Code { get; init; }
    public bool Repeat { get; init; }
    public bool CtrlKey { get; init; }
    public bool ShiftKey { get; init; }
    public bool AltKey { get; init; }
    public bool MetaKey { get; init; }
}

public class InputEvent : UiEvent { public string? Data { get; init; } }
public class CustomEvent : Event   { public object? Detail { get; init; } }
public class PopStateEvent : Event { public object? State { get; init; } }
```

## MutationObserver

Per [SPEC: DOM §4.3](https://dom.spec.whatwg.org/#mutation-observers).

```csharp
public sealed class MutationObserver
{
    public MutationObserver(Action<IReadOnlyList<MutationRecord>, MutationObserver> callback);
    public void Observe(Node target, MutationObserverInit options);
    public void Disconnect();
    public IReadOnlyList<MutationRecord> TakeRecords();
}

public sealed record MutationObserverInit(
    bool ChildList = false, bool Subtree = false,
    bool Attributes = false, IReadOnlyList<string>? AttributeFilter = null,
    bool AttributeOldValue = false,
    bool CharacterData = false, bool CharacterDataOldValue = false);

public sealed class MutationRecord
{
    public string Type { get; init; }            // "attributes" | "characterData" | "childList"
    public Node Target { get; init; }
    public IReadOnlyList<Node>? AddedNodes { get; init; }
    public IReadOnlyList<Node>? RemovedNodes { get; init; }
    public Node? PreviousSibling { get; init; }
    public Node? NextSibling { get; init; }
    public string? AttributeName { get; init; }
    public string? AttributeNamespace { get; init; }
    public string? OldValue { get; init; }
}
```

### Implementation

- Each `Node.AppendChild` / `RemoveChild` / `SetAttribute` etc. fires a "mutation" through a static `MutationObserverRegistry`.
- Registry walks up the parent chain looking for observers whose `Subtree=true` or whose `target == node`.
- Records are queued; the queue is **drained by the event loop as a microtask** (see [10_WEB_APIS.md#event-loop](10_WEB_APIS.md#event-loop)).
- All records for the same observer in one microtask coalesce into a single callback call.

This is required for SPAs — React/Vue/etc. rely on it.

## Traversal

`NodeIterator` and `TreeWalker` per spec §6. Implement straight from the spec algorithms. Both honor `NodeFilter` callbacks.

## Ranges and Selection

OUT-OF-SCOPE-V1 except for these stubs:
- `Document.CreateRange()` returns an object with `startContainer`/`startOffset`/`endContainer`/`endOffset` and `commonAncestorContainer`. No mutation methods in v1.
- `Window.GetSelection()` returns an empty selection.

Many SPAs call `getSelection()` defensively; returning a no-op is fine. They break only if they read a non-empty selection, which is rare on initial render.

## Shadow DOM

OUT-OF-SCOPE-V1. Plan target: M8.

Stubs that must exist:
- `Element.AttachShadow(ShadowRootInit)` throws `NotSupportedError`.
- `Element.ShadowRoot` returns `null`.
- `<slot>` is parsed and rendered as if it were `<span>`.

Sites using web components without polyfills (e.g. some Google services) will look glitchy. Acceptable for v1.

## Forms

Forms must work for google.com search and claude.ai login. Implement:

```csharp
public sealed class HtmlFormElement : HtmlElement
{
    public string Action { get; set; }
    public string Method { get; set; }      // "get" | "post" | "dialog"
    public string Enctype { get; set; }
    public string AcceptCharset { get; set; }
    public string Target { get; set; }
    public bool NoValidate { get; set; }
    public IReadOnlyList<HtmlElement> Elements { get; }

    public void Submit();        // submit programmatically (skips submit event)
    public void RequestSubmit(); // fires submit event, then submits (spec algorithm)
    public void Reset();
}
```

Submission algorithm per [SPEC: HTML §4.10.21](https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#form-submission-algorithm) — entry list construction, URL encoding for GET, multipart/form-data for POST when `enctype` matches.

## Innerhtml and outerhtml

```csharp
public string InnerHtml
{
    get => HtmlSerializer.SerializeInner(this);
    set { /* parse via FragmentParser; replace children */ }
}
```

`HtmlSerializer` per [SPEC: HTML §13.3](https://html.spec.whatwg.org/multipage/parsing.html#serializing-html-fragments).

`FragmentParser` is in `Tessera.Html` ([04_HTML_PARSING.md#fragment-parser](04_HTML_PARSING.md#fragment-parser)).

## Performance budget

- 100k-node DOM: AppendChild ≤ 200ns each (= 20ms for the whole tree).
- `getElementById`: average O(1) via per-document hashmap keyed on `id` attribute (invalidate on id mutations).
- `getElementsByClassName('foo')`: live, lazy. Snapshot rebuilt on first access after `MutationVersion` changes.
- `querySelectorAll`: O(n) full traversal in v1; index by `[id]` and `[tagname]` as fast paths (see [06_CSS.md#selector-matching](06_CSS.md#selector-matching)).

## Thread safety

The DOM is **single-threaded**. All mutations from the main engine thread. Other threads (net, paint) **must not touch nodes directly**. Use `IEngineDispatcher.Post(() => ...)`.

## Acceptance Tests

- [ ] All WPT `dom/nodes/**` cases pass ≥ 99%.
- [ ] Building a 100k-node DOM with `appendChild` in a loop completes in ≤ 25ms.
- [ ] `getElementById('x')` returns the same instance after a mutation that doesn't touch `id`.
- [ ] `getElementsByClassName('x')` reflects live mutations.
- [ ] Event dispatch produces correct path on a 5-deep tree with capture and bubble listeners; `stopPropagation` halts at the right step.
- [ ] MutationObserver fires on `childList`, `attributes` (with filter), and `characterData` mutations, in the next microtask, with correct `MutationRecord` shape.
- [ ] `element.innerHTML = '...'` parses with FragmentParser, replaces children, fires `MutationObserver` once.
- [ ] Forms: a GET `<form>` constructs the correct query string and triggers a navigation through `Engine.Navigate`.
