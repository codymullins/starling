# Tessera — UX Handoff

A specification for implementing the chrome + devtools system shown in
`index.html`. Built around the **Sidecar** variant (vertical tabs, devtools
docks right) in two themes (dark, light) plus a high-contrast option.

> Companion files:
> - `index.html` — interactive canvas of all artboards
> - `theme.css` — design tokens, the source of truth
> - `chrome.jsx`, `devtools.jsx` — reference React composition

---

## 1 · Goals

Tessera serves **two audiences with one UI**:

1. **Engine hackers** building Tessera itself — need visibility into the
   parser, layout engine, JS runtime, GC, IPC channels.
2. **Web/app developers** using Tessera as their daily dev browser — need
   network waterfalls, console, performance timelines.

The chrome stays small and polished by default; debug intensity lives in
DevTools, which can be docked or detached. The page-load flame chart in
the URL bar is the one piece of debug surface that's always visible — it's
how users learn to *feel* the engine working.

**Design principles**

- **Information density on demand.** Idle chrome is one row of toolbar.
  Open devtools and you get a flight deck.
- **Color is meaning.** Every chart bar, every log row, every tree node
  uses the same category palette (HTML / CSS / JS / Layout / Paint / GC /
  Network). Learn it once, read it everywhere.
- **Soft, not slick.** Rounded corners, warm neutrals, hairline borders.
  No drop shadows on inline elements. No emoji. No gradients except where
  data needs them (waterfalls, sparklines).
- **Monospace for facts, sans for chrome.** Timestamps, paths, sizes,
  hex, code → mono. Tab titles, buttons, labels → sans.

---

## 2 · Design Tokens

All tokens flow through CSS custom properties at the `.tessera` root.
Themes are switched by `[data-theme]`, density by `[data-density]`, font
mode by `[data-type]`. **See `theme.css` for the authoritative list.**

### 2.1 Color — Dark (default)

| Token            | Value                       | Use                              |
|------------------|-----------------------------|----------------------------------|
| `--bg`           | `#0c0d10`                   | window background                |
| `--panel`        | `#14161b`                   | chrome surface, devtools shell   |
| `--surface`      | `#1b1e25`                   | input wells, cards               |
| `--raise`        | `#232730`                   | hover, raised tiles              |
| `--border`       | `rgba(255,255,255,0.06)`    | hairline dividers                |
| `--hair`         | `rgba(255,255,255,0.10)`    | stronger dividers                |
| `--text`         | `#e8e9ed`                   | primary text                     |
| `--text-2`       | `#b8bdc8`                   | secondary text                   |
| `--muted`        | `#7f8492`                   | hint text                        |
| `--faint`        | `#525866`                   | tertiary / disabled              |
| `--accent`       | `#7dd3a0`                   | mint — selection, focus, "live"  |
| `--accent-bg`    | `rgba(125,211,160,0.10)`    | pill backgrounds                 |

### 2.2 Color — Light

Warm paper, ink text, sage-mint accent. Saturation pulled down on flame
colors so they read on cream surfaces.

| Token            | Value         |
|------------------|---------------|
| `--bg`           | `#f4f3ee`     |
| `--panel`        | `#fbfaf6`     |
| `--surface`      | `#ffffff`     |
| `--text`         | `#1a1a17`     |
| `--text-2`       | `#44423c`     |
| `--muted`        | `#6e6a5e`     |
| `--accent`       | `#2f8a5e`     |

### 2.3 Color — Contrast

Pure black bg, white text, brighter accent. For accessibility and
sun-bright environments.

### 2.4 Category palette (used in flame charts, logs, trees)

