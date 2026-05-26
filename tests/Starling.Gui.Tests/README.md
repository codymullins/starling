# Starling.Gui.Tests

Tests for the Avalonia desktop shell in `src/Starling.Gui`.

## What it covers

The URL bar, tabs, history, downloads, DevTools panels, shared-session
tabs, and the in-process Model Context Protocol server that lets agents
drive the browser (`browser_screenshot`, `browser_inspect`,
`browser_click`, `browser_move`, `browser_type`).

[`Starling.Gui.Headless.Tests`](../Starling.Gui.Headless.Tests) drives the
same UI without a display, which is what CI runs.

## How to run

```bash
dotnet test tests/Starling.Gui.Tests           # needs a display
dotnet test tests/Starling.Gui.Headless.Tests  # no display needed
```

## What the badge means

Usable day to day — tabs, history, DevTools, and agent control all work.
Real sites render end to end (see the screenshot in the project
[`README.md`](../../README.md)). Longer UI features like a downloads
panel, preferences, and profiles are still to come.

Design: [`11_AVALONIA_SHELL.md`](../../browser-plan/11_AVALONIA_SHELL.md).
