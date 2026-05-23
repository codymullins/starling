# Starling

Managed-first .NET 10 web browser. Built from primitives, no Chromium / Gecko / WebKit reuse.
Native interop is confined to one vetted seam (image codecs); everything
else — including paint — is pure-managed.

![Starling rendering netclaw.dev — an Astro + Redux-Toolkit site — in the Avalonia GUI shell](docs/screenshot.png)

<sub>netclaw.dev is shown only as a rendering target; Starling Browser has no direct affiliation with Netclaw.</sub>

## Why?

The conventional wisdom is that a browser engine has to be C++ or Rust, built by
a large team over many years. [Ladybird](https://ladybird.org) has already proven
the hardest part — that a genuinely independent engine, built from primitives
with no Chromium/Gecko/WebKit reuse, is viable. Starling shares that
from-scratch spirit but asks the next question: do you still need a systems
language to do it? Arc and Dia build their browsers in Swift — even on Windows —
so the assumption that it all has to be C++ or Rust is already weaker than people
think. Starling pushes further — a managed-first .NET engine, with native code
confined to a single vetted seam, built in the open, independent of any browser
vendor.

The longer-term motivation is a specific frustration: how slowly the standards
process has delivered things like first-class WASM/WASI access to the DOM. The
plan is to earn parity on real-world websites first, then use the managed
architecture to make WASM a first-class citizen — hitting the DOM directly, not
as a guest behind a JS bridge. And part of it, honestly, is just wanting to find
out whether it can be done.

## Contents

- [Status](#status--high-level-buckets)
- [Quickstart](#quickstart)
  - [Six Labors license](#six-labors-license)
- [Repository layout](#repository-layout)
- [Interop policy](#interop-policy)
- [Working on Starling](#working-on-starling)
- [License](#license)

## Status — high-level buckets

Legend: ✅ shipped · 🟡 partial / actively iterating · ⚫ not started

Each bucket links to its design doc in [`browser-plan/`](browser-plan/) for the
full feature inventory.

| Bucket | Status | Summary |
|---|---|---|
| [**HTML parsing**](browser-plan/04_HTML_PARSING.md) | ✅ | Tokenizer (html5lib 100%) + spec-compliant tree builder. |
| [**DOM**](browser-plan/05_DOM.md) | ✅ | Nodes, mutations, live collections, events. |
| [**CSS**](browser-plan/06_CSS.md) | 🟡 | Full selector grammar (incl. `:has`), cascade + layers, Values 4 math, Color 4, Media 5 + `@supports`, nesting, `@font-face` + WOFF/WOFF2. More properties ongoing. |
| [**Layout**](browser-plan/07_LAYOUT.md) | 🟡 | Block, inline, inline-block, margin collapse, minimal tables, Flexbox, and minimal CSS Grid. More flex/grid ongoing. |
| [**Paint**](browser-plan/08_FONTS_PAINT.md) | ✅ | Pure-managed ImageSharp.Drawing 3; one DisplayList drives headless + GUI. WebGPU backend by default (`STARLING_PAINT_BACKEND=imagesharp` for CPU). |
| [**Networking**](browser-plan/03_NETWORKING.md) | ✅ | URL (WPT 100%), DNS/TCP, TLS 1.3 (BouncyCastle), HTTP/1.1 + keep-alive pool, gzip/brotli/deflate, redirects, RFC 6265bis cookies + PSL, CCADB roots. |
| [**Image pipeline**](browser-plan/08_FONTS_PAINT.md) | ✅ | OS-native codecs (ImageIO / WIC / libjpeg-png-webp), `data:` URIs, accessible names for unrenderable images. |
| [**JS engine**](browser-plan/09_JS_ENGINE.md) | 🟡 | Full ES2024 parse → bytecode → register VM: intrinsics, Promise + async/await, generators, classes, ES modules (top-level await, dynamic `import()`). Test262 `language` ≈ 81%. A runtime-selectable [**Jint** backend](browser-plan/09_JS_ENGINE.md#alternative-engine-backend-jint) (`STARLING_JS_ENGINE=jint`, ≈ 99.6% Test262 `language`) is available as a temporary high-compat crutch. |
| [**DOM bindings / Web APIs**](browser-plan/10_WEB_APIS.md) | 🟡 | Full-grammar `querySelector*`, `innerHTML`/`insertAdjacentHTML`, `fetch`, `XMLHttpRequest`, timers, storage, observers, HTML-spec script loading (async/defer + dynamic injection). Runs real-world bundles — a full Astro + Redux-Toolkit site (netclaw.dev) renders with zero JS-engine errors. |
| [**GUI shell**](browser-plan/11_AVALONIA_SHELL.md) | 🟡 | Avalonia 12 desktop (win/mac/linux): browser chrome, DevTools panels, shared-session tabs, and an in-process MCP server for agent control. |
| **Telemetry / Aspire** | ✅ | Aspire AppHost orchestrates Gui + Headless; shared OTel + health-check bootstrap. |
| **Multi-process / sandbox / disk cache / HSTS** | ⚫ | M9+, not started. |

See [`browser-plan/13_MILESTONES.md`](browser-plan/13_MILESTONES.md) for the
milestone-by-milestone roadmap and [`tasks/INDEX.md`](tasks/INDEX.md) for the
work-package queue.

## Quickstart

You'll need:

- The [.NET SDK 10.0.100](https://dotnet.microsoft.com/) or newer.
- A Six Labors license key for the paint backend — a **free community license**
  takes a couple of minutes. See [Six Labors license](#six-labors-license) below.

```bash
dotnet restore
dotnet build
dotnet test
```

**Launch the browser.** `aspire run` brings up the full app — the Avalonia GUI
shell plus the headless renderer, orchestrated by Aspire (the dashboard URL
prints on stdout, typically <http://localhost:18888>). This is the desktop
browser pictured above.

```bash
aspire run
```

**Render a live site from the CLI.** The headless renderer fetches and paints a
real URL straight to a PNG:

```bash
# The built CLI binary is named `starling`.
dotnet run --project src/Starling.Headless -- render https://example.com -o example.png
```

`starling render https://example.com` is exercised in CI, and real-world bundles
render end-to-end — e.g. netclaw.dev (an Astro + Redux-Toolkit site, pictured
above). You can also point it at a local fixture for an offline smoke test (bare
paths are auto-normalized to `file://`):

```bash
dotnet run --project src/Starling.Headless -- render testdata/hello.html -o out.png
```

The CLI accepts bare filesystem paths as well as well-formed `file://` URLs.
`file:///absolute/path` works; `file://relative` does not (per the WHATWG URL
spec, the segment after `//` is the authority/host, not part of the path). Its
full shape is documented in [`browser-plan/02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#headless-cli-shape);
subcommands beyond `render` and `tokenize` are still incremental and may return a
"not yet implemented" message as they light up over the remaining milestones.

### Six Labors license

The engine paints via **ImageSharp.Drawing 3** — pure-managed, no native
graphics shim to build — which is commercially licensed by Six Labors. The repo
does **not** ship a license key (`sixlabors.lic` is gitignored), so each
contributor supplies their own. **Applying for a community license is quick and
easy: <https://licensing.sixlabors.com/>.** Save the key as `sixlabors.lic` in
the repository root and the build picks it up automatically (CI uses the
`SIXLABORS_LICENSE_KEY` secret instead).

## Repository layout

```
starling/
├── browser-plan/             # The entire design spec. Read 00_INDEX.md first.
├── src/                      # 14 engine modules + Headless CLI + Avalonia Gui (win/mac/linux)
├── Starling.AppHost/         # Aspire AppHost orchestrating Gui + Headless
├── Starling.ServiceDefaults/ # Shared OTel + health-check bootstrap for future services
├── tests/                    # One xUnit project per src/ module + an E2E project
├── bench/                    # BenchmarkDotNet harness
├── testdata/                 # Fixtures (HTML, golden PNGs, WPT subsets)
└── .github/workflows/        # CI: build+test on win/mac/linux, plus an interop-seam grep
```

## Interop policy

**Managed-first, native at vetted seams.** Native interop (`LibraryImport`) is
confined to one designated project — `Starling.Codecs` (image decode). Every
other engine project stays P/Invoke-free and takes no native dependency beyond
what the .NET BCL ships. The CI `lint` job greps the engine-project allowlist
(all engine projects *except* the Codecs interop project) to enforce this — see
[`02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#ci-matrix-githubworkflowsciyml).

## Working on Starling

Each subsystem has a focused doc in [`browser-plan/`](browser-plan/). For agent-facing work
packages with explicit inputs / outputs / acceptance, see
[`14_AGENT_TASKS.md`](browser-plan/14_AGENT_TASKS.md).

**New here?** Start with the design spec ([`browser-plan/00_INDEX.md`](browser-plan/00_INDEX.md))
and the engineering conventions in [`AGENTS.md`](AGENTS.md). Bug reports,
questions, and proposals are welcome via GitHub issues.

**Implementation agents:** start with [`AGENTS.md`](AGENTS.md) and the queue at
[`tasks/INDEX.md`](tasks/INDEX.md). Multiple agents can work in parallel — claim
an unblocked package via `./tasks/lib/claim.sh`, commit directly to `main` with
the wp id in the subject, leave a handoff-log entry on stop, and mark complete
via `./tasks/lib/claim.sh complete <wp-id>`. The full workflow is in
[`tasks/README.md`](tasks/README.md).

## License

[BSD 2-Clause License](LICENSE) — Copyright (c) 2026, Cody Mullins.
