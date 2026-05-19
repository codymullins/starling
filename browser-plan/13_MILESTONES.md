# 13 — Milestones

## Posture

Be honest: building a managed-first .NET browser that runs google.com **search** and claude.ai **sign-in** is a multi-engineer-year undertaking. (Managed-first: the engine is pure-managed; native interop is confined to two vetted seams — `Starling.Skia` for graphics and `Starling.Codecs` for image decode.) The plan stages work so the project is **demoable end-to-end** at every step, not "lots of pieces with nothing assembled."

Each milestone has:
- **Entry**: what must exist before starting.
- **Goal**: the demo at exit.
- **Work**: which subsystem docs and which sections.
- **Exit**: concrete pass criteria.
- **Est. duration**: serial agent-weeks (1 agent on milestone full-time). Cuts ~50% with the parallel work packages in [14_AGENT_TASKS.md](14_AGENT_TASKS.md).

## M0 — Walking skeleton

**Entry**: nothing.

**Goal**: `starling render file://hello.html -o hello.png` writes a PNG showing the words "Hello, world." in a sans-serif font on a white background.

**Work**:
- [02_PROJECT_SETUP.md](02_PROJECT_SETUP.md) entirely.
- [01_ARCHITECTURE.md](01_ARCHITECTURE.md) project skeletons.
- [08_FONTS_PAINT.md](08_FONTS_PAINT.md): bare ImageSharp backend, single bundled font, hardcoded "render this string at (0,0)".
- [04_HTML_PARSING.md](04_HTML_PARSING.md): tokenizer only, just enough to see `<body>` text.
- [05_DOM.md](05_DOM.md): minimal `Document`, `Element`, `Text`.
- No CSS yet.

**Exit**:
- All 13 projects compile.
- CI green on win/mac/linux.
- `starling render file://testdata/hello.html -o out.png` succeeds and the image hash matches the golden.

**Est. duration**: 1 week.

## M1 — HTML+CSS to PNG (static)

**Entry**: M0.

**Goal**: A hand-written HTML+CSS file with paragraphs, headings, divs, basic block + inline layout, colors, fonts, backgrounds, padding/margin renders correctly.

**Work**:
- [04_HTML_PARSING.md](04_HTML_PARSING.md): full tokenizer; tree builder for the common subset (no `<table>`, no `<svg>`).
- [05_DOM.md](05_DOM.md): full Node/Element/Text/Comment hierarchy.
- [06_CSS.md](06_CSS.md): full syntax parser; selectors (no `:has`, no `:nth-*` yet); cascade for the layout-affecting + visual properties; UA stylesheet.
- [07_LAYOUT.md](07_LAYOUT.md): block + inline formatting; basic text layout via SixLabors.Fonts; margin collapse.
- [08_FONTS_PAINT.md](08_FONTS_PAINT.md): display list; full ImageSharp backend for solid colors, text, basic borders, rounded rectangles.

**Exit**:
- 20 golden-image cases pass: hello world, paragraphs with margins, headings, lists, nested divs with backgrounds, simple multi-line wrapping, justify/center text-align.
- html5lib tokenizer suite: 100%.
- html5lib tree-construction suite: ≥ 95%.
- WPT `css/css-syntax/**` ≥ 80%.

**Est. duration**: 4 weeks.

## M2 — Networking and live HTML

**Entry**: M1.

**Goal**: `starling render https://example.com -o out.png` works end-to-end (DNS → TCP → TLS → HTTP/1.1 → parse → layout → paint).

**Work**:
- [03_NETWORKING.md](03_NETWORKING.md): URL, DNS, TCP, TLS 1.3 via `SslStream`, HTTP/1.1 (client only), cookies, basic cache, brotli/gzip decoding.
- Image decoding (`<img>` rendering) via OS-native codecs (`Starling.Codecs`).
- Encoding sniffing across HTTP `Content-Type` charset, BOM, meta.
- The headless renderer wires the network → parser pipeline.

