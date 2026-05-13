# Tessera

Pure-managed .NET 10 web browser. Built from primitives, no Chromium / Gecko / WebKit reuse.

> **Status:** M1 static rendering is wired end-to-end for the current supported
> subset: HTML tokenizer/tree-builder, DOM, CSS cascade, block/inline layout,
> display-list paint, and PNG output. M2 networking pieces through HTTP/1 are
> present, with redirects, meta charset sniffing, local HTTP render fixtures,
> session cookies, navigation history, a deterministic event-loop core, and a
> tiny JS-to-DOM host bridge; the next large section is live/snapshot web-page
> rendering plus images. See
> [`browser-plan/13_MILESTONES.md`](browser-plan/13_MILESTONES.md) for the roadmap.

## Quickstart

Requires the [.NET SDK 10.0.100](https://dotnet.microsoft.com/) or newer.

```bash
dotnet restore
dotnet build
dotnet test

# Render the static 'hello world' fixture (bare path is auto-normalized to file://)
dotnet run --project src/Tessera.Headless -- render testdata/hello.html -o out.png
```

The CLI accepts bare filesystem paths as well as well-formed `file://` URLs.
`file:///absolute/path` works; `file://relative` does not (per the WHATWG URL
spec, the segment after `//` is the authority/host, not part of the path).

The CLI's full shape is documented in [`browser-plan/02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#headless-cli-shape).
Subcommands beyond `render` and `tokenize` are still incremental and may return a
"not yet implemented" message as they light up over the remaining milestones.

## Repository layout

```
tessera/
├── browser-plan/          # The entire design spec. Read 00_INDEX.md first.
├── src/                   # 12 engine modules + Headless CLI + MAUI Gui (Mac Catalyst)
├── Tessera.AppHost/       # Aspire AppHost orchestrating Gui + Headless
├── Tessera.ServiceDefaults/ # Shared OTel + health-check bootstrap for future services
├── tests/                 # One xUnit project per src/ module + an E2E project
├── bench/                 # BenchmarkDotNet harness
├── testdata/              # Fixtures (HTML, golden PNGs, WPT subset eventually)
└── .github/workflows/     # CI: build+test on win/mac/linux, plus a Rule-0 grep
```

## Rule 0

Pure managed throughout. No `DllImport`, no `LibraryImport`, no native dependencies beyond what
the .NET BCL ships. The CI `lint` job greps the source tree to enforce this — see
[`02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#ci-matrix-githubworkflowsciyml).

## Working on Tessera

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
