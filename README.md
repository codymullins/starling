# Starling

An independent web browser, built from scratch — no Chromium, Gecko, or WebKit. Written in .NET.

![Starling rendering netclaw.dev — an Astro + Redux-Toolkit site — in the Avalonia GUI shell](docs/screenshot.png)

<sub>netclaw.dev is shown only as a rendering target; Starling Browser has no direct affiliation with Netclaw.</sub>

## Why?

The conventional wisdom is that a browser engine has to be C++ or Rust, built by
a large team over many years. [Ladybird](https://ladybird.org) has already proven
the hardest part: that a genuinely independent engine is viable. Starling shares
that from-scratch spirit but asks the next question — do you still need a systems
language? Arc and Dia build their browsers in Swift, even on Windows, so the
C++-or-Rust assumption is already weaker than it looks. Starling pushes further:
a managed-first .NET engine, built in the open, independent of any browser
vendor.

The longer-term motivation is a specific frustration — how slowly the standards
process has delivered things like first-class WASM/WASI access to the DOM. The
plan is to earn parity on real-world sites first, then use the managed
architecture to make WASM a first-class citizen, hitting the DOM directly rather
than as a guest behind a JS bridge. And part of it, honestly, is just wanting to
find out whether it can be done.

## Project Status

_Last updated: 2026-05-25_

| Area | Status |
|---|---|
| Overall | ![Experimental](https://img.shields.io/badge/status-experimental-red) |
| ECMAScript | ![Test262](https://img.shields.io/badge/Test262_language-95%25-brightgreen) |
| Web Platform | ![WPT](https://img.shields.io/badge/WPT-25%25-orange) |
| DOM | ![DOM](https://img.shields.io/badge/DOM-partial-yellow) |
| HTML Parser | ![HTML](https://img.shields.io/badge/HTML-spec_compliant-brightgreen) |
| CSS | ![CSS](https://img.shields.io/badge/CSS-partial-yellow) |
| Layout | ![Layout](https://img.shields.io/badge/layout-partial-yellow) |
| Paint | ![Paint](https://img.shields.io/badge/paint-shipped-brightgreen) |
| Networking | ![Networking](https://img.shields.io/badge/networking-partial-yellow) |
| JavaScript | ![JavaScript](https://img.shields.io/badge/JS-partial-yellow) |
| Web APIs / DOM bindings | ![WebAPIs](https://img.shields.io/badge/Web_APIs-partial-yellow) |
| GUI shell | ![GUI](https://img.shields.io/badge/GUI-partial-yellow) |
| Multi-process / sandbox | ![Sandbox](https://img.shields.io/badge/sandbox-not_started-lightgrey) |
| Security | ![Security](https://img.shields.io/badge/security-not_hardened-red) |

Each area has a focused design doc in [`browser-plan/`](browser-plan/) with the
full feature inventory. See [`browser-plan/13_MILESTONES.md`](browser-plan/13_MILESTONES.md)
for the milestone-by-milestone roadmap and [`tasks/INDEX.md`](tasks/INDEX.md)
for the work-package queue.

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

**Choosing engines / backends.** Pass selection flags after `aspire run --`:

```bash
# Defaults are --starling (JS VM) and --imagesharp-gpu (WebGPU paint).
aspire run -- --jint --imagesharp     # Jint JS engine + CPU paint backend
```

**Render a live site from the CLI.** The headless renderer fetches and paints a
real URL straight to a PNG:

```bash
# The built CLI binary is named `starling`.
dotnet run --project src/Starling.Headless -- render https://example.com -o example.png
```

This is exercised in CI, and real-world bundles render end-to-end (netclaw.dev,
pictured above). You can also point it at a local fixture — bare filesystem paths
auto-normalize to `file://`:

```bash
dotnet run --project src/Starling.Headless -- render testdata/hello.html -o out.png
```

The CLI's full shape is documented in [`browser-plan/02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#headless-cli-shape).

### Six Labors license

The engine paints via **ImageSharp.Drawing 3** — pure-managed, no native
graphics shim to build — which is commercially licensed by Six Labors. The repo
does **not** ship a license key (`sixlabors.lic` is gitignored), so each
contributor supplies their own. **Applying for a community license is quick and
easy: <https://licensing.sixlabors.com/>.** Save the key as `sixlabors.lic` in
the repository root and the build picks it up automatically (CI uses the
`SIXLABORS_LICENSE_KEY` secret instead).

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