**Exit**:
- `starling render https://example.com` succeeds; rendered PNG matches expected within SSIM 0.99.
- HTTPS to `https://anthropic.com` (the marketing site, JS-light) reaches first paint within 2s on a wired connection.
- WPT `url/` 100%, `encoding/` ≥ 95%.
- Connection pool reuses across 2 sequential requests.

**Est. duration**: 4 weeks.

## M3 — JavaScript v1 (no DOM bindings)

**Entry**: M2.

**Goal**: `starling js script.js` evaluates ES2024-level code. No DOM access yet. Console output works.

**Work**:
- [09_JS_ENGINE.md](09_JS_ENGINE.md) almost entirely: lexer, parser, bytecode compiler, register VM, intrinsics (Object/Array/String/Number/Math/JSON/Date/RegExp), Promise + microtasks (microtask queue, but no fetch yet).
- Modules: ES module loader hooked up against the file URL scheme.
- **Native-interop pivot** (runs alongside the JS work — see `tasks/M3/wp-M3-06*`):
  adopt the interop seam policy ("managed-first, native at vetted seams").
  Introduce `Starling.Skia` (Skia Graphite + ANGLE graphics) and `Starling.Codecs`
  (OS-native image decode) as the two designated `LibraryImport` projects; swap
  BouncyCastle TLS for `SslStream`; repurpose the CI lint job from a blanket
  P/Invoke ban to the engine-project allowlist.

**Exit**:
- Test262 pass rate ≥ 80% (excluding stage-3+ proposals).
- Hand-picked microbenchmarks within 10x of V8 on simple loops.
- `starling js testdata/js/sunspider/*.js` completes in ≤ 5x reference wall-clock.

**Est. duration**: 8–10 weeks. **The single largest milestone.**

## M4 — DOM bindings + minimal Web APIs

**Entry**: M3.

**Goal**: Hand-written interactive HTML pages (counter button, fetch-and-render-list demo) work in `starling render --wait-for=networkidle`.

**Work**:
- [10_WEB_APIS.md](10_WEB_APIS.md): DOM bindings (Window, Document, Element, Event, Mouse/KeyboardEvent), addEventListener/dispatchEvent, setTimeout/setInterval, requestAnimationFrame, MutationObserver, fetch.
- [05_DOM.md](05_DOM.md) `innerHTML`, `querySelectorAll`, getBoundingClientRect.
- Event dispatch tied to layout hit-testing (no UI yet — drive via headless API).
- `localStorage` (in-memory only at this milestone).

**Exit**:
- The `counter.html` fixture: clicking a button via `Page.PostInput` increments a count displayed in the DOM.
- A `fetch-and-list.html` fixture loads `/api/items.json` via fetch and renders the list.
- WPT `dom/nodes/**` ≥ 90%, `fetch/**` ≥ 70%.

**Est. duration**: 6 weeks.

## M5 — Avalonia shell + interactivity polish

**Entry**: M4.

**Goal**: First **shippable** desktop browser. `Starling.Shell` launches on win/mac/linux, has tabs, URL bar, back/forward, can browse the modern static web.

**Work**:
- [11_AVALONIA_SHELL.md](11_AVALONIA_SHELL.md) entirely.
- Selection + clipboard text-only.
- Damage-rectangle paint optimization.
- `localStorage` on disk.
- CSS: `:has`, `:nth-*`, transforms, basic transitions, simple animations.

**Exit**:
- 30 manually verified static sites render acceptably (Wikipedia article, MDN page, hand-picked blogs).
- Tabs / back / forward / reload all work.
- One nightly E2E run passes.

**Est. duration**: 5 weeks.

## M6 — HTTP/2 + cookies hardening + WebSocket

**Entry**: M5.

**Goal**: Sites that require HTTP/2 and live WebSocket connections start working. Cookie + SameSite behavior matches Firefox's.

