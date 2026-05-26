# Starling.Paint.Tests

Tests for `src/Starling.Paint` — the display list and the rasterizer.

## What it covers

Two layers:

1. **Display list** — turning the box tree into a `DisplayList`. The same
   list drives the headless renderer and the Avalonia GUI.
2. **Golden images** — the rendered PNG compared to a checked-in PNG using
   a similarity score. On failure the runner writes the actual and diff
   images next to the expected one. Fixtures live under `testdata/golden/`.
   See [`12_TESTING.md`](../../browser-plan/12_TESTING.md#golden-image-testing).

## How to run

```bash
dotnet test tests/Starling.Paint.Tests
```

## What the badge means

The milestone 1 paint baseline is complete. One display list drives both
backends and real sites render (see the screenshot in the project
[`README.md`](../../README.md)).

Design: [`08_FONTS_PAINT.md`](../../browser-plan/08_FONTS_PAINT.md).
