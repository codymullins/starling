# Tessera

Pure-managed .NET 10 web browser. Built from primitives, no Chromium / Gecko / WebKit reuse.

> **Status:** M0 (walking skeleton). See [`browser-plan/13_MILESTONES.md`](browser-plan/13_MILESTONES.md) for the roadmap.

## Quickstart

Requires the [.NET SDK 10.0.100](https://dotnet.microsoft.com/) or newer.

```bash
dotnet restore
dotnet build
dotnet test

# Render the M0 'hello world' fixture (bare path is auto-normalized to file://)
dotnet run --project src/Tessera.Headless -- render testdata/hello.html -o out.png
```

The CLI accepts bare filesystem paths as well as well-formed `file://` URLs.
`file:///absolute/path` works; `file://relative` does not (per the WHATWG URL
spec, the segment after `//` is the authority/host, not part of the path).

The CLI's full shape is documented in [`browser-plan/02_PROJECT_SETUP.md`](browser-plan/02_PROJECT_SETUP.md#headless-cli-shape).
Subcommands beyond `render` are stubbed in M0 and return a "not yet implemented" message — they
light up incrementally over M1–M4.

## Repository layout

```
tessera/
├── browser-plan/          # The entire design spec. Read 00_INDEX.md first.
├── src/                   # 12 engine modules + Headless CLI + Avalonia Shell
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

## License

TBD (set before public release).