| Category    | Token         | Dark      | Light     |
|-------------|---------------|-----------|-----------|
| HTML/parser | `--cat-html`  | `#7dd3a0` | `#2f8a5e` |
| CSS/style   | `--cat-css`   | `#a78bfa` | `#6a4cd0` |
| JS/script   | `--cat-js`    | `#f59e0b` | `#c47a0a` |
| Layout      | `--cat-layout`| `#60a5fa` | `#2a5dcf` |
| Paint       | `--cat-paint` | `#f472b6` | `#c93f7c` |
| GC          | `--cat-gc`    | `#ef6f7a` | `#c54250` |
| Network     | `--cat-net`   | `#22d3ee` | `#0c8398` |
| Idle        | `--cat-idle`  | `#3a3f4b` | `#d4d1c5` |

**Rule:** any timing bar, log source tag, IPC channel, or tree node uses
exactly one of these. Don't introduce new hues for new event types — pick
the closest existing category. The palette is the API.

### 2.5 Typography

| Role          | Family             | Notes                                      |
|---------------|--------------------|--------------------------------------------|
| Chrome (sans) | **Geist** 400–700  | tab titles, buttons, labels                |
| Mono          | **Geist Mono** 400–600 | timestamps, paths, code, URLs, build pill |

Sizes via tokens (comfy / compact):

| Token     | Comfy | Compact |
|-----------|-------|---------|
| `--fs-xs` | 11    | 10      |
| `--fs-sm` | 12    | 11      |
| `--fs-md` | 13    | 12      |
| `--fs-lg` | 14    | 13      |

Tabular numerals (`font-feature-settings: 'tnum'`) on all timing/metric
rows so columns line up under varying digit widths.

### 2.6 Spacing & radius

Density token rewrites these. Default (`comfy`):

| Token       | Comfy | Compact |
|-------------|-------|---------|
| `--row`     | 36px  | 28px    |
| `--row-sm`  | 30px  | 24px    |
| `--pad`     | 14px  | 10px    |
| `--gap`     | 10px  | 7px     |
| `--r`       | 12px  | 9px     |
| `--r-md`    | 10px  | 7px     |
| `--r-sm`    | 7px   | 5px     |
| `--r-pill`  | 999px | 999px   |

### 2.7 Motion

Use sparingly — chrome should feel solid, not bouncy.

- Hover/press: `120ms` linear on `background`, `color`.
- Tab activate: `180ms cubic-bezier(.2,.7,.3,1)` on `transform` for the
  accent rail; instant on text color.
- Loading spinner (tab favicon): `1s linear` infinite rotation.
- Shimmer on skeleton blocks: `1.8s linear` infinite.
- DevTools panel switch: no transition. Snap.

---

## 3 · Chrome — Sidecar Layout

A single browser window has three vertical slabs side-by-side:

```
┌─ 220 px ──┬─ flex ─────────────────┬─ flex (when open) ─┐
│ Sidebar   │ Main column            │ DevTools           │
│  tabs     │  • toolbar (44 px)     │  • tab strip       │
│           │  • webview             │  • toolbar         │
│           │  • status bar (24 px)  │  • panel body      │
└───────────┴────────────────────────┴────────────────────┘
```

DevTools opens **in-window, docked right**. Width is draggable, default
50/50 split, min 360 px for the panel. A detach action pops it into its
own window (out of scope for first cut).

### 3.1 Sidebar (220 px fixed)

Top → bottom:

1. **Wordmark row** (38 px) — "tessera" in mono, 13 px, weight 600.
   Macros title-bar drag region.
2. **Command palette stub** (28 px) — single rounded well, label
   "search · jump · run", `⌘K` keybind on the right. Clicking opens a
   centered modal overlay (out of scope for first cut; placeholder OK).
3. **Pinned section** — "PINNED" cap label (uppercase mono, 10 px), then
   tab rows. Pinned tabs survive across sessions; closing them only
   collapses to the favicon-only state.
4. **Today section** — "TODAY" cap label, then current-session tabs.
5. **Build pill footer** (bottom of sidebar) — see §3.5.

**Tab row** (height `--row-sm`)

