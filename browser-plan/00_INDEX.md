# Tessera Browser — Master Plan Index

> **Codename:** Tessera. Pure-managed .NET 10 web browser, modeled after Ladybird in ambition (own engine, no Chromium/Gecko/WebKit reuse) but written entirely in C# 14 with no native dependencies and no P/Invoke beyond what the .NET BCL ships.
>
> **End-state goal:** Load `https://www.google.com` (basic search) and `https://claude.ai` (full SPA) on Windows, macOS, and Linux.
>
> **Audience:** Implementation agents. This plan is the entire spec. Do not search the web for design decisions — they are made here. If a decision is missing, add it to the relevant doc rather than improvising.

---

## How to use this plan

1. Each doc is a self-contained brief for one subsystem. Read top-to-bottom.
2. Cross-links between docs are absolute file references (e.g. `07_LAYOUT.md#inline-formatting-context`).
3. Every doc ends with **Acceptance Tests** — concrete check-offs an agent uses to know they're done.
4. Search the plan with: `grep -rin "<term>" /path/to/browser-plan/`.
5. If you must defer a design call to a human, raise it as a `<!-- OPEN QUESTION -->` block in the doc, do not block forward progress.

---

## Locked decisions (do not relitigate)

| Topic | Decision | Source |
|---|---|---|
| Language | C# 14 on .NET 10 (LTS, Nov 2025 – Nov 2028) | user |
| Native code | None. Pure managed. `System.Security.Cryptography` BCL primitives are allowed; everything above the primitive layer is hand-written. | user |
| UI | Avalonia 12 (stable 12.0.x, released Apr 2026; targets .NET 10 directly; .NET 8+ only) | user |
| Rasterization | `SixLabors.ImageSharp` 3.x + `SixLabors.ImageSharp.Drawing` 2.x + `SixLabors.Fonts` 2.x | user |
| JS engine | Hand-written in C#. Jint and Acornima may be consulted as references but are **not dependencies**. | user |
| Networking | Hand-written from `System.Net.Sockets` up. No `HttpClient`, no `SslStream`. | user |
| Process model | Single-process for v1. Ladybird-style multi-process sandboxing deferred to v2. | this plan |
| Cross-platform | Windows + macOS + Linux from day one. No platform branches without an `OPEN QUESTION`. | user |
| Threading | Single-threaded UI + event loop. Worker pools for parsing/networking/JS. Details in `01_ARCHITECTURE.md`. | this plan |

---

## Documents

| # | File | What's in it |
|---|---|---|
| 00 | [00_INDEX.md](00_INDEX.md) | This page. |
| 01 | [01_ARCHITECTURE.md](01_ARCHITECTURE.md) | Module graph, public interfaces, threading model, IPC plan. |
| 02 | [02_PROJECT_SETUP.md](02_PROJECT_SETUP.md) | Solution layout, NuGet pins, `dotnet new` commands, CI matrix. |
| 03 | [03_NETWORKING.md](03_NETWORKING.md) | DNS, TCP, TLS 1.3, HTTP/1.1, HTTP/2, HPACK, cookies, gzip/brotli, URL spec. |
| 04 | [04_HTML_PARSING.md](04_HTML_PARSING.md) | WHATWG tokenizer state machine + tree construction. |
| 05 | [05_DOM.md](05_DOM.md) | DOM Living Standard subset, events, observers. |
| 06 | [06_CSS.md](06_CSS.md) | CSS parser, selectors, cascade, computed values. |
| 07 | [07_LAYOUT.md](07_LAYOUT.md) | Formatting contexts, block, inline, flex, grid. |
| 08 | [08_FONTS_PAINT.md](08_FONTS_PAINT.md) | Font handling, display list, ImageSharp.Drawing paint. |
| 09 | [09_JS_ENGINE.md](09_JS_ENGINE.md) | Lexer, parser, bytecode, VM, GC, intrinsics. |
| 10 | [10_WEB_APIS.md](10_WEB_APIS.md) | Web IDL bridge, event loop, fetch, observers, storage. |
| 11 | [11_AVALONIA_SHELL.md](11_AVALONIA_SHELL.md) | UI shell, tabs, address bar, history, downloads. |
| 12 | [12_TESTING.md](12_TESTING.md) | Unit, golden-image, WPT, Test262, fuzzing, perf. |
| 13 | [13_MILESTONES.md](13_MILESTONES.md) | M0 → M-Final phased delivery. |
| 14 | [14_AGENT_TASKS.md](14_AGENT_TASKS.md) | Parallelizable work packages by milestone. |

---

## Glossary anchors (grep-friendly)

`#tessera-net` `#tessera-url` `#tessera-html` `#tessera-dom` `#tessera-css` `#tessera-layout`
`#tessera-paint` `#tessera-js` `#tessera-bindings` `#tessera-loop` `#tessera-engine` `#tessera-shell`

`#milestone-m0` `#milestone-m1` `#milestone-m2` `#milestone-m3` `#milestone-m4` `#milestone-m5`
`#milestone-m6` `#milestone-m7` `#milestone-m8` `#milestone-m9` `#milestone-m10` `#milestone-final`

---

## Status

| Date | Author | Note |
|---|---|---|
| 2026-05-11 | Claude (planning pass) | Initial handoff plan. |
| 2026-05-11 | Claude (update pass) | Bumped UI shell from Avalonia 11.12 to Avalonia 12 (stable). See `11_AVALONIA_SHELL.md` and the `Migration notes (vs Avalonia 11)` section. |
| 2026-05-11 | Claude (scope cut) | Dropped obsolescent surfaces from v1: TLS 1.2 fallback (`03_NETWORKING.md`), `document.write` (`04_HTML_PARSING.md`), `keypress` event (`05_DOM.md`), `XMLHttpRequest` (`10_WEB_APIS.md`). Each remains as a loud-failure stub so feature-detection paths still work. |
| 2026-05-11 | Claude (wp:M2-05) | Landed HTTP/1.1 client: `H1RequestWriter`, `H1ResponseParser` (status line + headers + chunked / Content-Length / EOF body framing), `BodyDecoder` (gzip / br / deflate stacked in reverse list order), and the `TesseraHttpClient` facade wired onto TCP + BouncyCastle TLS. Live `GET https://example.com` returns 200 + HTML body (test gated by `TESSERA_LIVE_HTTP_TESTS=1`). 60 new unit tests; full suite green. |
| 2026-05-11 | Claude (wp:M2-06) | Landed cookies: RFC 6265bis-shaped `CookieParser`, `CookieJar` (host-only / domain matching, path matching with slash boundary, Secure / HttpOnly / SameSite, `__Host-` / `__Secure-` prefix rules, Max-Age beats Expires), and bundled Mozilla Public Suffix List (~16k rules). `TesseraHttpClient` injects `Cookie` from the jar and stores `Set-Cookie` from responses. 45 new unit + integration tests; full suite green. |
| 2026-05-11 | Claude (wp:M2-07 partial) | Wired `TesseraHttpClient` into `TesseraEngine` so `tessera render https://example.com -o out.png` now runs the full DNS → TCP → TLS → HTTP/1.1 → HTML parse → paint pipeline end-to-end. Added charset sniffing (Content-Type → BOM → UTF-8). **Blocked on M1 layout/paint** for the SSIM-against-golden acceptance criterion: rendering still uses the M0 text-on-white painter; will upgrade automatically once the in-flight box-tree / layout / display-list work lands. 4 new engine integration tests; full suite green. |
