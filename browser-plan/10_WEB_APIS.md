# 10 — Web APIs and Event Loop

## Scope

**In:** DOM bindings (Web IDL → JS), the event loop (tasks + microtasks), `fetch`, `Promise` integration with host, Storage (localStorage/sessionStorage), History API, observers (Mutation/Intersection/Resize), `requestAnimationFrame`, `URL`, `URLSearchParams`, `TextEncoder`/`TextDecoder`, `console`.
**Out:** ServiceWorker, WebRTC, WebGL/WebGPU, WebSocket (sketched, M6+), `Notification`, IndexedDB (M7+), File System Access API, **`XMLHttpRequest`** (not implemented; modern target sites use `fetch` exclusively — see [XMLHttpRequest section](#xmlhttprequest) below).

## Spec refs

- [SPEC: WebIDL](https://webidl.spec.whatwg.org/)
- [SPEC: HTML §9 — Browsing Contexts and Event Loop](https://html.spec.whatwg.org/multipage/webappapis.html)
- [SPEC: Fetch](https://fetch.spec.whatwg.org/)
- [SPEC: DOM §4.3 MutationObserver](https://dom.spec.whatwg.org/#mutation-observers)
- [SPEC: IntersectionObserver](https://www.w3.org/TR/intersection-observer/)
- [SPEC: ResizeObserver](https://www.w3.org/TR/resize-observer/)
- [SPEC: Streams](https://streams.spec.whatwg.org/) (subset)
- [SPEC: Encoding](https://encoding.spec.whatwg.org/)

## Project layout

```
src/Tessera.Bindings/
├── Tessera.Bindings.csproj
├── DomBindings.cs                # static entry: install all DOM globals on a Realm
├── BindingHelpers.cs             # to/from JsValue conversions
├── Idl/
│   ├── IdlAttribute.cs           # marks bound methods on CLR classes
│   └── IdlGenerator.cs           # source generator (optional, M5+)
├── Bindings/
│   ├── WindowBinding.cs
│   ├── DocumentBinding.cs
│   ├── NodeBinding.cs / ElementBinding.cs / TextBinding.cs / ...
│   ├── EventBinding.cs / MouseEventBinding.cs / ...
│   ├── HtmlElementBindings/      # one per HtmlXxxElement
│   ├── ConsoleBinding.cs
│   ├── StorageBinding.cs
│   ├── HistoryBinding.cs / LocationBinding.cs
│   ├── NavigatorBinding.cs / ScreenBinding.cs
│   ├── UrlBinding.cs / UrlSearchParamsBinding.cs
│   ├── TextEncoderBinding.cs / TextDecoderBinding.cs
│   ├── FetchBinding.cs / RequestBinding.cs / ResponseBinding.cs / HeadersBinding.cs
│   ├── FormDataBinding.cs / BlobBinding.cs / FileBinding.cs
│   ├── MutationObserverBinding.cs
│   ├── IntersectionObserverBinding.cs
│   ├── ResizeObserverBinding.cs
│   ├── AbortControllerBinding.cs
│   └── WebSocketBinding.cs       # M6+ stub
└── WindowProxy.cs

src/Tessera.Loop/
├── Tessera.Loop.csproj
├── IEventLoop.cs
├── EventLoop.cs
├── TaskQueue.cs / MicrotaskQueue.cs
├── Timers.cs
├── RafScheduler.cs
└── Fetch/
    ├── Fetcher.cs
    └── Request.cs / Response.cs
```

## Event loop

Per [SPEC: HTML §9.5](https://html.spec.whatwg.org/multipage/webappapis.html#event-loop). One per `BrowsingContext`. Single-threaded.

### Task queues

- **Task queue** ("macrotask") — multiple, named: `dom-manipulation`, `user-interaction`, `networking`, `history-traversal`, `timer`. Per spec, can pick from any non-empty.
- **Microtask queue** — single, drained completely between tasks.
- **Animation frame callbacks** — run at the rendering opportunity.
- **Render steps** — at the rendering opportunity, run resize-observer / IO / RAF / paint.

### Algorithm (simplified)

```
loop:
  oldestTask = pick next runnable task across queues
  if oldestTask: run it (synchronously)
  drain microtasks (run until queue empty, recursing through new microtasks added)
  if it's time to render:
      run resize-observer steps
      run intersection-observer steps
      run RAF callbacks
      style + layout + paint (if dirty)
  loop
```

The "is it time to render" heuristic in v1: target 60 Hz, render at most every 16.6ms. Skip renders if last frame was < target interval.

### Public API

```csharp
public interface IEventLoop
{
    void PostTask(TaskSource source, Action work);
    void PostMicrotask(Action work);
    void Run();           // blocking; returns when shut down
    void Stop();
}

public enum TaskSource { DomManipulation, UserInteraction, Networking, HistoryTraversal, Timer, Idle }
```

### Microtask integration

`Promise` resolution enqueues microtasks. Bindings call `eventLoop.PostMicrotask` on `then` callback dispatch.

### Threading

The event loop runs on its dedicated thread. Network bytes arriving on `ThreadPool` workers `PostTask(Networking, ...)` to it. UI input from Avalonia uses `Dispatcher.Post` from UI thread to engine thread.

## DOM bindings — the bridge

The bridge sits between `Tessera.Dom` (plain .NET objects) and `Tessera.Js` (JsObjects). Implemented manually as `BindingObject` subclasses of `JsObject`:

```csharp
public sealed class DocumentBinding : JsObject
{
    private readonly Document _doc;

    public DocumentBinding(Realm realm, Document doc) : base(realm.Intrinsics.DocumentPrototype)
    { _doc = doc; }

    public override JsValue Get(string key, JsValue receiver) => key switch
    {
        "documentElement" => DomBindings.Wrap(_doc.DocumentElement, _realm),
        "body"            => DomBindings.Wrap(_doc.Body, _realm),
        "head"            => DomBindings.Wrap(_doc.Head, _realm),
        "title"           => _doc.Body?.OwnerDocument.Title ?? JsValue.String(""),
        "URL"             => JsValue.String(_doc.Url.Serialize()),
        "readyState"      => JsValue.String(_doc.ReadyState.ToString().ToLowerInvariant()),
        // ...
        _ => base.Get(key, receiver),
    };

    // Methods exposed as properties:
    // Stored on the prototype; identity caches.
}
```

`DomBindings.Wrap(node)` keeps a WeakReference map `Node ↔ JsObject` so the same node returns the same JS wrapper across calls — required by spec for object identity.

### IDL generation (M5+, optional)

Hand-writing all bindings is tedious but tractable in v1. M5+ source-generator approach:

```csharp
[Idl("Element")]
public partial class ElementBinding : JsObject
{
    [IdlAttribute] public string Id => _element.Id;
    [IdlAttribute] public string TagName => _element.TagName;
    [IdlAttribute] public ElementBinding? FirstElementChild => Wrap(_element.FirstElementChild);
    [IdlMethod] public bool Matches(string selectors) => _element.Matches(selectors);
}
```

Generator emits the `Get`/`Set` overrides + descriptor table at compile time.

## Window

The JS global is a `WindowBinding` (extends `JsObject`). It holds:

| Property | Resolves to |
|---|---|
| `window`, `self`, `globalThis`, `frames`, `parent`, `top` | the same WindowBinding (no iframes in v1) |
| `document` | the page Document |
| `location` | `LocationBinding` |
| `history` | `HistoryBinding` |
| `navigator` | `NavigatorBinding` |
| `screen` | `ScreenBinding` |
| `console` | `ConsoleBinding` |
| `localStorage`, `sessionStorage` | `StorageBinding` |
| `crypto` | `CryptoBinding` (only `getRandomValues` and `randomUUID` in v1) |
| `fetch`, `WebSocket` | constructors / functions. `XMLHttpRequest` is exposed as a stub constructor that throws `NotSupportedError` on `open()` — see [XMLHttpRequest section](#xmlhttprequest). |
| `setTimeout`, `clearTimeout`, `setInterval`, `clearInterval` | bound to `Timers` |
| `requestAnimationFrame`, `cancelAnimationFrame` | bound to `RafScheduler` |
| `queueMicrotask` | direct event loop call |
| `alert`, `confirm`, `prompt` | UI shell hooks (M4+; stubs return defaults in M3) |

### `Window` properties also exposed as globals

JS lets you write `document.body` and `alert()` directly. Implementation: the `Window` is also the global object's lexical scope; property gets fall through to it.

### Lifecycle events

- `DOMContentLoaded` — fired after parsing complete, before stylesheets needed for execution. Per spec.
- `load` — after all stylesheets + images + scripts loaded.
- `beforeunload` — on navigation away.
- `popstate` — on back/forward.

Each is fired via `EventTarget.dispatchEvent` on the Window.

## fetch

```csharp
public sealed class Fetcher
{
    public Fetcher(IHttpClient http, CookieJar jar);
    public Task<FetchResponse> FetchAsync(FetchRequest req, CancellationToken ct);
}

public sealed record FetchRequest(
    Url Url, string Method, IReadOnlyList<(string, string)> Headers,
    ReadOnlyMemory<byte>? Body, CredentialsMode Credentials,
    CacheMode Cache, RedirectMode Redirect, ReferrerPolicy Referrer);

public sealed record FetchResponse(
    int Status, string StatusText,
    IReadOnlyList<(string, string)> Headers,
    IAsyncEnumerable<ReadOnlyMemory<byte>> Body,
    Url Url, ResponseType Type);
```

Spec [SPEC: Fetch §4](https://fetch.spec.whatwg.org/#fetching) is huge. v1 implements:
- GET, POST with `Body` of `string | ArrayBuffer | Uint8Array | FormData`.
- Streaming response body (`Response.body` is a `ReadableStream` whose underlying source pulls from `IAsyncEnumerable`).
- Redirects per `RedirectMode`.
- CORS preflight + checks per spec (`Origin` header, `Access-Control-*` response headers).
- Credentials per mode.
- `Cache-Control: no-store` honored.

`fetch()` in JS:
```js
const r = await fetch('/api/x', { method:'POST', body: JSON.stringify(...) });
const json = await r.json();
```

Bind `Response.prototype.{text, json, arrayBuffer, blob, formData, clone}` accordingly.

`AbortSignal` integrates with `Tessera.Net` via `CancellationToken` chains.

## XMLHttpRequest

**NOT IMPLEMENTED.** Every modern target site (google.com, claude.ai, and every SPA framework worth caring about in 2026) uses `fetch` exclusively. XHR's two-decade legacy surface — `readystatechange`, sync mode, partial-response progress, document responseType — is a notable maintenance burden for ~zero target traffic.

The constructor is exposed but throws `NotSupportedError` on `open()`:

```csharp
public sealed class XhrBinding : JsObject
{
    public override JsValue Get(string key, JsValue receiver) => key switch
    {
        "open" => JsValue.NativeFn(_ => throw new JsException("XMLHttpRequest is not supported. Use fetch().", "NotSupportedError")),
        _ => base.Get(key, receiver),
    };
}
```

Sites that feature-detect XHR and fall back to fetch will work. Sites that hard-depend on XHR will break loudly with a clear console message. Revisit only if a target site forces it.

## Storage

`localStorage` and `sessionStorage` are per-origin key/value stores.

```csharp
public sealed class StorageBinding : JsObject
{
    private readonly Dictionary<string, string> _data;
    public override JsValue Get(string key, JsValue receiver)
    {
        if (key == "length") return JsValue.Int32(_data.Count);
        if (_data.TryGetValue(key, out var v)) return JsValue.String(v);
        return base.Get(key, receiver);
    }
    public override void Set(string key, JsValue value, JsValue receiver)
    {
        _data[key] = JsConvert.ToString(value);
        StorageEventBus.Emit(_origin, key);
    }
}
```

Persistence:
- `sessionStorage`: per-tab in-memory only.
- `localStorage`: per-origin on-disk persistent. v1 location: `%LOCALAPPDATA%/Tessera/Storage/<origin-hash>.json` (or platform equivalent via `Environment.GetFolderPath`). Pure-managed JSON file.

`storage` event fires across windows of the same origin (cross-tab in M5+).

## History API

```csharp
public sealed class HistoryBinding : JsObject
{
    public void PushState(JsValue state, string title, string url);
    public void ReplaceState(JsValue state, string title, string url);
    public void Back(); public void Forward(); public void Go(int delta);
    public int Length { get; }
    public JsValue State { get; }
}
```

`pushState`/`replaceState` mutate the URL bar **without navigating**. SPAs depend on this for client-side routing.

Implementation: history is a list of entries in the `BrowsingContext`. The shell URL bar listens for `Engine.UrlChanged`.

`location.href = ...` triggers a real navigation through `Engine.NavigateAsync`.

## Observers

### MutationObserver

Already defined in [05_DOM.md](05_DOM.md#mutationobserver). Bindings layer wraps it as a JS-visible constructor:

```js
const o = new MutationObserver((records, obs) => { ... });
o.observe(target, { childList: true, subtree: true });
```

### IntersectionObserver

[SPEC: IntersectionObserver](https://www.w3.org/TR/intersection-observer/).
- Maintained at the page level. Reads layout boxes for tracked elements at end-of-frame.
- Computes intersection with each `root` (or viewport).
- Fires callback with records when ratios cross thresholds.

Integration: hooks into the event-loop "render steps". Reads `LayoutResult` for the latest frame.

### ResizeObserver

[SPEC: ResizeObserver](https://www.w3.org/TR/resize-observer/). Hooks at render-step time. Fires when a target's `contentBoxSize`, `borderBoxSize`, or `devicePixelContentBoxSize` change.

## requestAnimationFrame

```csharp
public sealed class RafScheduler
{
    public int Schedule(Action<double> callback);   // returns handle
    public void Cancel(int handle);
    internal void RunFrame(double timestampMs);
}
```

Run from the event loop's rendering opportunity, before paint.

## queueMicrotask, setTimeout, setInterval

```csharp
public sealed class Timers
{
    public int SetTimeout(JsFunction cb, int ms, JsValue[] args);
    public int SetInterval(JsFunction cb, int ms, JsValue[] args);
    public void ClearTimeout(int id);
    public void ClearInterval(int id);
}
```

Backed by a min-heap of `(deadline, id, callback)`. Event loop checks the heap at every tick.

Minimum delay: 4ms after the 5th nested timer, per HTML spec.

## URL and URLSearchParams

Wrap `Tessera.Url.Url`. Surface per [WHATWG URL](https://url.spec.whatwg.org/#url-class).

```js
const u = new URL('/x?q=1', 'https://example.com');
u.searchParams.append('y', '2');
console.log(u.href);  // https://example.com/x?q=1&y=2
```

## TextEncoder / TextDecoder

`TextEncoder` always UTF-8. `TextDecoder` supports `utf-8`, `utf-16le`, `utf-16be`, `windows-1252`, `iso-8859-*`, the WHATWG-required set. Use `System.Text.Encoding` + WHATWG-spec'd mappings (some labels normalize to non-IANA names — see [SPEC: Encoding §4.2](https://encoding.spec.whatwg.org/#names-and-labels)).

## Crypto

```js
crypto.getRandomValues(new Uint8Array(16));
crypto.randomUUID();
```

Use `System.Security.Cryptography.RandomNumberGenerator`. Pure managed in .NET 10? Yes — `RandomNumberGenerator.Fill` is backed by the OS RNG but the surface area is managed (technically P/Invoke under the hood inside the BCL, which is **allowed** — Rule 0 forbids P/Invoke in our code, not within the framework). Document this in the [01_ARCHITECTURE.md](01_ARCHITECTURE.md) rule-0 footnote.

`crypto.subtle.*` — OUT-OF-SCOPE-V1. Many sites won't need it. Login flows that do (passkeys) require subtle. Plan for M8 implementation via Bouncy Castle's crypto primitives.

## WebSocket (M6+)

Sketched here for binding shape. Real impl in `Tessera.Net/Ws/` (RFC 6455 framing).

```js
const ws = new WebSocket('wss://example.com');
ws.onmessage = (e) => ...;
ws.send('hi');
```

## Script loading

Per [SPEC: HTML §4.12.1](https://html.spec.whatwg.org/multipage/scripting.html). Critical because the HTML parser pauses on `<script>`.

Inline `<script>`: synchronous, blocks parser, fetched-data immediately available.

External: parser fires `ScriptBlocked` event. Loader fetches via `Fetcher`, then `Realm.Evaluate`, then resumes parser.

`async`: queue fetch; evaluate as soon as available, don't block parser.
`defer`: queue fetch; evaluate after parsing is done, before `DOMContentLoaded`, in source order.
`type=module`: ES module, always deferred, fetches submodules recursively.

## Console

```csharp
public sealed class ConsoleBinding : JsObject
{
    public void Log(JsValue[] args)
        => _logger.Log(LogLevel.Info, Format(args));
    // ... Info, Warn, Error, Debug, Dir, Table, Time, TimeEnd, Count, Group, GroupEnd
}
```

Format args per [SPEC: console](https://console.spec.whatwg.org/) (printf-ish: `%s`, `%d`, `%i`, `%o`, `%O`, `%c`).

In v1, route to host `ILogger`. In M7+, surface a devtools console panel.

## Acceptance Tests

- [ ] `setTimeout(fn, 0)` runs `fn` in the next task tick; microtasks queued before it run first.
- [ ] `Promise.resolve().then(...)` runs the continuation in the current microtask drain, before any task.
- [ ] `fetch('https://example.com').then(r => r.text())` returns the response body.
- [ ] `new XMLHttpRequest().open('GET', '/x')` throws `NotSupportedError` (constructor exists, `open` throws).
- [ ] `localStorage.setItem('x', '1'); localStorage.getItem('x')` returns `"1"`. Persisted across process restarts.
- [ ] `history.pushState({}, '', '/new')` changes URL without navigation; `popstate` fires on back.
- [ ] `MutationObserver` callback fires in next microtask drain after a DOM mutation.
- [ ] `IntersectionObserver` callback fires when a target enters the viewport scroll-induced.
- [ ] `requestAnimationFrame(t => ...)` fires once per frame, `t` is increasing.
- [ ] `new TextEncoder().encode('hello')` returns the expected bytes.
- [ ] `console.log(document)` produces an inspectable representation of the DOM root.