- 12 px favicon (color-derived from host, see §3.6)
- title (sans, `--fs-sm`), ellipsis on overflow
- optional 6 px green dot on the right for audio-playing tabs
- active state: `--surface` background, 2 px accent rail flush to the
  left edge of the artboard

Hover: `--hover` background. Drag handle is the whole row.

### 3.2 Toolbar (44 px, main column)

`back · forward · reload · [URL bar grows] · save · more`

All icon buttons are 36×36 with 16 px stroke icons centered. No labels —
tooltips on hover.

### 3.3 URL bar

The defining surface. One rounded well (`--r-md`), height `--row`.

```
[lock] https://justinjackson.ca/words.html        [mini-chart] [find]
 14px  ←─────────── ellipsis on overflow ──────────→  22px      22px
```

- **Lock icon**: `--ok` when HTTPS, `--muted` when HTTP, `--err` on cert
  issues. Click opens connection inspector (cert details, HSTS, etc.).
- **URL text**: mono, `--fs-sm`. Scheme + path are `--muted`; host is
  `--text`. This makes the *origin* visually dominant.
- **Find button**: collapses to icon when devtools is open.

### 3.4 Mini load chart (in URL bar, during load)

A 140-px horizontal sparkbar stacking the request lifecycle:

```
●  ▰▰▰▰▱▱▱▱▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▰▱▱▱▱  376ms
   DNS  TLS    GET html             parse… script… style… layout… paint
   net  net    net                  html   js     css    layout   paint
```

- **Phases** (data shape):
  ```ts
  type Phase = { t: number; dur: number; cat: keyof CategoryPalette; label?: string };
  ```
- Bars are stacked by `t` (start ms) and `dur` (width ms), filled with
  `var(--cat-<category>)`.
- A 1 px vertical cursor marks the current wall-clock position during
  load. Removes itself when document is `complete`.
- Total ms label is mono, `--fs-xs`, right-aligned.
- Hover any segment → tooltip with `label · ${dur}ms`.
- After load: chart fades out over 600 ms, leaving just the URL.
- **Clicking the chart opens DevTools → Performance scoped to this
  load** — this is the bridge between "noticing it's slow" and
  "investigating".

### 3.5 Build pill

Mono, pill-shape, in the sidebar footer (Sidecar variant).

```
● M3 · flow layout · async loader · ipc sandbox
```

- Green dot = build is the head of `main`. Yellow = dirty working tree.
  Red = engine running an experimental flag.
- Flags listed comma-free, separated by middot.
- Click opens an "About this build" panel with commit SHA, build date,
  enabled flags.

### 3.6 Favicons (synthetic, M3 placeholder)

Until the favicon loader ships, generate a 12×12 colored square with the
host's first letter:

- Hue: hash of host string → `oklch(0.65 0.13 <h>)`
- Foreground: white, mono, 62% of square size
- Radius: 4 px

This stays stable across reloads and avoids the awkward "loading…
favicon" period.

### 3.7 Status bar (24 px, bottom)

Mono, `--fs-xs`. Left side: hover hint (link target, button tooltip).
Right side: live engine metrics, separated by middots.

```
↪ link to /about.html                  87 DOM · 4.2 kB · 318ms TTFB · 16.4MB heap
```

Metrics are read-only here; they're for at-a-glance, not action. Click
the heap metric to jump to DevTools → Internals → GC.

---

## 4 · Page-load lifecycle

Five visible states the chrome must represent:

| State        | Tab favicon  | URL bar                      | Status bar          |
|--------------|--------------|------------------------------|---------------------|
| Idle         | favicon      | url only                     | "↪ link to …" hover |
| Loading      | spinner      | url + **mini load chart**    | "Loading… <ms>"     |
| Stalled      | spinner (red ring) | url + chart, cursor frozen | "Stalled on TLS"  |
| Error        | err glyph    | url, red lock                | "TLS error · click for detail" |
| Loaded       | favicon      | url, chart fades over 600 ms | metrics             |

Spinner: 12 px ring, 1.5 px accent stroke, top-quarter transparent,
rotating 1 s linear infinite.

