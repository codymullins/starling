# 01 — Architecture

> **Scope (In):** Module boundaries, dependency direction, public surface between modules, threading model, in-process IPC abstraction, error handling conventions, allocation strategy.
> **Scope (Out):** Implementation of individual modules — see numbered docs.
> **Dependencies:** `02_PROJECT_SETUP.md` (concrete csproj/sln), all module docs (implementations).
> **Spec refs:** WHATWG HTML (`https://html.spec.whatwg.org/multipage/`), WHATWG DOM (`https://dom.spec.whatwg.org/`).

---

## A. Module graph

```
                       ┌──────────────────┐
                       │  Tessera.Shell   │   Avalonia UI host
                       └────────┬─────────┘
                                ▼
                       ┌──────────────────┐
                       │  Tessera.Engine  │   Browser / Page / Frame composition
                       └────────┬─────────┘
        ┌───────────────────────┼──────────────────────┐
        ▼                       ▼                      ▼
┌──────────────┐        ┌──────────────┐       ┌─────────────────┐
│ Tessera.Loop │        │Tessera.Layout│       │ Tessera.Bindings │
│ event loop   │        │ box tree     │       │ Web IDL ↔ JS     │
└────┬─────────┘        └────┬─────────┘       └────┬─────────────┘
     │                       ▼                      ▼
     │                ┌──────────────┐       ┌─────────────────┐
     │                │Tessera.Paint │       │   Tessera.Js    │
     │                │display lists │       │  parser + VM    │
     │                └────┬─────────┘       └─────────────────┘
     │                     │
     ▼                     ▼
┌──────────────┐    ┌──────────────┐
│Tessera.Css   │◄───│ Tessera.Dom  │
│cascade       │    │ DOM tree     │
└──────────────┘    └────┬─────────┘
                         ▼
                   ┌──────────────┐
                   │Tessera.Html  │
                   │ tokenizer    │
                   └────┬─────────┘
                        ▼
                   ┌──────────────┐
                   │ Tessera.Url  │
                   └────┬─────────┘
                        ▼
                   ┌──────────────┐
                   │ Tessera.Net  │
                   │DNS/TLS/HTTP  │
                   └──────────────┘
```

**Dependency rules:**

1. Arrows point **down**. A module may only `using` modules strictly below it.
2. `Tessera.Engine` is the only module that may compose all others.
3. `Tessera.Shell` is a leaf — it may only depend on `Tessera.Engine`. No direct calls into DOM/CSS/etc.
4. `Tessera.Js` does **not** know about the DOM. `Tessera.Bindings` is the bridge.
5. Cyclical references are prohibited. Enforced via `Roslyn analyzer` rule defined in `02_PROJECT_SETUP.md`.

---

## B. Project list <a id="project-list"></a>

