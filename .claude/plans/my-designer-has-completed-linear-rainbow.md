# Hand-translate the Sidecar design into the MAUI GUI

## Context

The designer delivered a first-pass UX handoff in `design/` — a React/JSX + CSS
prototype of the **Sidecar** browser chrome (vertical tab sidebar, slim toolbar,
URL bar with mini load chart, status bar) plus a docked **DevTools** surface
(Performance / Console / Internals). It is a strong, sample-data-driven spec.

The actual GUI (`src/Tessera.Gui/`) is **.NET MAUI / Mac Catalyst**, built
imperatively in C#. There is no React in the repo and no way to run JSX in MAUI,
so the handoff is consumed as a **visual/behavioral spec only** — every token and
component is hand-translated to C#. The current `MainPage.cs` is the M2-era shell
(one toolbar row, no tabs, no devtools); this work rebuilds the window to the
Sidecar layout with DevTools driven by **static mock data** (no engine
instrumentation exists yet to feed it live).

Authoritative sources: `design/HANDOFF.md` (behavior), the `design/*.jsx` canvas
(visuals — canvas wins on visual disagreements), `design/theme.css` (tokens).

### Handoff punch list (report only — not fixed as part of this work)

Reported to the designer, left as-delivered in `design/`:
1. `macos-window.jsx` + `tweaks-panel.jsx` are unused (not loaded by `index.html`;
   `app.jsx` re-implements `Tweaks`/`WinShell` inline).
2. `HANDOFF.md` companion-file list omits `app.jsx` and `design-canvas.jsx`.
3. `index (2).html` not renamed to `index.html`.
4. `MiniLoadChart` tooltips render `undefined` — `app.jsx` URL-bar phases (lines
   48–57) have no `label` field.
5. `devtools.jsx` `LOGS` uses `cat: 'ipc'` → `var(--cat-ipc)`, which doesn't exist
   in `theme.css` (palette has no `ipc`).
6. `tnum` (tabular numerals, HANDOFF §2.5) is not applied anywhere.
7. Minor: `contrast` theme doesn't override `--cat-*`/status/`--web-*`; flame bars
   lack `aria-label` (§7); console filter-pill counts hardcoded/slightly off;
   `Geist` `@import` from Google Fonts should be verified to resolve.

These are translation references — the MAUI port should do the *right* thing
(real categories, applied `tnum`, accessible bars), not replicate the bugs.

## Approach

Build in four phases. All four are in scope this round, but they layer cleanly.

### Phase 1 — Token system (foundation)