---

## 5 · DevTools

### 5.1 Shell

```
┌─ DevTools ────────────────────────────────────────────────────┐
│ ⚡ Performance  ▢ Console (2)  ⌘ Internals  ⊕ Inspect  ⊞ Net  │  ← tab strip (34 px)
├───────────────────────────────────────────────────────────────┤
│ [body of active panel]                                        │
└───────────────────────────────────────────────────────────────┘
```

- **Tab strip**: each tab is `icon · label · optional badge`. Active tab
  gets `--panel` background and an accent-colored icon. Tabs with new
  unread events (Console errors, Network failures) show a red pill badge
  with the count.
- **Dock controls** (right side of strip): dock-bottom, dock-right,
  detach, close. Dock state persists per-tab in localStorage.

### 5.2 Performance panel

The hero. From top to bottom:

1. **Toolbar (36 px)**: REC button (red), `${totalMs} · ${frames} frames
   · ${jank} jank` stats, category legend chips (HTML / CSS / JS /
   Layout / Paint / GC / Net).
2. **Frame strip (28 px)**: one block per frame, colored by FPS health.
   - `>= 60 fps` → mint-on-mint (`rgba(125,211,160,0.10)`)
   - `< 60 fps` → amber (`rgba(245,185,66,0.16)`) with `⚠`
   - Label inside: `${fps}fps · ${dur}ms`
3. **Ruler (18 px)**: 50-ms ticks in mono `--fs-xs`. Dashed accent
   verticals at named markers: **FB / FCP / LCP / TTI**. (FB = first
   byte; the rest match Web Vitals.)
4. **Thread groups**: one stack per thread. Tessera has at least
   `Main`, `Loader`, `Compositor` — extend to GPU/Audio/Worker as those
   ship. Each thread can have multiple "rows" representing call-stack
   depth — the topmost row is tasks, rows below are nested calls.
5. **Selected event detail (88 px footer)**: three columns wide:
   - **SELECTED**: event name, source file, line.
   - **TIMING**: start, self, total ms; forced-reflow callouts in amber.
   - **CALL TREE**: child events with self-time.

Bar drawing:

- Bar height: 18 px, 2 px vertical gap between rows.
- Bar background: `var(--cat-<category>)`.
- Bar text color: `var(--bar-ink)` (token that flips to dark on light
  theme, light on dark — currently `#0a0a0a` on dark, `#1a1a17` on light).
- Bars narrower than 4% of total width hide their label and rely on
  hover tooltip.
- Inner shadow: `0 0 0 0.5px rgba(0,0,0,0.25) inset` so adjacent bars
  show a hairline even when they're the same hue.

Data shape:

```ts
type Sample = {
  totalMs: number;
  frames: { t: number; d: number; fps: number; jank?: boolean }[];
  markers: { t: number; label: string; hint: string }[];
  threads: {
    name: string;
    rows: Bar[][];           // rows[0] = top stack, rows[1] = children, ...
  }[];
};
type Bar = { t: number; d: number; cat: Category; label: string };
```

### 5.3 Console panel

A structured log table, not a freeform text stream.

**Columns** (fixed widths, mono throughout):

| col         | width  | content                              |
|-------------|--------|--------------------------------------|
| Time        | 76 px  | `mm:ss.sss` since load               |
| Level       | 64 px  | `error / warn / info / log / debug`  |
| Source      | 64 px  | `tessera / parser / layout / page …` |
| Message     | flex   | text or pretty-printed object        |
| Tag         | auto   | status code, timing, count           |

**Level styling**

| Level   | Color           | Row background          |
|---------|-----------------|-------------------------|
| `error` | `--err`         | `rgba(239,111,122,0.06)`|
| `warn`  | `--warn`        | `rgba(245,185,66,0.05)` |
| `info`  | `--muted`       | none                    |
| `log`   | `--text-2`      | none                    |
| `debug` | `--cat-css`     | none                    |

