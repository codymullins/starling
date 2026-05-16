# Tessera GUI

A small .NET MAUI desktop demo that wraps the Tessera engine in an address-bar
+ viewport shell. Mac Catalyst only at v1; Windows / Android / iOS are
deferred until the engine itself matures further.

The project is part of `Tessera.sln`, which means `dotnet build` /
`dotnet test` at the repo root will need the MAUI Mac Catalyst workload
installed (`dotnet workload install maui-maccatalyst`).

## Run directly

```bash
DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer \
  dotnet run --project src/Tessera.Gui/Tessera.Gui.csproj --framework net10.0-maccatalyst
```

For a compile-only smoke test without packaging:

```bash
dotnet build src/Tessera.Gui/Tessera.Gui.csproj -f net10.0-maccatalyst -t:CoreCompile
```

## Run via Aspire

Tessera has an Aspire AppHost (`Tessera.AppHost/`) that orchestrates the
GUI plus the headless CLI under a single dashboard. Logs, stdout, and
process state from each resource land in the Aspire dashboard at
`http://localhost:18888` (port may vary):

```bash
dotnet run --project Tessera.AppHost
```

`Tessera.ServiceDefaults/` ships the standard Aspire bootstrap
(OpenTelemetry traces + metrics, resilience, health checks). Today no
project consumes it — the MAUI app and the CLI both predate any
HostBuilder-shaped startup — so the dashboard sees process-level logs but
no in-process spans. When an ASP.NET-shaped service lands (e.g. a
snapshot HTTP server for offline rendering tests), wiring it via
`builder.AddServiceDefaults()` is a one-line change.

## What's in the window

- **Address bar** — accepts `https://`, `http://`, and `file://` URLs. Enter
  submits.
- **Back / Forward / Reload** — driven by `BrowserSession.NavigationHistory`,
  so cookies and history persist for the lifetime of the app.
- **Viewport** — an `Image` control bound to the engine's PNG output, with
  `Aspect.AspectFit` scaling inside a scrollable border.
- **Status bar** — render duration, output dimensions, and the resolved URL.

## What's not in here yet

- No click-to-interact (the engine doesn't dispatch DOM events from screen
  coordinates yet — M4 work).
- No tabs (M5).
- No DevTools, scroll-within-page, selection, or zoom.
- The render is a **PNG snapshot** of a single static layout pass; JS is
  parsed and DOM-ready but not executed during a render (M4 will wire that).

This is the M2-era GUI: it lets a human eyeball what the engine produces on
real URLs, with browser-shaped chrome around it. The interactive surface
lands as JS bindings come online.