| Project | Purpose | Key public types |
|---|---|---|
| `Tessera.Net` (#tessera-net) | DNS, TCP, TLS 1.3, HTTP/1.1, HTTP/2, cookies, content-encoding | `HttpFetcher`, `Connection`, `CookieJar`, `Resolver` |
| `Tessera.Url` (#tessera-url) | WHATWG URL parser | `Url`, `UrlParser` |
| `Tessera.Html` (#tessera-html) | Tokenizer + tree builder | `Tokenizer`, `TreeBuilder` |
| `Tessera.Dom` (#tessera-dom) | DOM tree, events, mutation observers | `Document`, `Element`, `Node`, `Event` |
| `Tessera.Css` (#tessera-css) | Syntax, selectors, cascade, computed values | `Stylesheet`, `Selector`, `Cascade`, `ComputedStyle` |
| `Tessera.Layout` (#tessera-layout) | Formatting contexts, box tree | `LayoutTree`, `Box`, `LayoutEngine` |
| `Tessera.Paint` (#tessera-paint) | Display list IR, ImageSharp paint backend | `DisplayList`, `Painter`, `PaintContext` |
| `Tessera.Js` (#tessera-js) | Lexer, parser, bytecode, VM | `JsEngine`, `Realm`, `JsValue` |
| `Tessera.Bindings` (#tessera-bindings) | Generated Web IDL bindings | `*Interface` partial classes per IDL fragment |
| `Tessera.Loop` (#tessera-loop) | Event loop + microtask queue + scheduler | `EventLoop`, `Task`, `Microtask` |
| `Tessera.Engine` (#tessera-engine) | Browser, BrowsingContext, Page, Frame | `Browser`, `Page`, `Frame`, `Navigation` |
| `Tessera.Shell` (#tessera-shell) | Avalonia UI | `MainWindow`, `TabView`, `AddressBar` |

Test projects mirror each: `Tessera.Net.Tests`, etc. Plus `Tessera.IntegrationTests` and `Tessera.GoldenTests`.

---

## C. Public API conventions

### C.1 Naming

- Public types: `PascalCase`. No `I` prefix on interfaces unless they exist alongside a same-named class (then `IFoo` / `Foo`).
- Internal types: `internal class` by default. `public` only when needed across project boundaries.
- File-scoped namespaces. One top-level type per file.
- `Result<T, E>` style returns where errors are recoverable. `throw` only for programmer errors (null where non-null required, invariant violation).

### C.2 Async/sync boundary

- **Networking is async-only**: every public method on `Tessera.Net` returns `ValueTask<>` or `Task<>`.
- **Parsing is sync** (HTML, CSS, JS): pure CPU, kept synchronous to simplify reasoning. Run on background `Task.Run` if needed.
- **DOM mutations are sync** and always run on the event-loop thread.
- **Layout and paint are sync** and run on the event-loop thread.

### C.3 Cancellation

Every async method accepts a `CancellationToken` as its last parameter. Internal `CancellationTokenSource`s are linked to a per-Page token that flips when the user navigates away or closes the tab.

### C.4 Memory

- Use `ReadOnlySpan<byte>` for parser input. `ReadOnlySpan<char>` for textual input post-decode.
- Object pools (`ObjectPool<T>` from `Microsoft.Extensions.ObjectPool`) for token, node, and box allocations on the hot path.
- `ArrayPool<byte>.Shared` for transient buffers (HTTP body chunks, decoded image scanlines).
- Avoid `IEnumerable<T>` on hot paths — prefer `Span<T>` or explicit `Span`-returning methods.
- The DOM tree itself is **not** pooled — nodes have identity that escapes to JS.

### C.5 Allocations: zero-cost goals

| Hot path | Allocation budget per call |
|---|---|
| Tokenizer per character | 0 |
| Tree builder per token | 0–1 |
| CSS cascade per element | 0 (reuse `ComputedStyle` slots) |
| Layout pass per frame | bounded by box count |
| JS bytecode dispatch per instruction | 0 (interpreter loop reuses register file) |

---

## D. Threading model <a id="threading-model"></a>

```
┌──────────────────────────────────────────────────────────┐
│ UI Thread (Avalonia)                                     │
│   - Renders bitmaps produced by paint thread             │
│   - Dispatches input → engine via posted action queue    │
└────────────────────┬─────────────────────────────────────┘
                     │ (Dispatcher.UIThread.Post)
                     ▼
┌──────────────────────────────────────────────────────────┐
│ Event Loop Thread (per Page)                             │
│   - Runs JS                                              │
│   - Mutates DOM                                          │
│   - Schedules style/layout/paint                         │
│   - Owns microtask queue                                 │
└──┬────────────┬──────────────┬──────────────┬────────────┘
   │            │              │              │
   ▼            ▼              ▼              ▼
Network     Parser pool    Image decode    Worker JS realms
worker      (HTML/CSS)     (deferred)      (deferred)
threads
(ThreadPool tasks)
```

Rules:

1. **One event-loop thread per Page.** Multiple Pages = multiple threads. The shell aggregates them.
2. **DOM access is single-threaded.** No locks. Other threads communicate via posted callbacks onto the event loop.
3. **Networking lives on the ThreadPool.** `Tessera.Net` exposes `ValueTask<HttpResponse>`; results are awaited from the event loop. Body streams deliver chunks back via a callback that's marshalled to the loop.
4. **No shared mutable state across threads** except through immutable snapshots (e.g. parsed `Stylesheet` after parsing completes) or thread-safe channels (`System.Threading.Channels`).
5. **No `lock()` in hot paths.** If you reach for `lock`, redesign with a channel or per-thread state.

---

## E. Page / Frame / BrowsingContext model

```csharp
namespace Tessera.Engine;

public sealed class Browser
{
    public IReadOnlyList<Page> Pages { get; }
    public Page CreatePage();
    public void ClosePage(Page page);
}

public sealed class Page
{
    public Frame Top { get; }                 // root frame
    public Navigation Navigation { get; }     // session history
    public EventLoop Loop { get; }            // owns the thread
    public Surface Surface { get; }           // paint output → shell

    public Task NavigateAsync(Url url, CancellationToken ct = default);
    public void Reload();
    public void Stop();
}

public sealed class Frame
{
    public Document Document { get; }         // Tessera.Dom
    public Realm Realm { get; }               // Tessera.Js
    public IReadOnlyList<Frame> Children { get; }
}

public sealed class Surface
{
    // Latest painted frame, addressable by Avalonia.
    public Image<Bgra32> Bitmap { get; }      // SixLabors.ImageSharp
    public event Action<Surface> Invalidated;
}
```

A `Page` corresponds to a tab. A `Frame` corresponds to an HTML `<iframe>` or the top-level browsing context. Each `Frame` has exactly one `Document` and exactly one JS `Realm`.

---

## F. Data flow: a request from address-bar to pixels

1. User types URL → `Tessera.Shell.AddressBar` posts `Page.NavigateAsync(url)` onto the page's event loop.
2. Event loop calls `Tessera.Net.HttpFetcher.GetAsync(url, ct)` (off-thread).
3. Network worker resolves DNS → opens TCP → completes TLS → sends HTTP request → reads response headers → returns response object + body stream.
4. Headers come back to event loop. Body bytes stream into `Tessera.Html.Tokenizer` (which buffers incrementally and yields tokens to `TreeBuilder`).
5. `TreeBuilder` mutates `Tessera.Dom.Document`.
6. On `</head>` and again on `DOMContentLoaded`-equivalent points, `Tessera.Css.Cascade` runs and updates `ComputedStyle` for newly-styled elements.
7. After DOM stable (a tick on the loop), `Tessera.Layout.LayoutEngine` builds/updates the box tree.
8. `Tessera.Paint.Painter` walks the box tree, emits a `DisplayList`, executes it against an `Image<Bgra32>` via `ImageSharp.Drawing`.
9. `Surface.Invalidated` fires → Avalonia's render loop blits the bitmap.
10. Meanwhile, `<script>` elements encountered by the parser are routed to `Tessera.Js` for execution, which can mutate the DOM, causing style/layout/paint invalidation.

See `13_MILESTONES.md` for which of these stages are implemented when.

---

## G. Error / panic model

| Source | Strategy |
|---|---|
| Malformed bytes in HTML/CSS | Spec-defined recovery, never throw. Emit parse-error event for devtools. |
| Network errors | Surface to `Page` as `NavigationFailure`. Display error page. |
| TLS failures | Same. Cert-error subtype. |
| JS uncaught exception | Logged to console, dispatched as `error` event. Does not crash. |
| Assertion violation in engine (invariant broken) | `Debug.Assert` in debug; `Tessera.Engine.PanicException` in release. Logged via `IDiagnostics`. |
| OOM, stack overflow | Let .NET kill the process; restart the page. |

---

## H. Diagnostics

A single `IDiagnostics` interface, registered in `Tessera.Engine`:

```csharp
public interface IDiagnostics
{
    void Log(DiagLevel level, string area, string message);
    IDisposable Span(string area, string operation);          // tracing
    void Counter(string name, double value);                  // metrics
    void Snapshot(string label, ReadOnlySpan<byte> bytes);    // memory dump
}
```

Implemented by `ConsoleDiagnostics` (dev) and `NoopDiagnostics` (perf builds). DevTools (deferred) sits on top.

---

## I. Where things explicitly are NOT

- **No `dynamic`.** Anywhere. Use generics, `object`, or `JsValue`-style discriminated unions.
- **No reflection on hot paths.** OK during startup (e.g. binding registration).
- **No `async void`.** Use `async Task` or `async ValueTask`. Fire-and-forget uses `_ = SomethingAsync().ContinueWith(...)` with logged errors.
- **No mutable static state.** All globals go through DI containers held by `Browser`.
- **No `Thread.Sleep`.** Use `Task.Delay` or proper synchronization.
- **No `unsafe` until proven necessary.** Pure managed throughout. (Some Span/Memory APIs require it under the hood — those count as managed.)

---

## J. Build flavors

| Flavor | Defines | Use |
|---|---|---|
| Debug | `DEBUG;TESSERA_TRACE` | Local dev. Asserts on, verbose logging. |
| Release | `RELEASE` | Distribution. Asserts off. |
| Profiling | `RELEASE;TESSERA_PROFILE` | BenchmarkDotNet runs. |

AOT compatibility is a **stretch goal** — code should be reflection-light to keep this open, but we do not enable `<PublishAot>true</PublishAot>` in v1.

---

## Acceptance Tests <a id="acceptance"></a>

An implementation agent has correctly read this doc when they can answer:

1. Where does `Tessera.Layout` live in the dependency graph and what may it `using`? **(Below Engine; may using Css, Dom, Url. Not Paint, not Js, not Net.)**
2. Which thread runs JavaScript? **(The per-Page event-loop thread. Same thread as DOM mutation.)**
3. Where do TLS bytes come from? **(`Tessera.Net.Connection`, on a ThreadPool task, never on the event-loop thread.)**
4. If `Tessera.Bindings` needs to call into the DOM, which thread does it run on? **(Event loop. Always.)**
5. What is the surface between Engine and Shell? **(`Page` and `Surface`. Shell never touches DOM/CSS/Layout/Paint directly.)**
6. May the JS engine import a type from `Tessera.Dom`? **(No. Only `Tessera.Bindings` straddles that boundary.)**