Source tags are colored by category (`parser → --cat-html`,
`layout → --cat-layout`, etc.). User-page console output uses the `page`
source and `js` category color.

**Toolbar** (36 px):

- Level filter pills with live counts: `all / error / warn / info / debug`.
- Filter input (right side): mono, supports `src:layout`, `cat:net`,
  `level:>=warn`, plain substring. Live-evaluates as you type.

**Prompt** (30 px footer):

- Mono caret `›` in accent color, then current input.
- Tab-complete from page globals + Tessera-internal symbols.
- Up-arrow walks history. ⌥-Up walks across the whole session.

### 5.4 Internals panel

The engine debug surface — what no other browser gives you. A 2×2 grid
of cards. Each card has a 28-px header (`TITLE · sub` left, badge right)
and a body sized to its content.

#### 5.4.1 Parser card

A live DOM tree of the page being parsed. Each node shows:

- A 6 px status dot:
  - `parsed`   → `--ok` (green)
  - `parsing`  → `--warn` (yellow)
  - `fetching` → `--cat-net` (cyan) — element waiting on a subresource
  - `queued`   → `--faint` (gray)
- Tag name in `--cat-html`.
- Quoted text content, if any.
- Right-side resource link (cyan) for `<link>`, `<script>`, `<img>`, etc.

Badge: `${tokens} tok · ${nodes} node · ${errors} err`. Error count is
red when nonzero.

Click any node to expand attrs/computed style in a slide-down inspector
(out of scope for v1; placeholder OK).

#### 5.4.2 JS engine card

Two columns:

- **Call stack**: top frame highlighted red if an exception is pending,
  otherwise normal. Each row: `function · file:line`. The currently
  paused frame gets a `▸` indicator.
- **Heap**: a stacked-segment bar showing `JS / strings / DOM / buffers`
  composition of current heap usage, plus a four-line legend with `kB`
  values. Badge: `${used} / ${total} MB`.

#### 5.4.3 GC card

A bar chart of recent GC events, oldest left → newest right:

- Bar height: freed bytes (kB), normalized to the max in the window.
- Bar color:
  - Minor → `--cat-css` (purple)
  - Major → `--cat-gc` (red), with a small `!` marker above the bar.
- Below the chart: three metrics — `young gen`, `old gen`, `next gc`.
- Badge: `${majors} major · ${minors} minor · ${totalMs}ms total`, color
  warn if total exceeds budget.

#### 5.4.4 IPC card

One row per channel:

```
WebContent → UI         ▁▂▃▅▃▂▁▂▅█▃▂▁▂▃▅▃▂   218
UI         → WebContent ▁▃▂▁▂▃▁▂▁▁▂▃▂▁▂▃▁    47
Loader     → WebContent ▁▁▂▁▂▁▁▁▂▁▁▁           12
WebContent → Sandbox    ▁▁▁▂▁▁▁                  4
```

- Sender / arrow / receiver in mono.
- 24-bar message-rate sparkline, colored by channel role
  (`paint → --cat-paint`, `input → --cat-js`, `data → --cat-net`,
  `fs → --cat-gc`).
- Message count at the right.

Badge: `${total} msgs · ${okPct}% ok`. The `% ok` is messages that
ack'd within the channel SLA (default 16 ms).

### 5.4.5 Sub-tabs (Parser / JS / Style / Layout / Paint / GC / IPC / Sandbox)

The Internals top toolbar shows module chips. Currently they all map to
"show all four cards"; eventually each chip filters to a deep-dive view
for that module (e.g. `parser` → tokenizer state machine, `layout` →
flow tree with box-model overlays). Out of scope for v1 — keep the
chips visible so we hint at the future shape.

---

## 6 · States & interactions

### 6.1 Tab states

