---
id: wp:M5-img-srcset-sizes
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T00:05:00Z"
branch: "main"
depends_on:
  - wp:M5-img-css-sizing
  - wp:M2-07a-img-fetch-decode-paint
subsystem: Starling.Engine
plan_refs:
  - browser-plan/07_LAYOUT.md
---

# wp:M5-img-srcset-sizes — HTML `srcset` + `sizes` source selection and density-correction

## Goal

Honor the HTML `srcset` and `sizes` attributes on `<img>`. Today the engine
ignores both, fetches whatever `src` points at, and paints the bitmap at its
source pixel dimensions. On responsive sites this paints images two to three
times too large: e.g. docs.htmlcsstoimage.com's `<img sizes="400px"
srcset="…400w,…666w,…932w,…1198w">` with a 1192×852 source paints at
1192-divided-by-column-width instead of the author's 400-px intent.

The spec is HTML Living Standard — "Images" → "Selecting an image source" +
"Density-corrected natural width and height". When a `w`-descriptor candidate
is picked via `sizes`, the image's effective intrinsic width is the
sizes-derived CSS-pixel length, not the bitmap's actual pixel width.

## Inputs

- `src/Starling.Engine/ImageFetcher.cs` — sole caller of the image network
  fetch path. Today it only reads `src`.
- `src/Starling.Engine/Engine.cs` — calls `images.FetchAllAsync(doc, baseUrl, ct)`
  in three places; needs to forward viewport width + font size.
- `src/Starling.Layout/Tree/IImageResolver.cs` — `ResolvedImage` carries the
  intrinsic dimensions BoxTreeBuilder reads.

## Outputs

- New `Starling.Engine.Srcset` (internal) static parser/selector exposing
  `Parse(srcset)`, `ParseSourceSize(sizes, vpw, fs)`, and
  `Select(srcset, sizes, fallbackSrc, vpw, fs) → (url, correctedW, correctedH)`.
  Supports:
  - `w`- and `x`-descriptor candidates
  - `sizes` clauses: bare lengths + `(min-width: Npx) length` /
    `(max-width: Npx) length` media queries
  - Length units: px, em, rem, vw, pt
  - Cloudinary-style URLs containing unescaped commas
- `ImageFetcher.FetchAllAsync(doc, baseUrl, viewportWidthCssPx, fontSizeCssPx, ct)`
  picks the candidate URL to fetch, then stores **density-corrected**
  intrinsic width/height on `ResolvedImage` so layout naturally renders the
  image at the sizes-derived CSS-pixel width.
- `Engine.cs` forwards `options.Viewport.Width` and `options.FontSize` to
  the fetcher at every call site (initial fetch, post-script refetch).
- `tests/Starling.Engine.Tests/SrcsetTests.cs` — 13 unit tests covering
  parsing, candidate selection, media-query evaluation, the docs.htmlcsstoimage.com
  regression, and Cloudinary comma-in-URL handling.

## Acceptance

- New `SrcsetTests` pass (13/13).
- Existing engine, layout, paint, and HTML test suites remain green.
- Live-page probe against https://docs.htmlcsstoimage.com on a 1280-px
  viewport shows the cover image at 400×286 (matching the author's
  `sizes="400px"`), down from 1192×852 pre-fix.

## Notes

- This is paired with `wp:M5-img-css-sizing`. The first WP made author CSS
  apply to `<img>` (which got the docs.htmlcsstoimage.com image down to
  576×411 — the column width). This WP adds the missing `sizes` hint
  semantics so it lands at the intended 400×286.
- `dpr` (device pixel ratio) is fixed at 1.0; selection therefore prefers
  the smallest `w`-candidate that satisfies `w ≥ sourceSize`. Multi-DPR
  support is a follow-up when we add real high-DPI rendering.
- Pure-`x` srcsets without `sizes` pick the highest density. Pure-`w`
  srcsets without `sizes` pick the largest candidate (matches Chromium at
  dpr=1).

## Handoff log

- 2026-05-20T00:00Z — created + claimed by agent-copilot-claude-opus-4.7
- 2026-05-20T00:05Z — implemented `Starling.Engine.Srcset`, wired into
  `ImageFetcher.FetchAllAsync` with new viewport-aware overload, updated all
  three Engine.cs call sites to forward viewport+font size. 13 new unit tests
  pass. Full sln green. Live-page probe confirms docs.htmlcsstoimage.com
  cover image is now 400×286 (vs 1192×852 before this WP and the prior
  wp:M5-img-css-sizing fix). Completed.
