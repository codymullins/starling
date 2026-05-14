---
id: "wp:M3-06c-decoded-image"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-image"
claimed_at: "2026-05-14T14:42:59Z"
branch: "main"
depends_on: []
blocks:
  - "wp:M3-06d-codecs"
  - "wp:M3-06i-skia-backend"
subsystem: "Tessera.Common"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06c-decoded-image — `DecodedImage` seam in `Tessera.Common`

## Goal

Phase 8 (seam half): define a backend-neutral `DecodedImage` type in
`Tessera.Common` and thread it through every place the engine currently passes
an ImageSharp `Image<Rgba32>` or untyped `object Source`. This decouples the
image-decode contract from any one decoder so `Tessera.Codecs` (native) can be
swapped in later. **ImageSharp stays working** as the decoder + rasterizer in
this package — this is a type seam, not a behavior change — so the package is
independently mergeable to `main` without breaking the running engine.

## Inputs

- No code dependencies; the `DecodedImage` type definition is immediate and the
  threading is mechanical.
- Existing image path: `IImageResolver.ResolvedImage`, `DisplayItem.DrawImage`,
  `DisplayListBuilder`, `ImageFetcher`, `ImageSharpBackend`, `BoxTreeRenderer`.

## Outputs

- `src/Tessera.Common/Image/DecodedImage.cs` — `{ int Width, int Height,
  ReadOnlyMemory<byte> Pixels }` (straight RGBA8888), `IDisposable`.
- `src/Tessera.Layout/Tree/IImageResolver.cs` — `ResolvedImage` carries
  `DecodedImage` instead of `object Source` / `Image<Rgba32>`.
- `src/Tessera.Paint/DisplayList/DisplayItem.cs` — `DrawImage` variant carries a
  `DecodedImage` (this is the contended file — land this package **first** so
  `06i-skia-backend` builds on the final signature).
- `src/Tessera.Paint/DisplayList/DisplayListBuilder.cs` — emits `DrawImage` with
  `DecodedImage`.
- `src/Tessera.Engine/ImageFetcher.cs` — produces `DecodedImage` (still via
  ImageSharp decode in this package).
- `src/Tessera.Paint/Backend/ImageSharpBackend.cs` — blits from `DecodedImage`
  pixels (ImageSharp still does the rasterizing).
- `src/Tessera.Gui/BoxTreeRenderer.cs` — reads `DecodedImage`.
- Test updates so the existing 3-case image-paint golden suite still passes.

## Acceptance

- `DecodedImage` exists in `Tessera.Common` with the exact shape above and is
  `IDisposable`.
- `object Source` / `Image<Rgba32>` no longer appears in `IImageResolver`,
  `DisplayItem.DrawImage`, `DisplayListBuilder`, `ImageFetcher`, or
  `BoxTreeRenderer` — all go through `DecodedImage`.
- ImageSharp still decodes and rasterizes; the engine renders identically.
- The existing image-paint golden tests pass byte-exact (no pixel change).
- Full repo `dotnet test` stays green at the current count.
- This package is mergeable to `main` standalone without breaking the engine.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 8,
  "DecodedImage seam").
- **File-contention:** `DisplayItem.cs` is also edited by `06i-skia-backend`.
  The coordination rule is explicit in the master plan — land `06c` first.
- `06d-codecs` depends on this: `ImageFetcher`'s decode call later becomes
  `NativeImageDecoder.Decode(bytes)` returning the same `DecodedImage`.
- Image **encode** is out of scope here — that goes to the Skia layer later.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