**Work**:
- [03_NETWORKING.md](03_NETWORKING.md) HTTP/2 + HPACK + flow control.
- WebSocket framing in `Starling.Net/Ws/` + `WebSocket` binding.
- Service-worker stubs (so sites that register them don't crash, but no actual SW execution).
- Cookies: full RFC 6265bis; partitioned cookies (CHIPS).
- HSTS preload list.

**Exit**:
- A WebSocket echo test against `wss://echo.websocket.events` works.
- Google search loads as HTTP/2 (alpn=`h2`) and renders top of results page.
- Cookies pass WPT `cookies/`.

**Est. duration**: 4 weeks.

## M7 — Big SPA support: google.com

**Entry**: M6.

**Goal**: `starling render https://www.google.com/search?q=hello` produces a rendered search-results page that is visually recognizable. Clicking a result navigates.

This is where the long tail of "spec coverage gaps that real sites trip" gets paid.

**Work**:
- [09_JS_ENGINE.md](09_JS_ENGINE.md) hardening: Proxy edge cases, RegExp unicode-flag (`v`), async/await/generators, typed arrays.
- [10_WEB_APIS.md](10_WEB_APIS.md) IntersectionObserver, ResizeObserver, History API, `Crypto.subtle` minimum (HMAC-SHA256, ECDH P-256, AES-GCM via `System.Security.Cryptography`).
- [06_CSS.md](06_CSS.md) full flexbox, grid, color functions, `clamp`, `min`, `max`, `calc` exhaustive.
- Form submission + autocomplete history.
- Subresource integrity.
- Console panel surfaces site errors.

**Exit**:
- Google home page renders with logo, search box, footer.
- Typing in the search box and pressing Enter navigates to the results page.
- The results page renders 10 results with titles, snippets, links.
- At least 80% of WPT runs that we'd previously had at < 80%.

**Est. duration**: 8 weeks.

## M8 — Big SPA support: claude.ai

**Entry**: M7.

**Goal**: `starling render https://claude.ai/` reaches a working sign-in page. Form submission proceeds (we don't need to actually authenticate; just reach the next page).

claude.ai's hardness comes from: heavy React, intersection observers, fetch streaming, web workers (maybe), modern CSS (clamp, color functions, custom properties), aggressive bundling with dynamic imports, possibly WebAssembly for crypto.

**Work**:
- Web Workers (full implementation).
- WebAssembly engine v0: parse + validate + interpret. Pure managed. Use Wabt-style validation. Hard, but bounded — WebAssembly is small relative to JS.
- `crypto.subtle` complete enough for WebAuthn primitives via `System.Security.Cryptography`.
- `import()` (dynamic).
- Performance pass: shape-based inline caches, dead bytecode elimination.

**Exit**:
- claude.ai sign-in page renders with input fields, branding, fonts.
- Clicking "Continue with email" advances to the OTP step (network round-trip succeeds; UI updates).
- At least 90% WPT pass for our covered subset.

**Est. duration**: 10–12 weeks.

## M9 — Multi-process + stability

**Entry**: M8.

**Goal**: Tabs run in separate processes. A crash in one tab doesn't take down the browser.

**Work**:
- IPC layer in pure .NET (`System.IO.Pipes` + length-prefixed protobuf via hand-rolled serialization).
- Per-tab `WebContent` process.
- `RequestServer` process for all network I/O.
- Updates to shell to manage child-process lifecycles, OS signal handling, crash reporters.

**Exit**:
- Force-crash in one tab leaves the other tabs alive.
- IPC overhead measured at < 5% on average over a workload.

**Est. duration**: 4 weeks.

## M10 — Hardening, security, perf

**Entry**: M9.

**Goal**: Browser is good enough to daily-drive for read-only browsing.

**Work**:
- CSP enforcement.
- Mixed-content blocking.
- Permission prompts (geolocation, notifications) — even if they always say "no".
- Devtools panels: DOM tree, computed styles, network, console.
- HTTP cache on disk.
- Image decoder isolation (decode in `Starling.Net` or a child process).
- Sandbox the WebContent process where the platform supports it.

**Est. duration**: 6 weeks.

## M11 — v1.0

**Entry**: M10.

**Goal**: Ship. Real users, real bug reports, real iteration.

**Work**:
- Bug bash.
- Polish UX.
- Auto-update (managed via a signed `starling.update.json` from a hosted endpoint).
- Public release on GitHub.

**Est. duration**: 4 weeks.

## M12 — Tiled compositor + layer tree

**Entry**: M5 (paint backend stable on ImageSharp). Sequenced after M11 in
the linear plan but can run in parallel with M6–M8 since it only touches
`Starling.{Layout,Paint}` + `Starling.Gui`.

**Goal**: The paint pipeline matches the Chrome/Safari shape — a CSS
stacking-context layer tree, per-layer tile cache, compositor-thread
composite, and CSS `transform`/`opacity` animations driven on the
compositor without re-painting. Demoable end state: scrolling a 200000-px
page is smooth, transforming a promoted div for 5 seconds straight does
not bump any paint counter, and the wgpu max-texture-dimension fallback
hack in `ImageSharpBackend` is gone (texture allocations are now bounded
by the 256² tile size).

**Work** (see `tasks/M12/`):

- `wp:M12-01-viewport-clip` — paint only what's on screen.
- `wp:M12-02-picture-cache` — WebRender-style single-bitmap cache for scroll smoothness.
- `wp:M12-03-stacking-contexts` — tag promotable boxes during layout.
- `wp:M12-04-layer-tree` — split paint into a tree of `CompositorLayer`s.
- `wp:M12-05-tile-grid` — per-layer 256² tile cache with LRU.
- `wp:M12-06-invalidation` — per-tile dirty tracking; one-button repaint stays local.
- `wp:M12-07-compositor-thread` — composite off the UI thread.
- `wp:M12-08-prefetch-ring` — speculative tile paint around the viewport.
- `wp:M12-09-compositor-anim` — transform/opacity animations as pure composite.

**Exit**:
- Tile cache hit rate ≥ 95% on a sustained scroll of a content-heavy page.
- Per-frame paint time on a `transform: translateX(...)` animation of a
  promoted div is < 1 ms (composite-only).
- The `MaxWebGpuTextureDimension` guard in `ImageSharpBackend` is removed.
- `dotnet test` green; new compositor counters surfaced in the Aspire
  dashboard.

**Est. duration**: 6–8 weeks (serial; parallelizable to ~4 with two agents).

## Aggregate timeline

| Milestone | Duration | Cumulative |
|---|---|---|
| M0 walking skeleton | 1 week | 1 |
| M1 HTML+CSS static | 4 weeks | 5 |
| M2 networking | 4 weeks | 9 |
| M3 JS engine | 10 weeks | 19 |
| M4 DOM bindings | 6 weeks | 25 |
| M5 Avalonia shell | 5 weeks | 30 |
| M6 H2+WS+cookies | 4 weeks | 34 |
| M7 google.com | 8 weeks | 42 |
| M8 claude.ai | 12 weeks | 54 |
| M9 multi-process | 4 weeks | 58 |
| M10 hardening | 6 weeks | 64 |
| M11 v1.0 | 4 weeks | 68 |

Serial: **~68 weeks (~16 months)** for one full-time agent.

With the parallel work packages in [14_AGENT_TASKS.md](14_AGENT_TASKS.md), independent subsystems collapse: networking can build alongside HTML parsing, paint can build alongside DOM, etc. Realistic with 4 parallel agents: **~8–10 months**.

## Hard-truth callouts

- **JS engine is the longest pole** (M3 + M7 + M8 are all JS-adjacent). If we can buy back 30% by adopting a parser from a permissively-licensed pure-managed engine and only writing the bytecode + VM + builtins ourselves, that's worth a conversation. **The user's stated requirement is "all in .NET" / "from scratch"; we follow that.**
- **Performance vs. Chrome is not on the table for v1.** Aim for "loads claude.ai in under 10s on a fast network on a 2024-era laptop". Optimizations come post-v1.
- **Security**: we ship without GPU isolation, without full process sandboxing, without site isolation. Mark as "not for sensitive browsing". M10 narrows the gap.

## Re-planning checkpoints

After each milestone, re-rank remaining work. We expect scope changes — that's the nature of building a browser. Each agent that completes a milestone files a "post-milestone retrospective" in `docs/retros/Mn.md` with at least: what slipped, what didn't, what to drop.