Port `theme.css` custom properties into MAUI and make theme/density/type
switchable at runtime (the design's Tweaks panel flips these live).

- **`Theme/ThemeTokens.cs`** (new) — the token values: 3 themes (dark/light/
  contrast), 2 densities (comfy/compact), 2 type modes (sans/mono). One immutable
  record per (theme) with all color tokens; density + type as separate small
  records. Mirror `theme.css` names 1:1 (`Bg`, `Panel`, `Surface`, `Accent`,
  `CatHtml`…). Include `--cat-*`/status/`--web-*` overrides for `contrast` that
  the CSS omits (punch-list item 7).
- **`Theme/ThemeManager.cs`** (new) — writes the active token set into the
  app-level `ResourceDictionary` so widgets bind via `DynamicResource` and a
  theme switch repaints without rebuilding the tree. This is the MAUI analogue of
  `[data-theme]` on `.tessera`. Exposes `SetTheme/SetDensity/SetType`.
- Register `ThemeManager` as a singleton in `MauiProgram.cs`; seed the dictionary
  in `App.CreateWindow`.
- **Reuse:** the existing `Border` + `RoundRectangle` StrokeShape idiom and the
  `ChromeButton`/`AccentButton` factory pattern in `MainPage.cs` — keep these
  factories but have them pull from `DynamicResource` instead of the hardcoded
  `Palette` static class. Delete `Palette` once nothing references it.

### Phase 2 — Chrome shell

Replace `MainPage.BuildLayout()` with the Sidecar 3-slab layout. `MainPage.cs`
stays the composition root but slims down; chrome pieces move to `Chrome/`.

- **`Chrome/Icons.cs`** (new) — port the `ICONS` path-data dict from `chrome.jsx`.
  The SVG path strings are valid geometry; build `Microsoft.Maui.Controls.Shapes.Path`
  via `Geometry` from each string. One `IconView` helper (16×16 viewbox, 1.5px
  stroke, `currentColor` → bound brush).
- **`Chrome/Sidebar.cs`** (new) — `TabStripB`: 220px fixed column, wordmark row,
  command-palette stub well, PINNED/TODAY sections, build-pill footer. Tab rows
  with favicon, title, audio dot, active accent rail.
- **`Chrome/Favicon.cs`** (new) — synthetic 12×12 colored square + host initial.
  Needs an `oklch(0.65 0.13 h)` → RGB conversion (MAUI `Color` has no oklch);
  small helper, hue from the same host-hash as `chrome.jsx`.
- **`Chrome/Toolbar.cs`, `UrlBar.cs`, `BuildPill.cs`, `StatusBar.cs`** (new) —
  direct ports. URL bar: lock icon + scheme/host/path with muted scheme+path,
  find button, mini load chart slot.
- **`Chrome/MiniLoadChart.cs`** (new) — a `GraphicsView` + `IDrawable`; stacked
  category bars positioned by `t`/`dur`. See Phase 4 for the drawing approach.
- **Preserve** the existing webview interaction code in `MainPage.cs`
  (`OnPagePointerMoved`, `OnPagePanUpdated`, `FindNext`, `BoxHitTester` usage,
  `_pageCanvas` overlays) — extract it into a `Chrome/WebviewPanel.cs` that owns
  `_pageScroll`/`_pageImage`/`_pageCanvas` and the gesture handlers unchanged.

### Phase 3 — DevTools shell + Console

- **`DevTools/DevToolsPanel.cs`** (new) — the docked shell: tab strip
  (Performance / Console / Internals / Inspect / Net), dock controls, body host.
  Lives in the 3rd grid column of `MainPage`, collapsible, default 50/50 split.
- **`DevTools/SampleData.cs`** (new) — C# mirrors of `PERF`, `LOGS`, the parser
  tree, JS/GC/IPC card data from `devtools.jsx`. Use a real `Category` enum
  (`Html, Css, Js, Layout, Paint, Gc, Net, Idle`) — no `ipc`/`console`/`boot`
  strings (punch-list 5); map log sources to the nearest real category.
- **`DevTools/ConsolePanel.cs`** (new) — `CollectionView` with fixed-width
  columns (Time/Level/Source/Message/Tag), level row tinting, filter pills,
  filter input, prompt footer. Apply `tnum` to the mono columns (punch-list 6).

### Phase 4 — DevTools Performance + Internals (custom drawing)

Flame charts, sparklines, GC bars, the heap bar, IPC sparklines: all custom-drawn
via `GraphicsView` + `IDrawable` — the MAUI primitive for this. One reusable
`FlameRowDrawable` covers Performance bars *and* the URL-bar mini chart
(HANDOFF §8: "mini load chart and Performance panel share data").

- **`DevTools/PerformancePanel.cs`** + **`FlameChartDrawable.cs`** (new) —
  toolbar (REC, stats, legend chips), frame strip, 50ms ruler with FB/FCP/LCP/TTI
  markers, per-thread flame rows, selected-event detail footer.
- **`DevTools/InternalsPanel.cs`** (new) — 2×2 card grid. `Card` helper
  (28px header + body). Parser card (DOM tree + state dots), JS card (call stack
  + heap bar), GC card (`GcBarsDrawable`), IPC card (`SparklineDrawable` per
  channel).
- Make drawn bars accessible: `SemanticProperties` / focusable hit-targets with
  `aria-label`-equivalent descriptions (HANDOFF §7, punch-list 7).

## Critical files

| File | Change |
|------|--------|
| `src/Tessera.Gui/MainPage.cs` | Rewrite layout to Sidecar; extract webview into `WebviewPanel`; drop `Palette` |
| `src/Tessera.Gui/MauiProgram.cs` | Register `ThemeManager` singleton |
| `src/Tessera.Gui/App.cs` | Seed theme `ResourceDictionary` on window create |
| `src/Tessera.Gui/Tessera.Gui.csproj` | Add `MauiFont` entries for Geist / Geist Mono |
| `src/Tessera.Gui/Resources/Fonts/` (new) | Bundle Geist + Geist Mono `.ttf` |
| `src/Tessera.Gui/Theme/` (new) | `ThemeTokens.cs`, `ThemeManager.cs` |
| `src/Tessera.Gui/Chrome/` (new) | `Icons.cs`, `Sidebar.cs`, `Favicon.cs`, `Toolbar.cs`, `UrlBar.cs`, `MiniLoadChart.cs`, `BuildPill.cs`, `StatusBar.cs`, `WebviewPanel.cs` |
| `src/Tessera.Gui/DevTools/` (new) | `DevToolsPanel.cs`, `SampleData.cs`, `ConsolePanel.cs`, `PerformancePanel.cs`, `FlameChartDrawable.cs`, `InternalsPanel.cs`, card/sparkline drawables |

## Key decisions

- **Live theming via `ResourceDictionary` + `DynamicResource`**, not a static
  `Palette` class — it's the MAUI equivalent of `[data-theme]` and the only clean
  way to get the Tweaks panel's live switching without rebuilding the tree.
- **`GraphicsView` + `IDrawable` for every chart** (flame rows, mini load chart,
  sparklines, GC/heap bars). MAUI has no SVG/canvas primitive otherwise, and one
  `FlameRowDrawable` is shared by Performance and the URL bar per HANDOFF §8.
- **Icons as `Shapes.Path`** — the `ICONS` path strings port directly as geometry.
- **DevTools data is static mock** mirroring `devtools.jsx`, behind a `Category`
  enum — when engine instrumentation lands later it swaps the data source, not
  the views.
- **Webview interaction is preserved verbatim** — only relocated into
  `WebviewPanel.cs`. No behavior change to hit-testing, find, or selection.
- Fonts must be bundled (`Resources/Fonts/`) — none are today.

## Verification

1. **Compile smoke:** `dotnet build src/Tessera.Gui/Tessera.Gui.csproj -f net10.0-maccatalyst -t:CoreCompile`
2. **Run:** `DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer dotnet run --project src/Tessera.Gui/Tessera.Gui.csproj --framework net10.0-maccatalyst`
3. **Chrome:** sidebar renders with pinned/today tabs + build pill; toolbar +
   URL bar + status bar match the dark artboard in `design/index.html`.
4. **Theme switch:** flipping dark/light/contrast + comfy/compact + sans/mono
   repaints live with no tree rebuild; compare side-by-side with the canvas.
5. **DevTools:** open the dock — Performance shows flame rows + frame strip +
   ruler markers; Console shows the tinted log table + filter pills; Internals
   shows the 2×2 cards with parser tree, heap bar, GC bars, IPC sparklines.
6. **Regression:** navigate a real URL — page render, link hover, drag-select,
   and Cmd-F find all still work (webview interaction code unchanged).
7. Side-by-side the running app against `design/index.html` artboards; the canvas
   is the visual source of truth.