| State       | Visual                                              |
|-------------|-----------------------------------------------------|
| Idle        | favicon · title · subdued text color                |
| Active      | favicon · title (`--text`) · `--surface` bg · accent rail |
| Hover       | favicon · title · `--hover` bg                      |
| Loading     | spinner replaces favicon                            |
| Audio       | small accent dot at right of title                  |
| Pinned      | rendered in PINNED section, title hidden under 60px |

### 6.2 Theme switching

`[data-theme]` on `.tessera` root flips all tokens at once. No fade
transition — clean cut. Webview content (rendered pages) does **not**
follow the theme; it always renders against `--web-bg` (`#ffffff`).

A future "match-system" tweak should add `@media (prefers-color-scheme)`
resolution, but the explicit per-section value should always win.

### 6.3 Empty / failure states

- **No tabs open**: sidebar shows pinned only, main column shows a
  centered "New tab" command-palette stub.
- **Page failed to load**: webview area paints a diagnostic skeleton —
  request waterfall on top, error stack below. Not a friendly error
  page — Tessera assumes the user wants to debug.
- **DevTools recording, no events yet**: panel shows the toolbar + ruler
  + thread labels but no bars. "Press Rec to start" hint centered.

---

## 7 · Accessibility

- All interactive elements reach 4.5:1 contrast against their surface in
  every theme. Audit with the high-contrast theme as a stress test.
- Every icon button has `aria-label`. Hover tooltips are not the
  primary affordance.
- Focus ring: 2 px `--accent`, 2 px offset. Visible on keyboard nav,
  hidden on mouse (via `:focus-visible`).
- The mini load chart and flame chart bars are **decoration** of the
  underlying timing data — each clickable bar has an
  `aria-label="${cat} · ${label} · ${dur}ms"` so screen readers can
  navigate them with the rotor.
- Density `compact` must not shrink below 11-px label text.

---

## 8 · Implementation notes

- **Single source of truth for tokens.** Use CSS custom properties.
  Don't bake hex codes into components. The reference React in
  `chrome.jsx`/`devtools.jsx` follows this rule.
- **Category enum, not strings.** Define the category palette once
  (`'html' | 'css' | 'js' | 'layout' | 'paint' | 'gc' | 'net' | 'idle'`)
  and have everything (flame bars, log rows, IPC sparklines) accept it.
- **Sample data, not props soup.** Performance, Console, and Internals
  each consume one structured sample object. Easier to record, replay,
  and diff than a tree of per-component props.
- **Persist devtools state per-tab.** Active panel, dock position, panel
  width — all per-tab so a user's debugging context survives reload.
- **Mini load chart and Performance panel share data.** The chart is a
  compressed view of the same sample the Performance panel renders.
  Don't fork the data — one source.

---

## 9 · Out of scope (for this design pass)

- Settings, history, bookmarks pages.
- Tab groups, vertical-tab tree-style nesting.
- Detached devtools window chrome.
- Network panel (request list + payload viewer + cookies).
- Inspector / element picker.
- Print preview, downloads tray, extensions surface.
- Profile / account / sync.
- Mobile / tablet layout.

These should reuse the tokens and components defined here when they
land.

---

## 10 · Open questions for the implementing agent

1. **Mini load chart visibility threshold.** Do we keep it for loads
   under 200 ms (where it'll feel like a flicker), or only show it past
   a threshold?
2. **Build pill placement.** Sidebar footer (current) vs. status bar?
   Sidebar feels more "this is the engine you're running"; status bar
   feels more "live build state".
3. **Per-tab vs. global devtools.** Right now we assume per-tab. Web
   devs may want one global devtools that re-targets when they switch
   tabs — like Safari's Web Inspector.
4. **Category palette extensibility.** If we add WebGPU, audio, worker —
   do we extend the eight-color palette or partition the existing ones?
   Probably extend, but the palette will need a recheck for
   colorblind-safety past ~10 hues.

---

_End of handoff. The canvas in `index.html` is the visual reference —
when this doc and the canvas disagree, the canvas wins for visuals and
the doc wins for behavior._
