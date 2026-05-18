# Starling

Managed-first .NET 10 web browser. Built from primitives, no Chromium / Gecko / WebKit reuse.
Native interop is confined to two vetted seams (graphics + image codecs); everything
else is pure-managed.

## Status — high-level buckets

Legend: ✅ shipped · 🟡 partial / actively iterating · ⚫ not started

| Bucket | Status | Notes |
|---|---|---|
| **HTML parsing** | ✅ | Tokenizer (html5lib 100%) + spec-compliant tree builder. |
| **DOM** | ✅ | Nodes, mutations, live collections, events. |
| **CSS** | 🟡 | Tokenizer, parser, selectors (incl. `:has`, pseudo-elements, modern pseudo-classes), cascade + layers, Values 4 math (`calc`/`min`/`max`/`clamp`), Color 4 spaces + gamut mapping, Media 5 + `@supports`, CSS Nesting, `revert`/`unset`, `@font-face` + WOFF/WOFF2, 183 PropertyIds. Further property/feature coverage ongoing. |
| **Layout** | 🟡 | Block + inline + inline-block (with BFC for block children and two-pass max-content shrink-to-fit), margin collapse, `margin: auto` centering, text-align, minimal table layout via UA-stylesheet inline-block cells, form controls visible by default. Flex/grid not yet. |
| **Paint** | ✅ | Skia Graphite via native shim (`libtessera_skia`) is the **sole rasterizer** — no managed fallback. DisplayList drives both headless and GUI. osx-arm64 only today. |
| **Networking** | ✅ | URL (WPT 100%), DNS, TCP, **TLS 1.3 via BouncyCastle** (an `SslStream` migration was reverted in `939f3a5` after a macOS TLS 1.3 issue — see [AGENTS.md](AGENTS.md)), HTTP/1.1 with keep-alive connection pool, gzip/brotli/deflate, redirects, RFC 6265bis cookies + PSL, WHATWG encoding labels (43/43 curated WPT subset), CCADB root store. `tessera render https://example.com` is gated in CI. |
| **Image pipeline** | ✅ | OS-native codecs (`Starling.Codecs`: ImageIO on macOS, WIC on Windows, libjpeg/png/webp on Linux), `data:` URI support, accessible names for unrenderable `<img>`/`<svg>`. |
| **JS engine** | 🟡 | Lex + parse + bytecode compiler + register VM; functions, recursion, snapshot closures, `new`/`this`, method binding. Still ahead: intrinsics (Object/Array/String/Number/Math/JSON/Date/RegExp), Promise + microtasks, ES modules, async/await, destructuring, classes, Test262 ≥ 80%. **The single largest gating piece for interactive demos.** |
| **DOM bindings / Web APIs** | ⚫ | Blocked on JS intrinsics. A temporary `DomBindingHost` exists for read/update + click dispatch experiments. |
| **GUI shell** | 🟡 | .NET MAUI / Mac Catalyst. Chrome (Sidebar, UrlBar, StatusBar, WebviewPanel, Favicon, MiniLoadChart), DevTools panels (Console, Performance, Internals), `BrowserSession` (shared cookies + nav history across tabs), an in-process MCP server (`GuiMcpServer`) exposing browser-control tools to external agents. See [`src/Starling.Gui/`](src/Starling.Gui/). |
| **Telemetry / Aspire** | ✅ | Aspire AppHost orchestrates Gui + Headless; shared OTel + health-check bootstrap. |
| **Multi-process / sandbox / disk cache / HSTS** | ⚫ | M9+, not started. |

See [`browser-plan/13_MILESTONES.md`](browser-plan/13_MILESTONES.md) for the
milestone-by-milestone roadmap and [`tasks/INDEX.md`](tasks/INDEX.md) for the
work-package queue.

## Quickstart

Requires the [.NET SDK 10.0.100](https://dotnet.microsoft.com/) or newer.

```bash
dotnet restore
dotnet build
dotnet test

# Render the static 'hello world' fixture (bare path is auto-normalized to file://)
dotnet run --project src/Starling.Headless -- render testdata/hello.html -o out.png
# The built CLI binary is named `starling`.
```

> **Native shim required.** Skia Graphite is the engine's sole rasterizer —
> there is no managed fallback. The native `libtessera_skia` shim is gitignored
> and not committed, so on a fresh checkout `dotnet build` fails fast with an
> actionable error until you build it. See
> [`native/README.md`](native/README.md) for the two-step build
> (`./native/build-skia.sh` then the shim CMake build). Currently produced for
> osx-arm64 only.

The CLI accepts bare filesystem paths as well as well-formed `file://` URLs.
`file:///absolute/path` works; `file://relative` does not (per the WHATWG URL
spec, the segment after `//` is the authority/host, not part of the path).

The CLI's full shape is documented in [`browser-plan/02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#headless-cli-shape).
Subcommands beyond `render` and `tokenize` are still incremental and may return a
"not yet implemented" message as they light up over the remaining milestones.

## Repository layout

```
starling/
├── browser-plan/             # The entire design spec. Read 00_INDEX.md first.
├── src/                      # 12 engine modules + Headless CLI + MAUI Gui (Mac Catalyst)
├── Starling.AppHost/         # Aspire AppHost orchestrating Gui + Headless
├── Starling.ServiceDefaults/ # Shared OTel + health-check bootstrap for future services
├── tests/                    # One xUnit project per src/ module + an E2E project
├── bench/                    # BenchmarkDotNet harness
├── native/                   # libtessera_skia shim (gitignored output) + build scripts
├── testdata/                 # Fixtures (HTML, golden PNGs, WPT subsets)
└── .github/workflows/        # CI: build+test on win/mac/linux, plus an interop-seam grep
```

## Interop policy

**Managed-first, native at vetted seams.** Native interop (`LibraryImport`) is
confined to two designated projects — `Starling.Skia` (graphics) and
`Starling.Codecs` (image decode). Every other engine project stays P/Invoke-free and
takes no native dependency beyond what the .NET BCL ships. The CI `lint` job greps
the engine-project allowlist (all engine projects *except* the two interop projects)
to enforce this — see
[`02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#ci-matrix-githubworkflowsciyml).

## Working on Starling

Each subsystem has a focused doc in [`browser-plan/`](browser-plan/). For agent-facing work
packages with explicit inputs / outputs / acceptance, see
[`14_AGENT_TASKS.md`](browser-plan/14_AGENT_TASKS.md).

**Implementation agents:** start with [`AGENTS.md`](AGENTS.md) and the queue at
[`tasks/INDEX.md`](tasks/INDEX.md). Multiple agents can work in parallel — claim
an unblocked package via `./tasks/lib/claim.sh`, work on the dedicated branch,
leave a handoff-log entry on stop, and complete when merged. The full workflow
is in [`tasks/README.md`](tasks/README.md).

## License

TBD (set before public release).
